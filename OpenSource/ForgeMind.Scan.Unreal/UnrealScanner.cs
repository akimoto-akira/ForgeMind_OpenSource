// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMindServer;
using ForgeMindServer.Engine;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// Unreal scanner provider — adapts the UE-specific scanning pipeline (DocumentEngine →
/// UeDocumentEngine → Ue5DocumentEngine) to the generic <see cref="IScanner"/> contract
/// (refactor plan §10/§12).
/// <para>
/// The Core / Scan framework and every consumer program against <see cref="IScanner"/> /
/// <see cref="IScanDataProvider"/>, never against the UE-specific types in this assembly.
/// A future scanner provider (Unity / Godot / custom) implements the same contract without
/// touching Core — that is the通用化 hardening target (refactor plan §37).</para>
/// </summary>
public sealed class UnrealScanner : IScanner
{
    private const string PureSpecifier = "BlueprintPure";

    /// <summary>Stable scanner identifier (surfaced in <see cref="ScanManifest.ScannerId"/>).</summary>
    public string ScannerId => "ForgeMind.Scan.Unreal";

    /// <summary>Scanner implementation version.</summary>
    public string ScannerVersion => "1.0.0";

    /// <summary>
    /// Discovers installed Unreal Engine targets on this machine (Epic Launcher installs +
    /// registry source builds). Best effort — returns an empty list when no engine is found
    /// or detection fails, never throws.
    /// </summary>
    public IReadOnlyList<ScanTargetDescriptor> DiscoverTargets()
    {
        try
        {
            UnrealEngineInstallInfo[] engines = UnrealEngineLocator.GetAllUnrealApplications();
            return engines
                .Select(e => new ScanTargetDescriptor
                {
                    TargetType = "UnrealEngine",
                    TargetId = e.AppVersion,
                    DisplayName = e.AppName,
                })
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Runs the UE scan lifecycle (discover → scan → normalize) over one target and returns
    /// a version-isolated <see cref="IScanResult"/>.
    /// <para>
    /// The target's <see cref="IScanTarget.Sources"/> carry the engine directory; the scan
    /// delegates to <see cref="Ue5DocumentEngine"/> for the actual extraction, then normalizes
    /// the UE-specific <see cref="ScanRoot"/> graph into generic <see cref="ScanEntry"/> records
    /// grouped by version namespace.</para>
    /// </summary>
    public IScanResult Scan(IScanTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Resolve the engine directory from the target's sources (kind "directory").
        string? engineDir = ResolveEngineRoot(
            target.Sources.FirstOrDefault(s => s.Kind == "directory")?.Identifier);
        string version = target.Descriptor.TargetId;
        // Root-relative path anchor for suppressing machine-specific directory prefixes (refactor plan §6/§12).
        string engineRoot = !string.IsNullOrEmpty(engineDir ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ? engineDir!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : "";

        var manifest = new ScanManifest
        {
            ScannerId = ScannerId,
            ScannerVersion = ScannerVersion,
            TargetType = target.Descriptor.TargetType,
            TargetVersions = [version],
            DataTypes = [ScanDataTypes.Code, ScanDataTypes.Asset, ScanDataTypes.Signature],
        };

        var index = new ScanIndex();
        var data = new ScanData();

        if (string.IsNullOrEmpty(engineDir) || !Directory.Exists(engineDir))
            return new UnrealScanResult(manifest, index, data);

        cancellationToken.ThrowIfCancellationRequested();

        var install = new DirectoryInfo(engineDir);
        var engine = new Ue5DocumentEngine(install, install, version);

        // Engine code scan → code entries + view data
        foreach (ScanRoot root in engine.ScanEngineCode())
            AddRootEntries(index, data, root, ScanDataTypes.Code, engineRoot);

        cancellationToken.ThrowIfCancellationRequested();

        // Engine asset scan → asset entries + view data
        foreach (ScanRoot root in engine.ScanEngineAssets())
            AddRootEntries(index, data, root, ScanDataTypes.Asset, engineRoot);

        return new UnrealScanResult(manifest, index, data);
    }

    /// <summary>
    /// 跑同一套 UE 扫描生命周期，但<b>不</b>把结果规范化成视图：原样返回算法产出的
    /// <see cref="ScanRoot"/> 树，供 <see cref="ScanCachePackWriter"/> 以 ScanCache 格式打包。
    /// <para>
    /// 扫描算法本身没有问题（历史 25+ 用例是在它上面跑过的），问题在于
    /// <see cref="Scan"/> 把树拍平成视图后丢掉了层级、属性、说明符与元数据。打包走这条路，
    /// 包里装的就是算法原本的产物。</para>
    /// </summary>
    public (string Version, IReadOnlyList<ScanRoot> CodeRoots, IReadOnlyList<ScanRoot> AssetRoots)
        ScanRoots(IScanTarget target, CancellationToken cancellationToken = default)
    {
        string? engineDir = ResolveEngineRoot(
            target.Sources.FirstOrDefault(s => s.Kind == "directory")?.Identifier);
        string version = target.Descriptor.TargetId;

        if (string.IsNullOrEmpty(engineDir) || !Directory.Exists(engineDir))
            return (version, [], []);

        var install = new DirectoryInfo(engineDir);
        var engine = new Ue5DocumentEngine(install, install, version);

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ScanRoot> codeRoots = engine.ScanEngineCode().ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ScanRoot> assetRoots = engine.ScanEngineAssets().ToArray();

        return (version, codeRoots, assetRoots);
    }

    private static void AddRootEntries(ScanIndex index, ScanData data, ScanRoot root, string dataType, string engineRoot)
    {
        // Root view — one per scan scope.
        data.RootList.Add(new ScanRootView(
            root.RootPath,
            root.Version,
            root.Modules.Select(m => m.Name).ToArray(),
            root.Plugins.Select(p => p.Name).ToArray()));

        // Each module's classes become addressable entries + view data.
        foreach (ModuleInfo module in root.Modules)
            AddModuleEntries(index, data, root.Version, module, dataType, engineRoot);

        foreach (PluginInfo plugin in root.Plugins)
            foreach (ModuleInfo module in plugin.Modules)
                AddModuleEntries(index, data, root.Version, module, dataType, engineRoot);
    }

    private static void AddModuleEntries(ScanIndex index, ScanData data, string version, ModuleInfo module, string dataType, string engineRoot)
    {
        foreach (UeClassInfo cls in module.Classes)
        {
            index.Add(new ScanEntry
            {
                Id = $"code/classes/{cls.Name}",
                Version = version,
                DataType = dataType,
                RelativePath = MakeRelative(cls.FilePath, engineRoot),
            });

            // Class view.
            data.ClassList.Add(new ScanClassView(
                cls.Name,
                cls.BaseClasses.Count > 0 ? cls.BaseClasses[0] : null,
                cls.BaseClasses.ToArray(),
                (int)cls.Scope));

            // Function views grouped by class name.
            if (cls.Functions.Count > 0)
            {
                var funcViews = cls.Functions
                    .Select(ToFunctionView)
                    .ToList();
                data.FunctionMap[cls.Name] = funcViews;
            }
        }
    }

    /// <summary>
    /// 引擎扫描根 = <c>{InstallLocation}\Engine</c>，不是安装目录本身。
    /// <para>
    /// 安装目录下既没有 <c>Source</c> 也没有 <c>Plugins</c>——它们都在 <c>Engine\</c> 子目录里。
    /// 传安装目录会让 <c>RulesFileScanner.GetEngineSourceRoots</c> 与插件发现全部落空，
    /// 结果就是一个没有任何模块的空 root（历史上 25+ 用例跑通时的 root 可不是空的）。
    /// 已经是 <c>...\Engine</c> 结尾（源码构建的注册项常直接指向它）时原样返回。</para>
    /// </summary>
    private static string? ResolveEngineRoot(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
            return null;

        string trimmed = installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.EndsWith("Engine", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        string candidate = Path.Combine(trimmed, "Engine");
        return Directory.Exists(candidate) ? candidate : trimmed;
    }

    private static ScanFunctionView ToFunctionView(UeFunctionInfo fn) =>
        new(
            fn.Name,
            fn.IsStatic,
            fn.Specifiers.Contains(PureSpecifier, StringComparer.OrdinalIgnoreCase),
            fn.Parameters.Select(p => new ScanParameterView(p.Name, p.Type)).ToArray(),
            fn.FilePath,
            fn.LineNumber,
            fn.ReturnType);

    /// <summary>
    /// Converts an absolute file path into a path relative to the engine root so the
    /// packaged SPK does not leak machine-specific install directories
    /// (refactor plan §6/§12). Returns the original path verbatim when it is not rooted
    /// under <paramref name="engineRoot"/>.
    /// </summary>
    internal static string MakeRelative(string filePath, string engineRoot)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(engineRoot))
            return "";

        try
        {
            if (Path.IsPathRooted(filePath) &&
                filePath.StartsWith(engineRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relative = filePath.Substring(engineRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(relative) ? "" : relative;
            }
        }
        catch
        {
            // best-effort; fall through to return empty
        }
        return "";
    }

    private sealed class UnrealScanResult(ScanManifest manifest, ScanIndex index, IScanData data) : IScanResult
    {
        public ScanManifest Manifest { get; } = manifest;
        public ScanIndex Index { get; } = index;
        public IScanData Data { get; } = data;
    }
}