// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.Scan;

/// <summary>
/// 从 SPK 包里读扫描数据（refactor plan §16）。包内布局就是历史 ScanCache 的布局：
/// <code>
/// {version}/engine.json     — 引擎侧代码扫描的 ScanRoot 树
/// {version}/assets.json     — 引擎侧资产扫描的 ScanRoot 树
/// </code>
/// <para>
/// 包里存的是<b>扫描算法原本产物的树</b>（含层级、属性、说明符、元数据）。这里在读取时把它
/// 投影成契约视图 <see cref="ScanRootView"/> / <see cref="ScanClassView"/> /
/// <see cref="ScanFunctionView"/> —— 对外契约不变，消费方（含
/// <see cref="SpkScanDataProvider"/>）无感知，但底层数据不再被提前拍平丢失。</para>
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
    /// ScanCache 布局没有 manifest 条目——历史 ScanCache 里就没有这个文件，所以恒为 null。
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

    // ── 树 → 视图 ──

    /// <summary>root 自己的 modules，加上每个 plugin 下的 modules —— 与历史遍历一致。</summary>
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
            fn.Specifiers?.Contains("BlueprintPure", StringComparer.OrdinalIgnoreCase) ?? false,
            fn.Parameters?.Select(p => new ScanParameterView(p.Name, p.Type)).ToArray() ?? [],
            fn.FilePath,
            fn.LineNumber,
            fn.ReturnType);

    private byte[]? ReadOrNull(string path)
    {
        if (!_directory.TryGetValue(path, out SpkEntry entry))
            return null;
        return _reader.ReadAt(entry.Offset, entry.Length);
    }

    // ── ScanCache JSON 形状（只取投影需要的字段；未列出的字段仍存在于包内，不解析而已）──

    /// <summary><c>engine.json</c> 的根元素。</summary>
    public sealed class DtoRoot
    {
        public string RootPath { get; set; } = "";
        public string Version { get; set; } = "";
        public int Scope { get; set; }
        public List<DtoPlugin>? Plugins { get; set; }
        public List<DtoModule>? Modules { get; set; }
    }

    /// <summary><c>engine.json</c> 的插件节点。</summary>
    public sealed class DtoPlugin
    {
        public string Name { get; set; } = "";
        public List<DtoModule>? Modules { get; set; }
    }

    /// <summary><c>engine.json</c> 的模块节点。</summary>
    public sealed class DtoModule
    {
        public string Name { get; set; } = "";
        public List<DtoClass>? Classes { get; set; }
    }

    /// <summary><c>engine.json</c> 的类节点。</summary>
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

    /// <summary><c>engine.json</c> 的函数节点。</summary>
    public sealed class DtoFunction
    {
        public string Name { get; set; } = "";
        public bool IsStatic { get; set; }
        public string? ReturnType { get; set; }
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public List<string>? Specifiers { get; set; }
        public List<DtoParameter>? Parameters { get; set; }
    }

    /// <summary><c>engine.json</c> 的参数节点。</summary>
    public sealed class DtoParameter
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
    }
}
