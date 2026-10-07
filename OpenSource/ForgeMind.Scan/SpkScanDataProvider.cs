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
                    fn.Parameters.Select(p => new ScanParameterSignature(p.Name, p.Type)).ToList())
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

        foreach (var (className, funcs) in _functionsByClass)
        {
            foreach (ScanFunctionView fn in funcs)
            {
                if (!string.Equals(fn.Name, method, StringComparison.Ordinal))
                    continue;
                anyDeclaration = true;
                if (argumentTypes is null || argumentTypes.Count <= fn.Parameters.Count)
                {
                    ownerClass = className;
                    return ScanOwnerLookup.Resolved;
                }
            }
        }
        return anyDeclaration ? ScanOwnerLookup.NoTypeMatch : ScanOwnerLookup.NoDeclaration;
    }

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