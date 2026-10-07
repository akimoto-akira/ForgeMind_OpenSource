// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text;
using ForgeMindServer;
using ForgeMindServer.Compat;
using ForgeMindServer.Test;

// ForgeMindServer test runner.
// The test mode originally lived in the server binary (ForgeMindServer --Test);
// it is extracted here so the server binary stays a pure MCP server. This exe
// starts the same host services (UE bridge / license / scan index preload),
// then discovers all ITestCase implementations in this assembly via reflection
// and executes them in order (fail-fast). Tools are called directly via
// ITool.Execute (no MCP protocol), but tools internally use the Bridge to
// communicate with UE.
//
// Usage:
//   ForgeMindServerTest --Test [group] [order]   (same shape as original ForgeMindServer --Test)
//   ForgeMindServerTest [group] [order]          (equivalent form omitting --Test)
//   no args = full; group only = whole group; group + order = the case matching Order in that group
//   (auto-includes must-run cases in the group, such as launch / close editor).
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// ── CLI parsing: prefer the explicit --Test form, where args after it that don't
//    start with -- are consumed as group / order in order;
//    when --Test is absent, all non-- args are consumed as group / order.
string? testGroup = null;
int? testOrder = null;
int markerIdx = Array.FindIndex(args, a => string.Equals(a, "--Test", StringComparison.OrdinalIgnoreCase));
int firstParamIdx = markerIdx >= 0 ? markerIdx + 1 : 0;
while (firstParamIdx < args.Length && args[firstParamIdx].StartsWith("--"))
{
    firstParamIdx++;
}

if (firstParamIdx < args.Length)
{
    testGroup = args[firstParamIdx];
    if (firstParamIdx + 1 < args.Length && !args[firstParamIdx + 1].StartsWith("--"))
    {
        if (!int.TryParse(args[firstParamIdx + 1], out int parsedOrder))
        {
            Console.Error.WriteLine(
                $"[ForgeMindServerTest] Invalid order '{args[firstParamIdx + 1]}': must be an integer");
            return 1;
        }
        testOrder = parsedOrder;
    }
}

ILogger logger = LogManager.Instance.GetLogger<ForgeMindServerTestEntry>();
logger.Info(null, "[ForgeMindServerTest] Test mode starting{0}{1}",
    testGroup != null ? $" (group: {testGroup}" : "",
    testGroup != null ? testOrder != null ? $", order: {testOrder})" : ")" : "");


// Host services (bridge, license, scan index preload) — same as the server's MCP mode
// ── Pre-build ────────────────────────────────────────────────────────────
// Tests call ITool.Execute directly to exercise the real plugin implementation. If plugins were not
// recompiled alongside the host, the stale assembly is executed: failures manifest as "mysterious behavior"
// and are extremely hard to trace back to "actually wasn't recompiled."
// Therefore, before running any test case, ensure the host and all plugins are freshly built — the host's
// post-build hook compiles all plugins transitively, so building the host here is sufficient.
// --NoBuild explicitly skips this step (useful when CI already guarantees freshness).
string? runDir = null;
TestFileLogger? runLog = null;
{
    bool skipBuild = Array.Exists(args,
        a => string.Equals(a, "--NoBuild", StringComparison.OrdinalIgnoreCase));
    string? repoRoot = FindRepoRoot();

    if (skipBuild)
    {
        logger.Info(null, "[ForgeMindServerTest] --NoBuild: skipped the pre-test build check");
    }
    else if (repoRoot is null)
    {
        logger.Warn(null, "[ForgeMindServerTest] No *.sln found above {0}: skipped the pre-test build check",
            AppContext.BaseDirectory);
    }
    else
    {
        // The suite attaches its test.log sink only after discovery, which is too late to catch
        // the build. Create the run directory here and attach the sink ourselves, then hand the
        // directory to the suite so it reuses both instead of making a second one.
        runDir = Path.Combine(@"d:\ForgeMind\TestRuns", DateTime.Now.ToString("yyyyMMdd_HHmm"));
        Directory.CreateDirectory(runDir);
        runLog = new TestFileLogger(Path.Combine(runDir, "test.log"));
        LogManager.Instance.AddSink(runLog);

        if (!BuildAll(repoRoot, logger, out string buildOutput))
        {
            Console.Error.WriteLine("[ForgeMindServerTest] Pre-test build FAILED - no test was run:");
            Console.Error.WriteLine(buildOutput);
            return 1;
        }
    }
}
// ── Pre-build helper functions ───────────────────────────────────────────
static string? FindRepoRoot()
{
    // Any solution file marks the repository root — the name is not pinned to one repo.
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (dir.EnumerateFiles("*.sln").Any())
            return dir.FullName;
    }

    return null;
}

static bool BuildAll(string repoRoot, ILogger logger, out string output)
{
#if DEBUG
    const string configuration = "Debug";
#else
    const string configuration = "Release";
#endif

    var log = new System.Text.StringBuilder();
    bool ok = true;

    // Host first: plugins reference it, so a fresh host assembly must exist before they build.
    // The host lives under OpenSource\ (it is no longer at the repository root).
    ok &= BuildProject(Path.Combine(repoRoot, "OpenSource", "ForgeMindServer", "ForgeMindServer.csproj"), configuration, log);

    // Plugins are discovered, not hard-coded: any directory that ships a file ending in
    // ".manifest.json" is a plugin, wherever it sits and whatever that file is called.
    // Adding a plugin requires no edit here - dropping the project in with its manifest
    // is enough.
    foreach (string pluginProject in DiscoverPluginProjects(repoRoot))
    {
        string name = Path.GetFileNameWithoutExtension(pluginProject);
        if (!BuildProject(pluginProject, configuration, log))
        {
            ok = false;
            logger.Error(null, "[ForgeMindServerTest] plugin build failed: {0}", name);
            continue;
        }

        if (!StagePluginArtifacts(pluginProject, name, configuration, logger, log))
        {
            ok = false;
            logger.Error(null, "[ForgeMindServerTest] plugin staging failed: {0}", name);
        }
        else
        {
            logger.Info(null, "[ForgeMindServerTest] plugin staged: {0}", name);
        }
    }

    output = log.ToString();

    // Land the per-project build record in the run log, not just on the console: a stale plugin
    // is the single most misleading failure mode, and it is only diagnosable from this record.
    logger.Info(null, "[ForgeMindServerTest] Pre-test build ({0}):{1}{2}",
        ok ? "ok" : "FAILED", Environment.NewLine, output);

    return ok;
}

/// <summary>
/// Deploys plugin artifacts to the directory the host scans: <c>&lt;entry dir&gt;\plugins\&lt;PluginName\&gt;\</c>.
/// <para>Standard (finalized this round): ① Target anchors to the **entry process directory** — the host resolves
/// the plugins directory from <c>AppContext.BaseDirectory</c>; test process and host process each resolve independently,
/// deployment must match the runtime resolution scope; ② Content is the plugin main DLL + same-named manifest;
/// ③ Target directory is cleared before deployment, otherwise stale DLLs from the previous version would be
/// scanned by <c>PluginLoader</c> and loaded twice; ④ <c>FORGEMIND_PLUGINS_DIR</c> is a runtime override
/// and is not involved during the build phase.</para>
/// </summary>
static bool StagePluginArtifacts(
    string pluginProject, string pluginName, string configuration, ILogger logger, System.Text.StringBuilder log)
{
    try
    {
        string projectDir = Path.GetDirectoryName(pluginProject)!;
        // Exclude ref/refint: those hold reference assemblies with the same file name but no
        // method bodies — picking one would stage a dll that cannot load at runtime.
        string binary = Directory
            .EnumerateFiles(Path.Combine(projectDir, "bin", configuration), pluginName + ".dll", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (binary is null)
        {
            log.AppendLine($"Build output not found for {pluginName} under bin/{configuration}.");
            return false;
        }

        string stageDir = Path.Combine(AppContext.BaseDirectory, "plugins", pluginName);
        Directory.CreateDirectory(stageDir);
        foreach (string stale in Directory.GetFiles(stageDir, "*", SearchOption.AllDirectories))
            File.Delete(stale);

        // Stage the plugin's whole output directory, not just its dll: a plugin may ship
        // sibling dependencies (ForgeMind.Speedy.dll), a .deps.json, and runtime data trees
        // (Knowledge/). Copying only dll + manifest silently breaks those.
        string outputDir = Path.GetDirectoryName(binary)!;
        foreach (string file in Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(outputDir, file);

            // Never stage a file the host already ships beside itself — the host assembly, and
            // shared contract libraries such as the scan contract. A copy inside the plugin
            // folder is loaded into the plugin load context as a SECOND assembly identity, so
            // cross-boundary types split: the host's ScanDataProvider.Current then no longer
            // matches the IScanDataProvider the plugin was compiled against and every call
            // dies with MissingMethodException. Falling back to the host's copy is the whole
            // point of the plugin contract.
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, relative)))
            {
                continue;
            }

            string destination = Path.Combine(stageDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        // Any file ending in ".manifest.json" is this plugin's manifest, whatever it is called
        // on disk. The host resolves it as "<assembly name>.manifest.json" sitting next to the
        // dll, so it is staged under that normalized name.
        string? manifest = Directory
            .EnumerateFiles(projectDir, "*.manifest.json")
            .OrderBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();

        if (manifest is not null)
            File.Copy(manifest, Path.Combine(stageDir, pluginName + ".manifest.json"), overwrite: true);

        return true;
    }
    catch (Exception ex)
    {
        log.AppendLine($"Staging {pluginName} failed: {ex.Message}");
        return false;
    }
}

static IEnumerable<string> DiscoverPluginProjects(string repoRoot)
{
    // No edition or layout name appears here: every directory whose name does not start with
    // "." is walked, and any directory shipping a file that ends in ".manifest.json" is a
    // plugin. Where the plugin lives, and what its manifest file is called, are both free.
    foreach (string directory in EnumerateDirectories(repoRoot))
    {
        if (!Directory.EnumerateFiles(directory, "*.manifest.json").Any())
            continue;

        foreach (string project in Directory.GetFiles(directory, "*.csproj").OrderBy(p => p, StringComparer.Ordinal))
            yield return project;
    }
}

/// <summary>
/// Depth-first walk of every directory under <paramref name="root"/>, skipping those whose name
/// starts with ".". Order is deterministic so the resulting build order is stable.
/// </summary>
static IEnumerable<string> EnumerateDirectories(string root)
{
    foreach (string directory in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
    {
        if (Path.GetFileName(directory).StartsWith('.'))
            continue;

        yield return directory;

        foreach (string nested in EnumerateDirectories(directory))
            yield return nested;
    }
}

static bool BuildProject(string projectPath, string configuration, System.Text.StringBuilder log)
{
    var startInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "dotnet",
        // --no-incremental: MSBuild's up-to-date check has been seen to skip CoreCompile for a
        // plugin even though the host assembly it references changed, silently leaving a stale
        // dll that only fails at runtime (MissingMethodException). This pre-build exists to
        // guarantee freshness, so correctness wins over build time.
        Arguments = $"build \"{projectPath}\" -c {configuration} --no-incremental -v quiet",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = System.Diagnostics.Process.Start(startInfo);
    if (process is null)
    {
        log.AppendLine($"Failed to start dotnet for {Path.GetFileName(projectPath)}.");
        return false;
    }

    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    string text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
    if (process.ExitCode != 0)
        log.AppendLine($"--- {Path.GetFileName(projectPath)} ---").AppendLine(text);
    else
        log.AppendLine($"OK   {Path.GetFileName(projectPath)}");

    return process.ExitCode == 0;
}

ForgeMindServerHost.Start();

// Plugin load timing is ours to decide: the host does not load plugins on its own.
ForgeMindServerHost.EnsurePluginsLoaded();

int exitCode;
try
{
    var suite = new TestSuite(ForgeMindServerHost.Tools);
    exitCode = suite.RunAll(testGroup, testOrder, runDir);
}
catch (Exception ex)
{
    logger.Error(null, "[Test] Fatal: {0}", ex);
    exitCode = 1;
}
finally
{
    ForgeMindServerHost.Stop();
    logger.Info(null, "[Test] Test mode finished");

    // We own the sink when we created it (pre-test build path), so we close it.
    if (runLog is not null)
    {
        LogManager.Instance.RemoveSink(runLog);
        runLog.Dispose();
    }
}

Console.Beep();
return exitCode;

/// <summary>Marker type for the entry-point logger category.</summary>
internal sealed class ForgeMindServerTestEntry
{
}