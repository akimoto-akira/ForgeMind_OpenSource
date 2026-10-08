// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// UBT rules file location (replicates UBT FindRulesFiles semantics):
/// <para>- Once a directory contains .build.cs, it is treated as a module directory; does not descend into subdirs.</para>
/// <para>- Skips Binaries / Intermediate / Saved / DerivedDataCache / .git / node_modules.</para>
/// <para>- Engine-direct roots: Engine/Source and Engine/Platforms/*/Source (Plugins subtree collected separately via plugin discovery).</para>
/// </summary>
public static class RulesFileScanner
{
    private static readonly string[] SkippedDirNames =
    [
        "Binaries", "Intermediate", "Saved", "DerivedDataCache", ".git", "node_modules",
    ];

    /// <summary>Engine-direct source roots (Engine/Source and Platforms/*/Source).</summary>
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

    /// <summary>Project-direct source roots (Project/Source).</summary>
    public static List<string> GetProjectSourceRoots(string projectDir)
    {
        var roots = new List<string>();
        AddIfDir(Path.Combine(projectDir, "Source"), roots);
        return roots;
    }

    /// <summary>Recursively collect .build.cs under a given root (stops descending on hitting module directory; auto-skips Plugins subtree).</summary>
    public static List<string> FindModuleRulesFiles(string directory)
    {
        var results = new List<string>();
        if (Directory.Exists(directory))
            FindRulesFilesRecursively(directory, results);
        return results;
    }

    /// <summary>
    /// Enumerate plugins under a root directory (direct subdirs containing a .uplugin descriptor file).
    /// Engine side: Engine/Plugins; project side: Project/Plugins.
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

        // Stop descending once module directory matches (consistent with UBT)
        if (foundInThisDir)
            return;

        foreach (string subDir in Directory.EnumerateDirectories(dir))
        {
            string name = Path.GetFileName(subDir);
            if (SkippedDirNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (name.Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                continue; // Modules inside plugins collected separately via plugin discovery, keeping ownership unique

            FindRulesFilesRecursively(subDir, results);
        }
    }
}
