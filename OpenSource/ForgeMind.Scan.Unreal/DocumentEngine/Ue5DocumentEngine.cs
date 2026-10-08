// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// UE5 baseline scanner: implements four independent scan paths — engine code / engine assets / project code / project assets.
/// <para>
/// Version strategy (test-driven, not version-number-guessed): once this scanner passes on some version's source,
/// it is shared with higher versions that also pass; on failure, a version-specific subclass is opened.
/// Compile / instantiation errors are recorded in <see cref="UeDocumentEngine.ScanDiagnostics"/> and guide stub gap-filling.
/// </para>
/// </summary>
public sealed class Ue5DocumentEngine : UeDocumentEngine
{
    public Ue5DocumentEngine(DirectoryInfo sourceCodeDir, DirectoryInfo installDir, string engineVersion)
        : base(sourceCodeDir, installDir, engineVersion)
    {
    }

    // ── Four independent scans ──

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
        return [];// temporarily disabled
        // RootPath uses install dir + ":assets" suffix to distinguish from code root (avoids ReplaceRange overwrite)
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

        // RootPath uses project dir + ":assets" suffix to distinguish from code root
        var root = BuildAssetRoot(ProjectDir.FullName + ":assets", ProjectDir.FullName,
            ScopeLevel.ProjectAsset, isEngine: false);
        return root is null ? [] : [root];
    }

    // ── Code scan (plugin discovery + module compile + header scanning; no assets) ──

    /// <summary>Assemble code root: plugin discovery → direct-module compilation → header scanning.</summary>
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

        // ---- 1. Plugin discovery ----
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

        // ---- 2. Direct-module compilation ----
        List<string> sourceRoots = isEngine
            ? RulesFileScanner.GetEngineSourceRoots(rootPath)
            : RulesFileScanner.GetProjectSourceRoots(rootPath);

        List<string> rulesFiles = sourceRoots.SelectMany(RulesFileScanner.FindModuleRulesFiles).ToList();
        RulesCompileResult compile = RulesCompiler.Compile(
            rulesFiles, rootScope, isEngine ? rootPath : null);

        foreach (string error in compile.Errors)
            ScanDiagnostics.Add($"[{rootScope}] Rules compile: {error}");

        foreach (ModuleInfo module in compile.Modules.Values)
        {
            module.Scope = rootScope;
            root.Modules.Add(module);
        }

        // ---- 3. Header scanning ----
        foreach (ModuleInfo module in EnumerateAllModules(root))
        {
            foreach (UeClassInfo info in HeaderScanner.ScanAllHeaders(module.Directory, module.Scope))
                module.Classes.Add(info);
        }

        return root;
    }

    // ── Asset scan (only walks .uasset; no code) ──

    /// <summary>Assemble asset root: scans only .uasset files.</summary>
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
            ScanDiagnostics.Add($"[{assetScope}] Asset scan: {failed} .uasset(s) failed to parse (skipped)");

        return root;
    }

    // ── Helpers ──

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

    /// <summary>Module name = .build.cs file name minus ".Build.cs" (UBT convention).</summary>
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
