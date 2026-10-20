// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Collections.ObjectModel;
using ForgeMind.Scan.Abstractions;

namespace ForgeMind.Scan;

/// <summary>
/// <see cref="IScanDataProvider"/> backed by an SPK pack (refactor plan §16).
/// <para>
/// The runtime opens an SPK file via <see cref="ScanPackReader"/>, loads the view data
/// (roots, classes, functions), and this provider serves queries from that in-memory
/// snapshot. Write-side methods (<see cref="EnqueueScan"/>) are no-ops — the pack is
/// read-only. Bridge-specific methods (parent member shape, event resolution) return
/// null/empty — that data lives in the bridge, not in the pack.</para>
/// </summary>
public sealed class SpkScanDataProvider : IScanDataProvider, IDisposable
{
    private const string PureSpecifier = "BlueprintPure";

    private readonly IReadOnlyList<ScanRootView> _roots;
    private readonly IReadOnlyList<ScanClassView> _classes;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ScanFunctionView>> _functionsByClass;
    private readonly Dictionary<string, ScanClassView> _classByName;
    private IDisposable? _ownedDisposable;

    /// <summary>Creates a provider from scan data loaded by <see cref="ScanPackReader"/>.</summary>
    public SpkScanDataProvider(IScanData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _roots = data.Roots;
        _classes = data.Classes;
        _functionsByClass = data.FunctionsByClass;
        _classByName = new Dictionary<string, ScanClassView>(StringComparer.Ordinal);
        foreach (ScanClassView cls in _classes)
            _classByName.TryAdd(cls.Name, cls);
    }

    /// <summary>
    /// Creates a provider from an SPK pack, loading only the first version namespace found in
    /// the pack (refactor plan §9 fix — no longer LoadAll across all versions). Owns the
    /// reader — dispose to release. For an explicit version scope, use
    /// <see cref="CreateVersionScope(ScanPackReader, string)"/>.
    /// </summary>
    public SpkScanDataProvider(ScanPackReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        IReadOnlyList<string> versions = reader.Versions;
        string version = versions.Count > 0 ? versions[0] : "";
        (_, IScanData data) = string.IsNullOrEmpty(version) ? reader.LoadAll() : reader.LoadVersion(version);
        _roots = data.Roots;
        _classes = data.Classes;
        _functionsByClass = data.FunctionsByClass;
        _classByName = new Dictionary<string, ScanClassView>(StringComparer.Ordinal);
        foreach (ScanClassView cls in _classes)
            _classByName.TryAdd(cls.Name, cls);
        _ownedDisposable = reader;
    }

    /// <summary>
    /// Creates a version-scoped provider from an SPK pack — only data from the given version
    /// namespace (e.g. "5.0") is loaded, so same-named classes from different versions never
    /// collide (refactor plan §8 fix). Owns the reader — dispose to release.
    /// </summary>
    public static SpkScanDataProvider CreateVersionScope(ScanPackReader reader, string version)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        (_, IScanData data) = reader.LoadVersion(version);
        return new SpkScanDataProvider(data)
        {
            _ownedDisposable = reader,
        };
    }

    /// <summary>
    /// Lists all version namespaces available in the pack, so the caller can pick one and
    /// call <see cref="CreateVersionScope(ScanPackReader, string)>. Returns an empty list when
    /// no version-scoped data exists.
    /// </summary>
    public static IReadOnlyList<string> ListVersions(ScanPackReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.Versions;
    }

    /// <inheritdoc />
    public void Dispose() => _ownedDisposable?.Dispose();

    // ===== IScanDataProvider =====

    /// <inheritdoc />
    public IReadOnlyList<ScanRootView> Roots => _roots;

    /// <inheritdoc />
    public bool HasIndexData => _classes.Count > 0;

    /// <summary>Number of classes held by this provider (0 means the pack carried no index).</summary>
    public int ClassCount => _classes.Count;

    /// <summary>Number of class → functions groups held by this provider.</summary>
    public int FunctionGroupCount => _functionsByClass.Count;

    /// <inheritdoc />
    public void EnqueueScan(string projectPath) { /* SPK is read-only — no live scans. */ }

    /// <inheritdoc />
    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name) =>
        FindClassesFlexibleCore(name);

    /// <inheritdoc />
    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name, string? projectPath) =>
        FindClassesFlexibleCore(name); // SPK data is already version-scoped; no project-path filtering.

    /// <inheritdoc />
    public IReadOnlyList<ScanFunctionView> CollectFunctions(string className) =>
        CollectFunctions(className, null);

    /// <inheritdoc />
    public IReadOnlyList<ScanFunctionView> CollectFunctions(string className, string? projectPath)
    {
        // Resolve the class (flexible), then return its own functions plus inherited ones.
        ScanClassView? cls = FindClassesFlexibleCore(className).FirstOrDefault();
        if (cls is null)
            return [];

        var result = new List<ScanFunctionView>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectOwnAndInherited(cls, result, seen);
        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanFunctionDeclarationView> FindFunctionDeclarations(string functionName)
    {
        var result = new List<ScanFunctionDeclarationView>();
        foreach (var (className, funcs) in _functionsByClass)
        {
            if (!_classByName.TryGetValue(className, out ScanClassView? cls))
                continue;
            foreach (ScanFunctionView fn in funcs)
            {
                if (string.Equals(fn.Name, functionName, StringComparison.Ordinal))
                    result.Add(new ScanFunctionDeclarationView(cls, fn));
            }
        }
        return result;
    }

    /// <inheritdoc />
    public string? ResolveEventFunctionName(string? name, string? projectPath) => null;

    /// <inheritdoc />
    public string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName) => null;

    /// <inheritdoc />
    public bool TryFindFunction(
        string owner, string method, int positionalCount,
        IReadOnlyList<string>? namedArguments, out ScanFunctionSignature? signature)
    {
        signature = null;
        if (!_functionsByClass.TryGetValue(owner, out IReadOnlyList<ScanFunctionView>? funcs))
            return false;

        foreach (ScanFunctionView fn in funcs)
        {
            if (!string.Equals(fn.Name, method, StringComparison.Ordinal))
                continue;
            // Match: positional count fits (trailing defaults may be omitted).
            if (fn.Parameters.Count >= positionalCount)
            {
                signature = new ScanFunctionSignature(
                    owner, fn.Name, fn.IsStatic,
                    fn.Parameters.Select(p => new ScanParameterSignature(p.Name, p.Type, p.IsOut)).ToList())
                {
                    IsPure = fn.IsPure,
                };
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public ScanOwnerLookup TryFindFunctionOwner(
        string method, IReadOnlyList<string?>? argumentTypes,
        string? expectedReturnType, out string ownerClass)
    {
        ownerClass = "";
        bool anyDeclaration = false;

        // Three passes collect every matching candidate; the winner is chosen by
        // PickBestOwner (Scope desc, then IsStatic desc, then stable index order).
        // First-match-wins (previous shape) picked whichever class the .spk dictionary
        // enumerated first -- for a bare call like `GetPlayerController()` this let an
        // unrelated non-static member (UCheatManager / APlayerState, both same Scope as
        // UGameplayStatics) beat the reachable static library entry, which the user
        // reported as "node identification error, duplicate name encountered" (run 20261019_1302 Camera).
        var friendlyMatches = new List<(string ClassName, ScanFunctionView Fn)>();
        var exactMatches = new List<(string ClassName, ScanFunctionView Fn)>();
        var prefixMatches = new List<(string ClassName, ScanFunctionView Fn)>();

        // Pass 1 (per user rule "query uses only two criteria: DisplayName and direct function name"): friendly alias
        // match on the UFUNCTION's DisplayName metadata (ScriptName / ScriptMethod are excluded
        // from the queryable pair). Scans every function's Metadata via
        // ScanFunctionView.MatchesFriendlyName. Signature filter uses
        // ScanFunctionView.ArgsFitParameters (bare-name compatible; distinguishes
        // FVector / FVector2D / FVector4 overloads that share the same "Dot_" prefix
        // candidate family). Same convergence point as the translator's ResolveMathFamilyName
        // fits filter (spec 8).
        foreach (var (className, funcs) in _functionsByClass)
        {
            foreach (ScanFunctionView fn in funcs)
            {
                if (!fn.MatchesFriendlyName(method))
                    continue;
                anyDeclaration = true;
                if (fn.FitsSignature(argumentTypes, expectedReturnType, FindClassesFlexibleCore))
                    friendlyMatches.Add((className, fn));
            }
        }
        if (PickBestOwner(friendlyMatches, out ownerClass))
            return ScanOwnerLookup.Resolved;

        // Pass 2: exact FName match (was Pass 1 before the friendly-alias priority landed).
        foreach (var (className, funcs) in _functionsByClass)
        {
            foreach (ScanFunctionView fn in funcs)
            {
                if (!string.Equals(fn.Name, method, StringComparison.Ordinal))
                    continue;
                anyDeclaration = true;
                if (fn.FitsSignature(argumentTypes, expectedReturnType, FindClassesFlexibleCore))
                    exactMatches.Add((className, fn));
            }
        }
        if (PickBestOwner(exactMatches, out ownerClass))
            return ScanOwnerLookup.Resolved;

        // Pass 3: prefix match for UE type-disambiguated function names (e.g. "Dot"
        // matches "Dot_VectorVector", "Add" matches "Add_VectorVector"). The DSL uses
        // the short operation name; the engine records overloaded math functions with
        // _TypeType suffixes. Only triggered when both name passes find nothing.
        if (!anyDeclaration)
        {
            string prefix = method + "_";
            foreach (var (className, funcs) in _functionsByClass)
            {
                foreach (ScanFunctionView fn in funcs)
                {
                    if (!fn.Name.StartsWith(prefix, StringComparison.Ordinal))
                        continue;
                    anyDeclaration = true;
                    if (fn.FitsSignature(argumentTypes, expectedReturnType, FindClassesFlexibleCore))
                        prefixMatches.Add((className, fn));
                }
            }
            if (PickBestOwner(prefixMatches, out ownerClass))
                return ScanOwnerLookup.Resolved;
        }

        return anyDeclaration ? ScanOwnerLookup.NoTypeMatch : ScanOwnerLookup.NoDeclaration;
    }

    /// <summary>
    /// Multi-candidate arbitration for TryFindFunctionOwner (four tiers, high to low):
    ///   1) Module kind -- a runtime-module declaration outranks an editor-module one. UE
    ///      rejects a call into an editor module from a runtime blueprint ("Cannot use
    ///      editor function ... in this runtime blueprint"), so an editor declaration can
    ///      never be the reachable answer. The bare call `GetDisplayName(h.HitActor)` bound
    ///      UContentBrowserItemLibrary (Engine/Source/Editor/ContentBrowserData, returns
    ///      FText) instead of UKismetSystemLibrary (Engine/Source/Runtime/Engine, returns
    ///      FString) and the asset failed to compile (runs 20261019_1854 / 20261019_2152).
    ///   2) Class.Scope descending -- engine-direct modules beat plugin-level matches.
    ///   3) Function.IsStatic descending -- a bare-name call has no receiver, so a static
    ///      library entry (UGameplayStatics::GetPlayerController) is the reachable form;
    ///      same-named non-static members on unrelated classes (UCheatManager /
    ///      APlayerState::GetPlayerController) need a self of that class and cannot serve
    ///      a bare call. Without this tier, four same-Scope engine-module candidates
    ///      resolve by dictionary iteration order (bug reported run 20261019_1302).
    ///   4) Stable original order (List is enumerated in _functionsByClass insertion order).
    /// OrderByDescending + ThenByDescending are documented as stable sorts.
    /// </summary>
    private bool PickBestOwner(List<(string ClassName, ScanFunctionView Fn)> matches, out string ownerClass)
    {
        ownerClass = "";
        if (matches.Count == 0)
            return false;

        List<(string ClassName, ScanFunctionView Fn)> ranked =
            [.. matches.OrderBy(m => IsEditorOnly(m.Fn) ? 1 : 0)];

        // Ranking is shared with ScanSignatureCatalog so both providers break
        // same-name ties identically (spec 8): runtime module first, then class
        // scope descending, then static over instance, then stable order.
        ownerClass = ScanOwnerArbiter.PickBest([.. matches.Select(
            m => (m.ClassName,
                  _classByName.TryGetValue(m.ClassName, out var cls) ? cls.Scope : 0,
                  m.Fn))]);
        return true;
    }

    /// <summary>
    /// Whether a declaration lives in an editor-only module — the gate UE applies when a
    /// blueprint is compiled for runtime (a UFUNCTION in an <c>Editor</c> module is rejected with
    /// "Cannot use editor function ... in this runtime blueprint"). The frozen index carries no
    /// explicit editor flag, so the declaring file path is the authoritative record: UE keeps
    /// editor modules under <c>&lt;Root&gt;/Source/Editor/</c> and runtime ones under
    /// <c>&lt;Root&gt;/Source/Runtime/</c>.
    /// </summary>
    private static bool IsEditorOnly(ScanFunctionView fn)
        => ScanTypeCompatibility.IsEditorOnlyPath(fn.FilePath);

    /// <inheritdoc />
    public ScanParentMemberKind? GetParentMemberKind(string parentRef, string member) => null;

    /// <inheritdoc />
    public string? GetParentGeneratedClass(string parentRef) => null;

    // ===== helpers =====

    private IReadOnlyList<ScanClassView> FindClassesFlexibleCore(string name)
    {
        // Exact match first, then U/A/F/I/E prefix tolerance (same logic as ScanIndexStore).
        var result = new List<ScanClassView>();
        foreach (ScanClassView cls in _classes)
            if (string.Equals(cls.Name, name, StringComparison.Ordinal))
                result.Add(cls);
        if (result.Count > 0)
            return result;

        foreach (string prefix in new[] { "U", "A", "F", "I", "E" })
        {
            string prefixed = prefix + name;
            foreach (ScanClassView cls in _classes)
                if (string.Equals(cls.Name, prefixed, StringComparison.Ordinal))
                    result.Add(cls);
            if (result.Count > 0)
                return result;
        }
        return [];
    }

    private void CollectOwnAndInherited(ScanClassView cls, List<ScanFunctionView> result, HashSet<string> seen)
    {
        // Own functions.
        if (_functionsByClass.TryGetValue(cls.Name, out IReadOnlyList<ScanFunctionView>? own))
        {
            foreach (ScanFunctionView fn in own)
            {
                if (seen.Add(fn.Name + "/" + fn.Parameters.Count))
                    result.Add(fn);
            }
        }

        // Inherited — walk base classes.
        foreach (string baseName in cls.AllBaseClasses)
        {
            if (_classByName.TryGetValue(baseName, out ScanClassView? baseCls))
                CollectOwnAndInherited(baseCls, result, seen);
        }
    }
}