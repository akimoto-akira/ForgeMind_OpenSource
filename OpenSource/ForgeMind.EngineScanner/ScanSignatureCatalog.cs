// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMind.Scan.Unreal;
using ForgeMindServer.Abstractions;


namespace ForgeMind.EngineScanner;

/// <summary>
/// Scan index catalog data source: connects the documentation engine's scan index (ScanIndexStore)
/// into <see cref="DomainCatalogProvider"/>; translator signature / pin queries are resolved here.
/// <para>
/// Function queries use <b>inheritance aggregation</b> (subclass same-name wins; unresolved parent class collapses, no error);
/// when the index is empty (not scanned / not connected), <see cref="TryLoad"/> returns false and the translator falls back to relaxed mode.
/// <see cref="TryLoad"/> is also the scan trigger point for query behavior: project paths enter the scan queue (idempotent, async processing).
/// </para>
/// </summary>
internal class ScanSignatureCatalog
{
    /// <summary>Singleton.</summary>
    public static ScanSignatureCatalog Instance { get; } = new();

    private static IScanDataProvider Self => ScanIndexDataProvider.Instance!;

    protected ScanSignatureCatalog()
    {
    }

    /// <inheritdoc />
    public bool HasIndexData => Self.Roots.Count > 0;

    /// <inheritdoc />
    public bool TryFindFunction(string owner, string method, int positionalCount,
                                IReadOnlyList<string>? namedArguments, out ScanFunctionSignature? signature)
    {
        signature = null;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(method))
            return false;

        // Class lookup (with U / A / F / I / E prefix compatibility) + locate function in inherited aggregated members.
        // When function name misses, retry with "K2_" prefix (engine convention: K2_GetActorLocation etc.
        // blueprint wrapper functions have K2_ prefix in real FName; editor display name omits it).
        foreach (ScanClassView cls in ScanDataProvider.Current
                     .FindClassesFlexible(owner))
        {
            var functions = Self.CollectFunctions(cls.Name);
            ScanFunctionView? function = functions
                .FirstOrDefault(f => f.Name.Equals(method, StringComparison.OrdinalIgnoreCase));
            if (function is null)
                function = functions
                    .FirstOrDefault(f => f.Name.Equals("K2_" + method, StringComparison.OrdinalIgnoreCase));
            if (function is null)
                continue;

            // Match rules: positional arg count must not exceed total param count (trailing default params omitted); named arg names must exist
            if (positionalCount > function.Parameters.Count)
                continue;

            HashSet<string> parameterNames = function.Parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (namedArguments is not null && namedArguments.Any(n => !parameterNames.Contains(n)))
                continue;

            signature = new ScanFunctionSignature(
                cls.Name,
                function.Name,
                function.IsStatic,
                function.Parameters
                    .Select(p => new ScanParameterSignature(p.Name, p.Type, p.IsOut))
                    .ToList())
            {
                // BlueprintPure = pure function (no exec pin, not on execution chain) —
                // the translator decides based on this whether to chain the call node into the execution flow
                // (VInterpTo is pure vs CreateSaveGameObject is impure; Save_Create_B's entry → create
                // execution chain is determined by this).
                IsPure = function.IsPure,
            };
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public ScanOwnerLookup TryFindFunctionOwner(string method, IReadOnlyList<string?>? argumentTypes,
                                                    string? expectedReturnType, out string ownerClass)
    {
        ownerClass = null!;

        if (string.IsNullOrWhiteSpace(method))
            return ScanOwnerLookup.NoDeclaration;

        // Precise reverse lookup of all declaration sites; retry with "K2_" prefix when function name misses (engine blueprint wrapper function convention).
        var declarations = Self.FindFunctionDeclarations(method);
        if (declarations.Count == 0)
            declarations = Self.FindFunctionDeclarations("K2_" + method);
        if (declarations.Count == 0)
        {
            // No matching declaration in index: not an engine/library function — relaxed fallback (local blueprint function,
            // e.g. GetCurrent has no corresponding engine placeholder method); strictly differentiated from "declared but no match".
            return ScanOwnerLookup.NoDeclaration;
        }

        // Call-site type matching (determine call-site type first, then filter same-name declarations):
        // positional arg count must not exceed total param count (trailing default params omitted);
        // each known arg type must be compatible; expected return type must be compatible.
        // Unknown type (null) acts as wildcard.
        // Static library first param WorldContextObject is auto-connected by engine to self, not counted as positional arg —
        // same convention as translator pin alignment (ExtractLiteralProperties / ExpectedArgType):
        // positional args start aligning after it (PrintString("x")'s FString aligns with InString,
        // not WorldContextObject). Per-declaration judgment (first param of same-name declarations may not all be WorldContext).
        int argCount = argumentTypes?.Count ?? 0;
        var candidates = declarations.Where(d =>
        {
            var ps = d.Function.Parameters;
            int offset = ps.Count > 0 &&
                         ps[0].Name.Equals("WorldContextObject", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            if (argCount + offset > ps.Count)
                return false;
            for (int i = 0; i < argCount; i++)
            {
                if (!IsTypeCompatible(argumentTypes![i], ps[i + offset].Type))
                    return false;
            }
            return IsTypeCompatible(d.Function.ReturnType, expectedReturnType);
        }).ToList();

        if (candidates.Count == 0)
            return ScanOwnerLookup.NoTypeMatch;

        // Multi-candidate arbitration is delegated to the shared arbiter so the
        // live-scan path ranks identically to the frozen-index path (spec 8).
        ownerClass = ScanOwnerArbiter.PickBest([.. candidates.Select(
            d => (d.Class.Name, (int)d.Class.Scope, d.Function))]);
        return ScanOwnerLookup.Resolved;
    }

/// <summary>
/// UE-semantic type compatibility judgment (argument → parameter / declared return → expected return):
/// either side unknown (null / empty) passes through; base name match means compatible (const T&amp; accepts T value).
/// When base names differ, decide in this order:
/// <list type="number">
/// <item>Engine blueprint auto-conversion (Conv_ node auto-inserted by editor on wire, all with BlueprintAutocast
/// mark): numeric family internal inter conversion, numeric → FString (verified: when int / float wired to InString, editor
/// auto-inserts "Convert to (Integer)/(Float) String"), FString → FName (Conv_StringToName,
/// KismetStringLibrary.h:85 "To Name (String)" — level streaming
/// LoadStreamLevel("Sub", ...)'s LevelName(FName) is exactly this scenario).</item>
/// <item>Template / container types (TArray&lt;...&gt; etc.) require base name match; no derived inference.</item>
/// <item>Either side is a type the judgment does not recognize (DSL type name, index-not-included struct) passes through —
/// unrecognized types cannot be judged, no guessing, no blocking.</item>
/// <item>Both sides recognized: walk scan index base class chain to judge derived compatibility (AActor* → const UObject*);
/// FString / FText / FName and other known value-type families are mutually incompatible — no implicit conversion between blueprint pins
/// (only exception: the previous FString → FName engine auto-conversion).</item>
/// </list>
/// </summary>
private static bool IsTypeCompatible(string? from, string? to)
    => ScanTypeCompatibility.IsCompatible(from, to, Self.FindClassesFlexible);

}
