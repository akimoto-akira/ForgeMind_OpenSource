// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Unified scan-data contract — read side.
/// <para>Engine-agnostic by design: it surfaces <see cref="ScanClassView"/> /
/// <see cref="ScanFunctionView"/> / <see cref="ScanRootView"/> views, never a
/// particular engine's scan model. Consumers (function C / DSL, any plugin) program
/// against this contract, so the implementation behind it can change — or move out of
/// the host entirely — without touching consumers.</para>
/// <para>Two data sources back it: SPK data (EngineScanner-generated, read via
/// <see cref="ScanPackLoader"/>) and bridge reflection report (primary).</para>
/// </summary>
public interface IScanDataProvider
{
    /// <summary>Snapshot of every scanned root currently known.</summary>
    IReadOnlyList<ScanRootView> Roots { get; }

    /// <summary>Queues a project scan. Idempotent and asynchronous — never blocks.</summary>
    void EnqueueScan(string projectPath);

    /// <summary>Finds classes by name, tolerating engine prefixes.</summary>
    IReadOnlyList<ScanClassView> FindClassesFlexible(string name);

    /// <summary>Finds classes by name within the scope resolved from a project path.</summary>
    IReadOnlyList<ScanClassView> FindClassesFlexible(string name, string? projectPath);

    /// <summary>Collects the functions of a class (by name), including inherited ones.</summary>
    IReadOnlyList<ScanFunctionView> CollectFunctions(string className);

    /// <summary>Finds every declaration site of a function name (exact match).</summary>
    IReadOnlyList<ScanFunctionDeclarationView> FindFunctionDeclarations(string functionName);

    /// <summary>Resolves a DSL event name to the engine-side function name.</summary>
    string? ResolveEventFunctionName(string? name, string? projectPath);

    /// <summary>Resolves a DSL event name, bounded to a parent class.</summary>
    string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName);

    /// <summary>
    /// Whether any scan data is available at all. A failed lookup means something different
    /// depending on this: no data at all is a lenient fallback, whereas data that is present
    /// but does not match is a real error.
    /// </summary>
    bool HasIndexData { get; }

    /// <summary>
    /// Finds a function signature by owner type + function name + argument shape, tolerating
    /// omitted trailing default parameters.
    /// </summary>
    /// <param name="owner">Type name, with or without its engine prefix.</param>
    /// <param name="method">Function name.</param>
    /// <param name="positionalCount">Number of positional arguments at the call site.</param>
    /// <param name="namedArguments">Named argument names; may be null or empty.</param>
    /// <param name="signature">The signature when found; otherwise null.</param>
    /// <returns>True when found.</returns>
    bool TryFindFunction(
        string owner, string method, int positionalCount,
        IReadOnlyList<string>? namedArguments, out ScanFunctionSignature? signature);

    /// <summary>
    /// Resolves which type declares a called function (for receiver-less this-calls): every
    /// same-named declaration is enumerated, then filtered by positional argument types and the
    /// expected return type; among several candidates the one closest to the engine wins.
    /// </summary>
    /// <param name="method">Function name.</param>
    /// <param name="argumentTypes">Positional argument types; null entries act as wildcards.</param>
    /// <param name="expectedReturnType">Expected return type at the call site; null when none.</param>
    /// <param name="ownerClass">Declaring type name when resolved.</param>
    /// <returns>Resolved / <see cref="ScanOwnerLookup.NoDeclaration"/> (lenient) /
    /// <see cref="ScanOwnerLookup.NoTypeMatch"/> (caller should error). The distinction matters:
    /// "no such name" and "such a name that does not fit" call for opposite handling.</returns>
    ScanOwnerLookup TryFindFunctionOwner(
        string method, IReadOnlyList<string?>? argumentTypes,
        string? expectedReturnType, out string ownerClass);

    /// <summary>
    /// Member shape of a parent blueprint asset as last reported by the bridge, or null when
    /// unknown.
    /// </summary>
    ScanParentMemberKind? GetParentMemberKind(string parentRef, string member);

    /// <summary>
    /// Generated class of a reported parent blueprint asset (<c>&lt;AssetName&gt;_C</c>),
    /// or null when it was not reported.
    /// </summary>
    string? GetParentGeneratedClass(string parentRef);
}