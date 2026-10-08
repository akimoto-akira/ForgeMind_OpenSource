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
                    .Select(p => new ScanParameterSignature(p.Name, p.Type))
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

        // Multi-candidate arbitration: level descending (engine-direct modules first). OrderByDescending is stable —
        // same-level entries preserve index order; no longer depends on List.Sort (results arbitrary for same Scope ties).
        ownerClass = candidates
            .OrderByDescending(d => (int)d.Class.Scope)
            .First().Class.Name;
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
{
    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        return true;

    (string fromBase, _, _) = DecomposeType(from);
    (string toBase, _, _) = DecomposeType(to);
    fromBase = CanonicalType(fromBase);
    toBase = CanonicalType(toBase);
    if (fromBase.Length == 0 || toBase.Length == 0)
        return true;
    if (fromBase.Equals(toBase, StringComparison.Ordinal))
        return true;

    if (IsNumericType(fromBase) &&
        (IsNumericType(toBase) || toBase.Equals("FString", StringComparison.Ordinal)))
        return true;

    // FString → FName: engine BlueprintAutocast auto-conversion (Conv_StringToName).
    // One-way exemption — FName → FString has Conv_NameToString but no call site exposed, not allowed.
    if (fromBase.Equals("FString", StringComparison.Ordinal) &&
        toBase.Equals("FName", StringComparison.Ordinal))
        return true;

    if (fromBase.Contains('<') || toBase.Contains('<'))
        return false;

        if (!IsKnownType(fromBase) || !IsKnownType(toBase))
            return true;

        return IsClassDerivedFrom(fromBase, toBase);
    }

    /// <summary>DSL / blueprint display type name → engine type name (positional arg types come from DSL declaration text, e.g. Vector / string).</summary>
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.Ordinal)
    {
        ["Vector"] = "FVector",
        ["Vector2D"] = "FVector2D",
        ["Rotator"] = "FRotator",
        ["Transform"] = "FTransform",
        ["Quat"] = "FQuat",
        ["string"] = "FString",
        ["name"] = "FName",
        ["text"] = "FText",
        ["int"] = "int32",
        ["long"] = "int64",
        ["short"] = "int16",
        ["byte"] = "uint8",
    };

    /// <summary>Numeric family (engine auto-conversion interop: byte / int / int64 / float / double).</summary>
    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64",
        "float", "double",
    };

    /// <summary>
    /// Known non-UObject value-type families (no implicit conversion between blueprint pins, base name match needed).
    /// Used for "do we recognize it" judgment — types not in this set pass through as "unknown".
    /// </summary>
    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "FString", "FText", "FName", "FVector", "FVector2D", "FVector4", "FIntPoint",
        "FRotator", "FQuat", "FTransform", "FLinearColor", "FColor", "FGuid", "FDateTime",
        "bool",
    };

    private static string CanonicalType(string baseName)
        => TypeAliases.TryGetValue(baseName, out string? canonical) ? canonical : baseName;

    private static bool IsNumericType(string baseName) => NumericTypes.Contains(baseName);

    /// <summary>Whether this base name is a type the judgment recognizes: numeric family / known value-type family / scan-indexed classes (incl. structs).</summary>
    private static bool IsKnownType(string baseName)
        => IsNumericType(baseName)
           || ValueTypes.Contains(baseName)
           || Self.FindClassesFlexible(baseName).Count > 0;

    /// <summary>Decompose type string → (base name, is pointer, is ref): strip trailing * / &amp; markers and const modifier, compress whitespace.</summary>
    private static (string Base, bool IsPointer, bool IsRef) DecomposeType(string type)
    {
        string t = type.Trim();
        bool isPointer = false, isRef = false;
        while (t.EndsWith("*"))
        {
            isPointer = true;
            t = t[..^1].TrimEnd();
        }
        while (t.EndsWith("&"))
        {
            isRef = true;
            t = t[..^1].TrimEnd();
        }
        if (t.StartsWith("const ", StringComparison.Ordinal))
            t = t["const ".Length..].Trim();
        t = string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (t, isPointer, isRef);
    }

    /// <summary>
    /// Whether from class derives from to class along base class chain (scan index;
    /// U/A/F/I/E prefix flexibly locates the starting point, base clause strips template params and walks up,
    /// visited set prevents cycles).
    /// </summary>
    private static bool IsClassDerivedFrom(string from, string to)
    {
        var store = ScanDataProvider.Current;
        // Visited set deduplicates by <b>entry itself</b> (reference identity), not by class name:
        // the index may contain multiple entries with the same name
        // (cross-module duplicate declarations, etc.). Dedup by name lets the first swallow all others —
        // if that entry is a baseless empty entry, the derivation chain breaks right there
        // (AActor was once judged "not derived from UObject" due to a leading pseudo-entry in the index).
        var visited = new HashSet<ScanClassView>();
        var queue = new Queue<ScanClassView>(store.FindClassesFlexible(from));
        while (queue.Count > 0)
        {
            var cls = queue.Dequeue();
            if (!visited.Add(cls))
                continue;

            foreach (string baseDecl in cls.AllBaseClasses)
            {
                string baseName = baseDecl.Trim();
                int lt = baseName.IndexOf('<');
                if (lt >= 0)
                    baseName = baseName[..lt].Trim();

                if (baseName.Equals(to, StringComparison.Ordinal))
                    return true;

                foreach (var next in store.FindClassesFlexible(baseName))
                    queue.Enqueue(next);
            }
        }

        return false;
    }
}
