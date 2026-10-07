// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Diagnostics;
using System.Text;
using ForgeMindServer;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.OfflineEngineKit;

/// <summary>
/// Unreal Engine project build tool ("Build Project").
/// Compiles the project through the engine's BuildTool batch script,
/// using the exact command form Visual Studio executes (extracted from the
/// generated NMake .vcxproj).
/// <para>Pipeline: resolve the .uproject → parse target (Editor/Game) and
/// configuration (Development/Debug) → when building the Editor target,
/// refuse if an editor already has the project open (its binaries are locked)
/// → resolve the engine from the EngineAssociation → run
/// <c>Build.bat {Target} Win64 {Configuration} -Project="..." -WaitMutex</c>,
/// plus <c>-architecture=x64</c> only on engines with the architecture-subdir
/// output layout (UE 5.3+; older UBTs would expand engine include paths to a
/// non-existent Win64\x64 directory).
/// Builds take minutes (tens of minutes for a full rebuild) and the call
/// blocks until completion.</para>
/// </summary>
public class BuildTool : ITool
{
    private readonly ServerPermissionChecker _permChecker;

    internal BuildTool(ServerPermissionChecker permChecker)
    {
        _permChecker = permChecker;
    }

    /// <summary>Engine-agnostic tool name.</summary>
    public string Name => "build";
    /// <summary>Hard wait limit — a full rebuild of a large project can be long, but never unbounded.</summary>
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Maximum error lines echoed back into a failed result.</summary>
    private const int MaxErrorLines = 30;

    public string Description =>
        "Compile an Unreal Engine project via the engine's Build.bat (BuildTool). " +
        "Parameters: 'path' (the .uproject file or its folder, required), " +
        "'target' ('editor' = project name + Editor suffix, or 'game'; default 'editor'), " +
        "'configuration' ('development' or 'debug' - 'debug' maps to UE's DebugGame configuration; default 'development'). " +
        "Building the editor target fails fast when the project is already open in an editor (locked binaries). " +
        "NOTE: this runs for minutes, up to tens of minutes for a full rebuild.";

    public string GetDisplayName() => "Build Project";

    public Dictionary<string, object> GetParameterSchema()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Path to the .uproject file, or the folder containing it"
                },
                ["target"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "editor", "game" },
                    ["description"] = "Build target: 'editor' ({ProjectName}Editor) or 'game' ({ProjectName}); default 'editor'"
                },
                ["configuration"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "development", "debug" },
                    ["description"] = "Build configuration: 'development' or 'debug' (mapped to UE's DebugGame); default 'development'"
                }
            },
            ["required"] = new[] { "path" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        // Route by engine version: one branch per supported version. Currently all versions
        // fall through to RunBuild; if a version diverges later, modify its case directly
        // (requires no new engine-specific files or additional registration).
        EngineVersion? engineVersion = ResolveEngineVersion(parameters);
        return engineVersion switch
        {
            EngineVersion.UE_5_0 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_1 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_2 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_3 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_4 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_5 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_6 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_7 => RunBuild(callerId, parameters),
            EngineVersion.UE_5_8 => RunBuild(callerId, parameters),
            _ => RunBuild(callerId, parameters)
        };
    }

    /// <summary>
    /// Parses the engine version from 'path' for <see cref="Execute"/> routing.
    /// Returns null for an invalid path or unlisted version, falling through to the default branch.
    /// </summary>
    private static EngineVersion? ResolveEngineVersion(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("path", out object? pathObj) ||
            string.IsNullOrWhiteSpace(pathObj?.ToString()))
        {
            return null;
        }

        FileInfo? projectFile = ProjectTool.ResolveProjectFile(pathObj.ToString()!);
        return projectFile == null
            ? null
            : EngineVersions.FromAssociation(ProjectTool.GetEngineAssociation(projectFile.Directory!));
    }

    /// <summary>
    /// Build implementation shared across all versions.
    /// If a version diverges later, modify the corresponding case in <see cref="Execute"/> to branch differently.
    /// </summary>
    private ToolResult RunBuild(Guid callerId, Dictionary<string, object> parameters)
    {
        string? permError = _permChecker.RequirePermission(Name);
        if (permError != null)
            return ToolResult.Failed(permError);

        if (!OperatingSystem.IsWindows())
            return ToolResult.Failed("Building UE projects is only supported on Windows");

        if (!parameters.TryGetValue("path", out object? pathObj) ||
            string.IsNullOrWhiteSpace(pathObj?.ToString()))
        {
            return ToolResult.Failed("Missing 'path' parameter");
        }

        // 1. Resolve the .uproject — accepts the file itself or its folder
        FileInfo? projectFile = ProjectTool.ResolveProjectFile(pathObj.ToString()!);
        if (projectFile == null)
        {
            return ToolResult.Failed(
                $"'{pathObj}' does not contain a valid .uproject - expected the file itself or its folder");
        }

        // 2. Target: editor (default) appends the Editor suffix, game uses the project name
        string targetKind = parameters.TryGetValue("target", out object? targetObj)
            ? targetObj?.ToString()?.Trim().ToLowerInvariant() ?? ""
            : "";
        bool buildEditor = targetKind switch
        {
            "" or "editor" => true,
            "game" => false,
            _ => false
        };
        if (targetKind is not ("" or "editor" or "game"))
            return ToolResult.Failed($"Invalid 'target' value '{targetObj}' - expected 'editor' or 'game'");

        string projectName = Path.GetFileNameWithoutExtension(projectFile.Name);
        string targetName = buildEditor ? $"{projectName}Editor" : projectName;

        // 3. Configuration: development (default) / debug → UE 'DebugGame'
        string configKind = parameters.TryGetValue("configuration", out object? configObj)
            ? configObj?.ToString()?.Trim().ToLowerInvariant() ?? ""
            : "";
        string configuration = configKind switch
        {
            "" or "development" => "Development",
            "debug" => "DebugGame",
            _ => ""
        };
        if (configuration == "")
            return ToolResult.Failed($"Invalid 'configuration' value '{configObj}' - expected 'development' or 'debug'");

        // 4. Editor builds fail while an editor holds the project binaries locked
        if (buildEditor && LaunchTool.FindEditorProcess(projectFile.FullName, out int editorPid))
        {
            return ToolResult.Failed(
                $"'{projectFile.Name}' is open in an Unreal Editor process (PID {editorPid}) - " +
                "close the editor before building the Editor target (the DLL is locked)");
        }

        // 5. Resolve the engine installation from the EngineAssociation
        string engineAssociation = ProjectTool.GetEngineAssociation(projectFile.Directory!) ?? "";
        UnrealEngineInstallInfo? engine = EngineTool.FindUnrealEngine(engineAssociation);
        if (engine == null)
        {
            return ToolResult.Failed(
                $"Could not resolve the engine for association '{engineAssociation}' - cannot build the project");
        }

        string buildBat = Path.Combine(engine.Value.InstallLocation, "Engine", "Build", "BatchFiles", "Build.bat");
        if (!File.Exists(buildBat))
        {
            return ToolResult.Failed($"Build.bat not found: {buildBat}");
        }

        // 6. Same command line Visual Studio runs (minus the MSBuild-only flag).
        // -architecture=x64 is only for engines using the architecture-subdir output layout (UE 5.3+, where VS passes this flag);
        // UBT in 5.0-5.2 receiving this flag expands engine-side Inc paths to Win64\x64\...——
        // which does not exist in installed builds, causing C1083 (*.generated.h not found) immediately.
        string architectureArgument =
            Version.TryParse(engine.Value.AppVersion, out Version? engineVersion) &&
            engineVersion >= new Version(5, 3)
                ? " -architecture=x64"
                : "";
        string arguments =
            $"{targetName} Win64 {configuration} -Project=\"{projectFile.FullName}\" -WaitMutex{architectureArgument}";

        var startInfo = new ProcessStartInfo
        {
            FileName = buildBat,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return ToolResult.Failed("Failed to start Build.bat");

            // Drain both streams concurrently — build output is huge and must not block
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(BuildTimeout))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return ToolResult.Failed(
                    $"Build of '{targetName}' did not finish within {BuildTimeout.TotalMinutes:0} minutes and was terminated");
            }

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            stopwatch.Stop();

            if (process.ExitCode != 0)
            {
                string errors = ExtractErrorLines(stdout, stderr);
                return ToolResult.Failed(
                    $"Build of '{targetName}' ({configuration}) failed with exit code {process.ExitCode} " +
                    $"after {stopwatch.Elapsed.TotalMinutes:0.#} min.\n{errors}");
            }

            return ToolResult.Successful(
                $"Built '{targetName}' ({configuration}, Win64) in {stopwatch.Elapsed.TotalMinutes:0.#} min",
                new
                {
                    projectFile = projectFile.FullName,
                    target = targetName,
                    configuration,
                    engineAssociation,
                    buildBat,
                    exitCode = process.ExitCode,
                    elapsedSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds)
                });
        }
        catch (Exception ex)
        {
            return ToolResult.Failed($"Build failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Collects lines that look like compile/build errors so the caller (an AI
    /// agent) can act on them; capped to keep the tool result small.
    /// </summary>
    private static string ExtractErrorLines(string stdout, string stderr)
    {
        var errors = new List<string>();

        foreach (string rawLine in (stdout + "\n" + stderr).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            // '... error C2xxx:', '... fatal error C1xxx:', UBT's 'ERROR:'
            if (line.Contains(" error ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(line);
            }
        }

        if (errors.Count == 0)
            return "(no error lines captured — see the build log)";

        var builder = new StringBuilder();
        if (errors.Count > MaxErrorLines)
        {
            builder.AppendLine($"({errors.Count - MaxErrorLines} earlier error line(s) omitted)");
            errors = errors.Skip(errors.Count - MaxErrorLines).ToList();
        }

        foreach (string error in errors)
            builder.AppendLine(error);

        return builder.ToString().TrimEnd();
    }
}
