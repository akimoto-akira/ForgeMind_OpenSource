// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// UE5 基线扫描器：实现四路独立扫描——引擎代码 / 引擎资产 / 项目代码 / 项目资产。
/// <para>
/// 版本策略（测试驱动，不靠版本号推测）：本扫描器对某版本源码测试通过后，
/// 若对更高版本也通过则共用；报错则为该版本单开子类。编译 / 实例化错误记录在
/// <see cref="UeDocumentEngine.ScanDiagnostics"/>，是补齐存根的依据。
/// </para>
/// </summary>
public sealed class Ue5DocumentEngine : UeDocumentEngine
{
    public Ue5DocumentEngine(DirectoryInfo sourceCodeDir, DirectoryInfo installDir, string engineVersion)
        : base(sourceCodeDir, installDir, engineVersion)
    {
    }

    // ── 四路独立扫描 ──

    /// <inheritdoc/>
    public override IReadOnlyList<ScanRoot> ScanEngineCode()
    {
        var root = BuildCodeRoot(SourceCodeDir.FullName, ScopeLevel.Engine, ScopeLevel.EnginePlugin,
            isEngine: true);
        return root is null ? [] : [root];
    }

    /// <inheritdoc/>
    public override IReadOnlyList<ScanRoot> ScanEngineAssets()
    {
        return [];//临时屏蔽功能
        // RootPath 用安装目录 + ":assets" 后缀，与代码根区分（避免 ReplaceRange 覆盖）
        var root = BuildAssetRoot(InstallDir.FullName + ":assets", InstallDir.FullName,
            ScopeLevel.EngineAsset, isEngine: true);
        return root is null ? [] : [root];
    }

    /// <inheritdoc/>
    public override IReadOnlyList<ScanRoot> ScanProjectCode()
    {
        if (ProjectDir is null)
            return [];

        var root = BuildCodeRoot(ProjectDir.FullName, ScopeLevel.Project, ScopeLevel.ProjectPlugin,
            isEngine: false);
        return root is null ? [] : [root];
    }

    /// <inheritdoc/>
    public override IReadOnlyList<ScanRoot> ScanProjectAssets()
    {
        if (ProjectDir is null)
            return [];

        // RootPath 用项目目录 + ":assets" 后缀，与代码根区分
        var root = BuildAssetRoot(ProjectDir.FullName + ":assets", ProjectDir.FullName,
            ScopeLevel.ProjectAsset, isEngine: false);
        return root is null ? [] : [root];
    }

    // ── 代码扫描（插件发现 + 模块编译 + 头文件扫描；不含资产）──

    /// <summary>组装代码根：插件发现 → 直属模块编译 → 头文件扫描。</summary>
    private ScanRoot? BuildCodeRoot(
        string rootPath,
        ScopeLevel rootScope,
        ScopeLevel pluginScope,
        bool isEngine)
    {
        var root = new ScanRoot
        {
            RootPath = rootPath,
            Scope = rootScope,
            Version = EngineVersion,
        };

        // ---- 1. 插件发现 ----
        foreach ((string pluginDir, string? descriptor) in RulesFileScanner.FindPlugins(rootPath))
        {
            var plugin = new PluginInfo
            {
                Name = Path.GetFileName(pluginDir),
                Directory = pluginDir,
                DescriptorFile = descriptor,
                Scope = pluginScope,
            };

            foreach (string rulesFile in RulesFileScanner.FindModuleRulesFiles(pluginDir))
                plugin.Modules.Add(CreateModule(rulesFile, pluginScope));

            root.Plugins.Add(plugin);
        }

        // ---- 2. 直属模块编译 ----
        List<string> sourceRoots = isEngine
            ? RulesFileScanner.GetEngineSourceRoots(rootPath)
            : RulesFileScanner.GetProjectSourceRoots(rootPath);

        List<string> rulesFiles = sourceRoots.SelectMany(RulesFileScanner.FindModuleRulesFiles).ToList();
        RulesCompileResult compile = RulesCompiler.Compile(
            rulesFiles, rootScope, isEngine ? rootPath : null);

        foreach (string error in compile.Errors)
            ScanDiagnostics.Add($"[{rootScope}] 规则编译: {error}");

        foreach (ModuleInfo module in compile.Modules.Values)
        {
            module.Scope = rootScope;
            root.Modules.Add(module);
        }

        // ---- 3. 头文件扫描 ----
        foreach (ModuleInfo module in EnumerateAllModules(root))
        {
            foreach (UeClassInfo info in HeaderScanner.ScanAllHeaders(module.Directory, module.Scope))
                module.Classes.Add(info);
        }

        return root;
    }

    // ── 资产扫描（仅遍历 .uasset；不含代码）──

    /// <summary>组装资产根：仅扫描 .uasset 文件。</summary>
    private ScanRoot? BuildAssetRoot(
        string rootPath,
        string assetDir,
        ScopeLevel assetScope,
        bool isEngine)
    {
        var root = new ScanRoot
        {
            RootPath = rootPath,
            Scope = assetScope,
            Version = EngineVersion,
        };

        root.Assets.AddRange(UAssetScanner.Scan(assetDir, assetScope, out int failed));
        if (failed > 0)
            ScanDiagnostics.Add($"[{assetScope}] 资产扫描: {failed} 个 uasset 解析失败（已跳过）");

        return root;
    }

    // ── 辅助 ──

    private static ModuleInfo CreateModule(string rulesFile, ScopeLevel scope)
    {
        string directory = Path.GetDirectoryName(rulesFile) ?? "";
        return new ModuleInfo
        {
            Name = ModuleNameFrom(rulesFile),
            Directory = directory,
            RulesFile = rulesFile,
            Scope = scope,
        };
    }

    /// <summary>模块名 = .build.cs 文件名去掉 ".Build.cs"（UBT 惯例）。</summary>
    private static string ModuleNameFrom(string rulesFile)
    {
        string fileName = Path.GetFileName(rulesFile);
        const string suffix = ".build.cs";
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static IEnumerable<ModuleInfo> EnumerateAllModules(ScanRoot root)
    {
        foreach (ModuleInfo module in root.Modules)
            yield return module;

        foreach (PluginInfo plugin in root.Plugins)
        {
            foreach (ModuleInfo module in plugin.Modules)
                yield return module;
        }
    }
}
