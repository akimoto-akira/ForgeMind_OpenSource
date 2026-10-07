// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// UBT 规则文件定位（复刻 UBT 的 FindRulesFiles 语义）：
/// <para>- 某目录一旦出现 .build.cs，即视为模块目录，不再深入其子目录；</para>
/// <para>- 跳过 Binaries / Intermediate / Saved / DerivedDataCache / .git / node_modules；</para>
/// <para>- 引擎直属根：Engine/Source 与 Engine/Platforms/*/Source（Plugins 子树另由插件发现收集）。</para>
/// </summary>
public static class RulesFileScanner
{
    private static readonly string[] SkippedDirNames =
    [
        "Binaries", "Intermediate", "Saved", "DerivedDataCache", ".git", "node_modules",
    ];

    /// <summary>引擎直属源码根（Engine/Source 与 Platforms/*/Source）。</summary>
    public static List<string> GetEngineSourceRoots(string engineDir)
    {
        var roots = new List<string>();
        AddIfDir(Path.Combine(engineDir, "Source"), roots);

        string platformsDir = Path.Combine(engineDir, "Platforms");
        if (Directory.Exists(platformsDir))
        {
            foreach (string platformDir in Directory.EnumerateDirectories(platformsDir))
                AddIfDir(Path.Combine(platformDir, "Source"), roots);
        }

        return roots;
    }

    /// <summary>项目直属源码根（Project/Source）。</summary>
    public static List<string> GetProjectSourceRoots(string projectDir)
    {
        var roots = new List<string>();
        AddIfDir(Path.Combine(projectDir, "Source"), roots);
        return roots;
    }

    /// <summary>在指定根下递归收集 .build.cs（命中模块目录即不再深入；自动跳过 Plugins 子树）。</summary>
    public static List<string> FindModuleRulesFiles(string directory)
    {
        var results = new List<string>();
        if (Directory.Exists(directory))
            FindRulesFilesRecursively(directory, results);
        return results;
    }

    /// <summary>
    /// 枚举根目录下的插件（含 .uplugin 描述文件的直接子目录）。
    /// 引擎侧：Engine/Plugins；项目侧：Project/Plugins。
    /// </summary>
    public static List<(string PluginDir, string? DescriptorFile)> FindPlugins(string rootDir)
    {
        var results = new List<(string, string?)>();
        string pluginsDir = Path.Combine(rootDir, "Plugins");
        if (!Directory.Exists(pluginsDir))
            return results;

        foreach (string pluginDir in Directory.EnumerateDirectories(pluginsDir))
        {
            string? descriptor = Directory.EnumerateFiles(pluginDir, "*.uplugin", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            results.Add((pluginDir, descriptor));
        }

        return results;
    }

    private static void AddIfDir(string path, List<string> roots)
    {
        if (Directory.Exists(path))
            roots.Add(path);
    }

    private static void FindRulesFilesRecursively(string dir, List<string> results)
    {
        bool foundInThisDir = false;

        foreach (string file in Directory.EnumerateFiles(dir, "*.build.cs", SearchOption.TopDirectoryOnly))
        {
            results.Add(file);
            foundInThisDir = true;
        }

        // 命中模块目录即不再深入（与 UBT 一致）
        if (foundInThisDir)
            return;

        foreach (string subDir in Directory.EnumerateDirectories(dir))
        {
            string name = Path.GetFileName(subDir);
            if (SkippedDirNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (name.Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                continue; // 插件内的模块由插件发现单独收集，保持归属唯一

            FindRulesFilesRecursively(subDir, results);
        }
    }
}
