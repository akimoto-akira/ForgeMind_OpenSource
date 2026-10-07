// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Scan.Unreal;
using ForgeMind.Speedy;
using ForgeMindServer;
using ForgeMindServer.Engine;

namespace ForgeMind.EngineScanner;

/// <summary>
/// Command line entry of the engine scanner (function B).
/// <para>Enumerates the engines installed on this machine, scans each through
/// <see cref="UnrealScanner"/> (IScanner), and packs all results into a single .spk file
/// via <see cref="ScanResultPacker"/> (IPackWriter). It runs standalone — the host does not
/// need to be running. No intermediate ScanCache, no loose files.</para>
/// <para>Takes no arguments. The output location is discovered, not configured: the scanner
/// searches the repository for <see cref="BundleMarkerFileName"/> and writes
/// <see cref="BundleFileName"/> into that same directory, overwriting it. No directory name is
/// ever written down here, and the marker — not the bundle — is what anchors the location, so
/// deleting or regenerating the bundle is harmless.</para>
/// </summary>
internal static class Program
{
    /// <summary>
    /// Marker file that anchors the output directory: it is kept precisely because the bundle
    /// next to it is expendable and may be cleaned away. It must never be deleted.
    /// </summary>
    private const string BundleMarkerFileName = "engine.spk.log";

    /// <summary>File name of the scan bundle, written next to <see cref="BundleMarkerFileName"/>.</summary>
    private const string BundleFileName = "engine.spk";

    /// <summary>Directory names never searched: build output, never the source of truth.</summary>
    private static readonly string[] SearchSkipDirectories = ["bin", "obj"];

    /// <summary>
    /// Any solution file marks the repository root. Nothing about the host is consulted —
    /// the scanner locates its own output from the solution downwards, so it runs standalone.
    /// </summary>
    private const string SolutionSearchPattern = "*.sln";

    public static int Main()
    {
        try
        {
            string? repoRoot = FindRepositoryRoot();
            if (repoRoot is null)
            {
                Console.Error.WriteLine(
                    $"Engine scan failed: could not locate the repository root above '{AppContext.BaseDirectory}'.");
                return 1;
            }

            string? bundlePath = FindBundlePath(repoRoot);
            if (bundlePath is null)
            {
                Console.Error.WriteLine(
                    $"Engine scan failed: no '{BundleMarkerFileName}' found under '{repoRoot}' — cannot tell where to write '{BundleFileName}'.");
                return 1;
            }

            return ScanToSpk(bundlePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Engine scan failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Scanner → SPK direct output. Discovers installed engines, builds a target per engine,
    /// scans every target with the original scanning algorithm, and packs each version's raw
    /// <see cref="ScanRoot"/> tree through <see cref="ScanCachePackWriter"/> — the pack layout
    /// is the historical ScanCache layout, one <c>{version}/engine.json</c> + <c>{version}/assets.json</c>
    /// pair per version. No manifest, no flat views, no loose files.
    /// </summary>
    private static int ScanToSpk(string spkPath)
    {
        string? outputDir = Path.GetDirectoryName(spkPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        UnrealEngineInstallInfo[] engines = UnrealEngineLocator.GetAllUnrealApplications();
        Console.WriteLine($"Found {engines.Length} installed engine(s).");

        if (engines.Length == 0)
        {
            Console.WriteLine("Nothing to scan.");
            return 0;
        }

        // Build all targets first (skip missing directories).
        // 同一版本只留一个目标：Launcher 记录里 UE_5.0 / QuixelBridge_5.0 / FabPlugin_5.x
        // 指向同一安装目录，版本归一后它们本就落进同一个 {version}/ 目录，重复扫描只是白烧 CPU。
        var targets = new List<IScanTarget>(engines.Length);
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UnrealEngineInstallInfo engine in engines)
        {
            string version = ResolveVersion(engine);
            string engineDir = engine.InstallLocation;

            if (!Directory.Exists(engineDir))
            {
                Console.WriteLine($"  [{version}] skip — directory not found: {engineDir}");
                continue;
            }

            if (!seenVersions.Add(version))
            {
                Console.WriteLine($"  [{version}] skip — version already queued: {engineDir}");
                continue;
            }

            targets.Add(new SimpleScanTarget
            {
                Descriptor = new ScanTargetDescriptor
                {
                    TargetType = "UnrealEngine",
                    TargetId = version,
                    DisplayName = engine.AppName,
                },
                Sources =
                [
                    new SimpleScanSource { Kind = "directory", Identifier = engineDir },
                ],
            });
        }

        if (targets.Count == 0)
        {
            Console.WriteLine("No valid engine directories to scan.");
            return 0;
        }

        // 覆盖语义：整包重写，不向已有内容追加。扫描前先删掉目标文件，上一次的产物
        // 不会以任何形式残留——否则包里留着上次的条目、新的写入又只覆盖同名条目，
        // 内容与清单会各说各话。
        if (File.Exists(spkPath))
            File.Delete(spkPath);

        // Per-target failure isolation (refactor plan §4): a single failed target does not
        // abort the entire scan; successful results are still written to the SPK.
        int failed = 0;
        var scanner = new UnrealScanner();
        using (var pack = SpeedyPack.Open(spkPath))
        {
            Console.WriteLine($"Writing SPK: {spkPath}");
            var writer = new ScanCachePackWriter(pack);

            foreach (IScanTarget target in targets)
            {
                try
                {
                    var (version, codeRoots, assetRoots) = scanner.ScanRoots(target);
                    writer.Write(version, codeRoots, assetRoots);
                    Console.WriteLine(
                        $"  [{version}] {codeRoots.Count} code root(s), {assetRoots.Count} asset root(s)");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"  [{target.Descriptor.TargetId}] scan failed: {ex.Message}");
                }
            }

            pack.FlushAsync().GetAwaiter().GetResult();
        }

        Console.WriteLine($"Done. {targets.Count - failed}/{targets.Count} target(s) scanned successfully.");
        return failed > 0 ? 1 : 0;
    }

    /// <summary>
    /// SPK 第一层目录名的来源，沿用历史 ScanCache 的规则：
    /// <b>版本仅作数据属性，不作身份标识</b>——安装版用关联版本号（major.minor，如 "5.0"），
    /// 源码版用 Build.version 全串（如 "5.0.3-20979098+++UE5+Release-5.0-2023.1-Windows"）。
    /// <para>
    /// 区分依据是 <see cref="UnrealEngineInstallInfo.NamespaceId"/>："regUe" = 注册表源码构建，
    /// 其余（"ue"）= Epic Launcher 安装版。历史上安装版取的是 .uproject 的 EngineAssociation
    /// （"5.0"），这里没有 .uproject 可依，就从 AppVersion 截 major.minor，结果一致。</para>
    /// </summary>
    private static string ResolveVersion(UnrealEngineInstallInfo engine)
    {
        bool isSourceBuild = string.Equals(engine.NamespaceId, "regUe", StringComparison.OrdinalIgnoreCase);
        string appVersion = engine.AppVersion ?? "";

        if (isSourceBuild)
            return appVersion;

        // 安装版 → major.minor（"5.4.4-35576357+++…" → "5.4"）
        string[] parts = appVersion.Split('.');
        return parts.Length >= 2
               && int.TryParse(parts[0], out _)
               && int.TryParse(parts[1], out _)
            ? parts[0] + "." + parts[1]
            : appVersion;
    }

    /// <summary>
    /// Walks up from the output directory to the repository root: the first ancestor that
    /// contains a file matching <see cref="SolutionSearchPattern"/>. The scanner then searches
    /// down from that root, so it can run from any build configuration without being told
    /// where to write — and without looking for the host.
    /// </summary>
    private static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles(SolutionSearchPattern).Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Resolves where to write the bundle: the first directory under <paramref name="repoRoot"/>
    /// that holds <see cref="BundleMarkerFileName"/> wins, and the bundle is written next to it
    /// under <see cref="BundleFileName"/>. Returns null when no marker exists anywhere — without
    /// one there is no location to write to.
    /// </summary>
    private static string? FindBundlePath(string repoRoot)
    {
        foreach (string directory in EnumerateDirectories(repoRoot))
        {
            if (File.Exists(Path.Combine(directory, BundleMarkerFileName)))
                return Path.Combine(directory, BundleFileName);
        }

        return null;
    }

    /// <summary>
    /// Depth-first walk of the directories under <paramref name="root"/>, skipping hidden ones
    /// and build output. Order is deterministic so a repository holding more than one bundle
    /// always resolves to the same one.
    /// </summary>
    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        foreach (string directory in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(directory);
            if (name.StartsWith('.') || SearchSkipDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            yield return directory;

            foreach (string nested in EnumerateDirectories(directory))
                yield return nested;
        }
    }
}
