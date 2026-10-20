// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.Scan;

/// <summary>
/// Reads scan data from an SPK pack (refactor plan §16). The on-disk layout mirrors the historical ScanCache layout:
/// <code>
/// {version}/engine.json     — engine-side code scan ScanRoot tree
/// {version}/assets.json     — engine-side asset scan ScanRoot tree
/// </code>
/// <para>
/// The pack stores the <b>raw product tree of the scan algorithm</b> (hierarchy, properties, specifiers, metadata). On read it is
/// projected into the contract views <see cref="ScanRootView"/> / <see cref="ScanClassView"/> /
/// <see cref="ScanFunctionView"/> — the public contract stays unchanged, consumers (including
/// <see cref="SpkScanDataProvider"/>) are unaware, but the underlying data is no longer flattened prematurely and lost.</para>
/// </summary>
public sealed class ScanPackReader : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPackReader _reader;
    private readonly IReadOnlyDictionary<string, SpkEntry> _directory;

    /// <summary>Opens the pack and reads the directory.</summary>
    public ScanPackReader(IPackReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _directory = reader.ReadDirectory();
    }

    /// <summary>All version namespaces found in the pack (e.g. "5.0", "5.8").</summary>
    public IReadOnlyList<string> Versions
    {
        get
        {
            var versions = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in _directory.Keys)
            {
                int slash = path.IndexOf('/');
                if (slash > 0 && slash < path.Length - 1
                    && path.AsSpan(slash + 1).Equals("engine.json".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    versions.Add(path[..slash]);
                }
            }
            return versions.ToList();
        }
    }

    /// <summary>
    /// ScanCache layout has no manifest entry — historical ScanCache never had this file, so always null.
    /// </summary>
    public ScanManifest? ReadManifest() => null;

    /// <summary>Reads one version's engine tree, or empty when absent.</summary>
    public IReadOnlyList<DtoRoot> ReadEngineRoots(string version)
    {
        byte[]? json = ReadOrNull($"{version}/engine.json");
        if (json == null) return [];
        return JsonSerializer.Deserialize<List<DtoRoot>>(json, _jsonOptions) ?? [];
    }

    /// <summary>Reads roots as contract views for one version.</summary>
    public IReadOnlyList<ScanRootView> ReadRoots(string version)
    {
        return ReadEngineRoots(version)
            .Select(r => new ScanRootView(
                r.RootPath,
                r.Version,
                r.Modules?.Select(m => m.Name).ToArray() ?? [],
                r.Plugins?.Select(p => p.Name).ToArray() ?? []))
            .ToList();
    }

    /// <summary>Reads every class of one version, flattened out of the tree.</summary>
    public IReadOnlyList<ScanClassView> ReadClasses(string version)
    {
        var result = new List<ScanClassView>();
        foreach (DtoRoot root in ReadEngineRoots(version))
        {
            foreach (DtoModule module in EnumerateModules(root))
                foreach (DtoClass cls in module.Classes ?? [])
                    result.Add(ToView(cls));
        }
        return result;
    }

    /// <summary>Reads functions grouped by class name for one version.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ScanFunctionView>> ReadFunctionsByClass(string version)
    {
        var result = new Dictionary<string, List<ScanFunctionView>>(StringComparer.Ordinal);
        foreach (DtoRoot root in ReadEngineRoots(version))
        {
            foreach (DtoModule module in EnumerateModules(root))
            {
                foreach (DtoClass cls in module.Classes ?? [])
                {
                    if (cls.Functions is not { Count: > 0 } functions)
                        continue;

                    if (!result.TryGetValue(cls.Name, out List<ScanFunctionView>? list))
                    {
                        list = new List<ScanFunctionView>();
                        result[cls.Name] = list;
                    }

                    foreach (DtoFunction fn in functions)
                        list.Add(ToView(fn));
                }
            }
        }

        return result.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<ScanFunctionView>)kvp.Value,
            StringComparer.Ordinal);
    }

    /// <summary>Reads roots across every version namespace.</summary>
    public IReadOnlyList<ScanRootView> ReadRoots() =>
        Versions.SelectMany(ReadRoots).ToList();

    /// <summary>Reads classes across every version namespace.</summary>
    public IReadOnlyList<ScanClassView> ReadClasses() =>
        Versions.SelectMany(ReadClasses).ToList();

    /// <summary>Reads functions grouped by class name across every version namespace.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ScanFunctionView>> ReadFunctionsByClass()
    {
        var result = new Dictionary<string, List<ScanFunctionView>>(StringComparer.Ordinal);
        foreach (string version in Versions)
        {
            foreach (var (className, funcs) in ReadFunctionsByClass(version))
            {
                if (!result.TryGetValue(className, out List<ScanFunctionView>? list))
                {
                    list = new List<ScanFunctionView>();
                    result[className] = list;
                }
                list.AddRange(funcs);
            }
        }

        return result.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<ScanFunctionView>)kvp.Value,
            StringComparer.Ordinal);
    }

    /// <summary>Loads scan data for a single version.</summary>
    public (ScanManifest? Manifest, IScanData Data) LoadVersion(string version)
    {
        var data = new ScanData();
        data.RootList.AddRange(ReadRoots(version));
        data.ClassList.AddRange(ReadClasses(version));

        foreach (var (className, funcs) in ReadFunctionsByClass(version))
            data.FunctionMap[className] = funcs.ToList();

        return (ReadManifest(), data);
    }

    /// <summary>Loads all scan data in one call across every version.</summary>
    public (ScanManifest? Manifest, IScanData Data) LoadAll()
    {
        var data = new ScanData();
        data.RootList.AddRange(ReadRoots());
        data.ClassList.AddRange(ReadClasses());

        foreach (var (className, funcs) in ReadFunctionsByClass())
            data.FunctionMap[className] = funcs.ToList();

        return (ReadManifest(), data);
    }

    /// <inheritdoc />
    public void Dispose() => _reader.Dispose();

    // ── tree → view ──

    /// <summary>The root's own modules plus the modules under each plugin — matches historical traversal.</summary>
    private static IEnumerable<DtoModule> EnumerateModules(DtoRoot root)
    {
        foreach (DtoModule module in root.Modules ?? [])
            yield return module;

        foreach (DtoPlugin plugin in root.Plugins ?? [])
            foreach (DtoModule module in plugin.Modules ?? [])
                yield return module;
    }

    private static ScanClassView ToView(DtoClass cls) =>
        new(
            cls.Name,
            cls.BaseClasses is { Count: > 0 } ? cls.BaseClasses[0] : null,
            cls.BaseClasses?.ToArray() ?? [],
            cls.Scope);

    private static ScanFunctionView ToView(DtoFunction fn) =>
        new(
            fn.Name,
            fn.IsStatic,
            IsPureFromSpecifiers(fn.Specifiers),
            fn.Parameters?.Select(p => new ScanParameterView(p.Name, p.Type, p.IsOut)).ToArray() ?? [],
            fn.FilePath,
            fn.LineNumber,
            fn.ReturnType,
            // Scanner writes raw meta=(K="V", ...) as a single "meta" string entry; the contract
            // view is documented as pre-flattened. Normalize() unpacks it (and is idempotent on
            // already-flattened packs, so no rebuild is required).
            FunctionMetadataNormalizer.Normalize(fn.Metadata));

    /// <summary>
    /// BlueprintPure → pure; const BlueprintCallable → also pure (engine generates no exec
    /// pins for const BlueprintCallable, same as BlueprintPure — e.g. UCurveFloat::GetFloatValue).
    /// </summary>
    private static bool IsPureFromSpecifiers(List<string>? specs)
    {
        if (specs is null) return false;
        return specs.Contains("BlueprintPure", StringComparer.OrdinalIgnoreCase)
            || (specs.Contains("const", StringComparer.OrdinalIgnoreCase)
                && specs.Contains("BlueprintCallable", StringComparer.OrdinalIgnoreCase));
    }

    private byte[]? ReadOrNull(string path)
    {
        if (!_directory.TryGetValue(path, out SpkEntry entry))
            return null;
        return _reader.ReadAt(entry.Offset, entry.Length);
    }

    // ── ScanCache JSON shape (only fields needed for the projection are listed; other fields still exist in the pack but are not parsed) ──

    /// <summary>Root element of <c>engine.json</c>.</summary>
    public sealed class DtoRoot
    {
        public string RootPath { get; set; } = "";
        public string Version { get; set; } = "";
        public int Scope { get; set; }
        public List<DtoPlugin>? Plugins { get; set; }
        public List<DtoModule>? Modules { get; set; }
    }

    /// <summary>Plugin node of <c>engine.json</c>.</summary>
    public sealed class DtoPlugin
    {
        public string Name { get; set; } = "";
        public List<DtoModule>? Modules { get; set; }
    }

    /// <summary>Module node of <c>engine.json</c>.</summary>
    public sealed class DtoModule
    {
        public string Name { get; set; } = "";
        public List<DtoClass>? Classes { get; set; }
    }

    /// <summary>Class node of <c>engine.json</c>.</summary>
    public sealed class DtoClass
    {
        public string Name { get; set; } = "";
        public int Kind { get; set; }
        public int Scope { get; set; }
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public List<string>? BaseClasses { get; set; }
        public List<string>? Specifiers { get; set; }
        public List<DtoFunction>? Functions { get; set; }
    }

    /// <summary>Function node of <c>engine.json</c>.</summary>
    public sealed class DtoFunction
    {
        public string Name { get; set; } = "";
        public bool IsStatic { get; set; }
        public string? ReturnType { get; set; }
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public List<string>? Specifiers { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
        public List<DtoParameter>? Parameters { get; set; }
    }

    /// <summary>Parameter node of <c>engine.json</c>.</summary>
    public sealed class DtoParameter
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }

        /// <summary>
        /// Non-const reference parameter -> Blueprint output pin. Absent in packs written
        /// before out-parameter capture, in which case it defaults to false (input pin).
        /// </summary>
        public bool IsOut { get; set; }
    }
}
