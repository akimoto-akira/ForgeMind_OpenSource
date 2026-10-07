// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Diagnostics;
using System.Text.Json;
using ForgeMindServer;
using ForgeMindServer.Compat;
using ForgeMindServer.Plugins;
using ForgeMindServer.Engine;

namespace ForgeMind.OfflineEngineKit;

/// <summary>
/// Unreal Engine project launch tool ("Launch Project").
/// Launches the project in the editor or as a standalone game after
/// verifying that it is not already open in an editor.
/// <para>Two dimensions: the build configuration (development / debug /
/// shipping) and the mode (editor / game). Shipping has no editor
/// binaries, so shipping × editor is refused. Editor mode launches the
/// engine's editor executable with the .uproject path as its only
/// argument - every configuration has its own exe (UnrealEditor.exe for
/// development, UnrealEditor-Win64-DebugGame.exe for debug); no command
/// line flag selects the configuration. Game mode launches the project's
/// own executable under Binaries/Win64 with no arguments and the engine
/// install root as its working directory; a missing exe is refused with a
/// hint to build the Game target (or package the project for shipping).
/// </para>
/// <para>Pipeline: resolve the .uproject (accepts the file itself or its
/// folder, error when missing) → resolve the engine from the
/// EngineAssociation → editor mode: detect an editor process whose command
/// line already references this .uproject (the opener may be any parent —
/// Epic Launcher, a desktop shortcut, etc. — so detection scans all editor
/// processes via a WMI query), then launch the configuration's editor exe
/// with the uproject, detached; game mode: verify the project's game exe
/// exists, then launch it detached.</para>
/// </summary>
public class LaunchTool : ITool
{
    private readonly ServerPermissionChecker _permChecker;
    private readonly IEditorBridge _bridge;

    internal LaunchTool(ServerPermissionChecker permChecker, IEditorBridge bridge)
    {
        _permChecker = permChecker;
        _bridge = bridge;
    }

    /// <summary>Engine-agnostic tool name.</summary>
    public string Name => "launch";
    /// <summary>
    /// Editor process image names whose command lines are inspected
    /// (5.x uses UnrealEditor, the DebugGame editor is a separate exe,
    /// older branches UE4Editor / UE5Editor).
    /// </summary>
    private static readonly string[] EditorProcessNames = ["UnrealEditor.exe", "UnrealEditor-Win64-DebugGame.exe", "UE4Editor.exe", "UE5Editor.exe"];

    /// <summary>Wait limit for the WMI detection query.</summary>
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(20);

    public string Description =>
        "Launch an Unreal Engine project. Two dimensions: 'configuration' (build " +
        "configuration: development / debug / shipping) and 'mode' (editor / game). " +
        "Shipping does not support editor mode. Editor mode launches the engine's " +
        "editor executable with the .uproject path as its only argument - each " +
        "configuration has its own exe (UnrealEditor.exe for development, " +
        "UnrealEditor-Win64-DebugGame.exe for debug). Game mode launches the " +
        "project's own executable under <project>/Binaries/Win64 with no arguments " +
        "(<Project>.exe for development, <Project>-Win64-DebugGame.exe for debug, " +
        "<Project>-Win64-Shipping.exe for shipping); a missing exe is refused with " +
        "a hint to build the Game target (development/debug) or package the project " +
        "(shipping - shipping executables come from packaging). " +
        "Requires the 'path' parameter - either the .uproject file or the folder containing it. " +
        "The tool first checks whether the project is already open in an editor " +
        "(regardless of who launched it) and refuses to open it twice; " +
        "the engine is resolved from the project's EngineAssociation.";

    public string GetDisplayName() => "Launch Project";

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
                ["configuration"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "development", "debug", "shipping" },
                    ["description"] = "Build configuration to launch: 'development' (default), 'debug' (UE's DebugGame) or 'shipping'. Shipping is only valid with mode 'game' - UE ships no Shipping editor binaries"
                },
                ["mode"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "editor", "game" },
                    ["description"] = "What to launch: 'editor' (default, the engine's editor exe with the .uproject path) or 'game' (the project's own exe under Binaries/Win64 - must exist, i.e. the Game target must have been built)"
                }
            },
            ["required"] = new[] { "path" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        // Route by engine version: one branch per supported version. Currently all versions
        // fall through to RunLaunch; if a version diverges later, modify its case directly
        // (requires no new engine-specific files or additional registration).
        EngineVersion? engineVersion = ResolveEngineVersion(parameters);
        return engineVersion switch
        {
            EngineVersion.UE_5_0 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_1 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_2 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_3 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_4 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_5 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_6 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_7 => RunLaunch(callerId, parameters),
            EngineVersion.UE_5_8 => RunLaunch(callerId, parameters),
            _ => RunLaunch(callerId, parameters)
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
    /// Launch implementation shared across all versions.
    /// If a version diverges later, modify the corresponding case in <see cref="Execute"/> to branch differently.
    /// </summary>
    private ToolResult RunLaunch(Guid callerId, Dictionary<string, object> parameters)
    {
        string? permError = _permChecker.RequirePermission(Name);
        if (permError != null)
            return ToolResult.Failed(permError);

        if (!OperatingSystem.IsWindows())
            return ToolResult.Failed("Launching UE projects is only supported on Windows");

        if (!parameters.TryGetValue("path", out object? pathObj) ||
            string.IsNullOrWhiteSpace(pathObj?.ToString()))
        {
            return ToolResult.Failed("Missing 'path' parameter");
        }

        string path = pathObj.ToString()!;

        // 1. Resolve the .uproject — accepts the file itself or its folder
        FileInfo? projectFile = ProjectTool.ResolveProjectFile(path);
        if (projectFile == null)
        {
            return ToolResult.Failed(
                $"No valid .uproject found at '{path}' - expected a .uproject file or the folder containing it");
        }

        // 2. Resolve the engine installation from the EngineAssociation
        string engineAssociation = ProjectTool.GetEngineAssociation(projectFile.Directory!) ?? "";
        UnrealEngineInstallInfo? engine = EngineTool.FindUnrealEngine(engineAssociation);
        if (engine == null)
        {
            return ToolResult.Failed(
                $"Could not resolve the engine for association '{engineAssociation}' - cannot launch the project");
        }

        string configKind = parameters.TryGetValue("configuration", out object? configObj)
            ? configObj?.ToString()?.Trim().ToLowerInvariant() ?? ""
            : "";
        if (configKind is not ("" or "development" or "debug" or "shipping"))
            return ToolResult.Failed($"Invalid 'configuration' value '{configObj}' - expected 'development', 'debug' or 'shipping'");
        if (configKind == "")
            configKind = "development";

        string modeKind = parameters.TryGetValue("mode", out object? modeObj)
            ? modeObj?.ToString()?.Trim().ToLowerInvariant() ?? ""
            : "";
        if (modeKind is not ("" or "editor" or "game"))
            return ToolResult.Failed($"Invalid 'mode' value '{modeObj}' - expected 'editor' or 'game'");
        if (modeKind == "")
            modeKind = "editor";

        // Shipping builds have no editor variant - the engine ships no
        // Shipping editor executable.
        if (modeKind == "editor" && configKind == "shipping")
        {
            return ToolResult.Failed("Shipping does not support editor mode - UE ships no Shipping editor binaries (use mode 'game' for shipping)");
        }

        // 3. Build the command line. Editor and game are very different:
        // editor mode uses the engine's editor exe with the .uproject path
        // as its only argument; game mode uses the project's own game exe
        // (which carries its project association) with no arguments.
        string launchPath;
        string launchArgs;
        string workingDir = "";
        if (modeKind == "editor")
        {
            // Refuse to open the project twice in an editor
            if (FindEditorProcess(projectFile.FullName, out int existingPid))
            {
                return ToolResult.Failed(
                    $"'{projectFile.Name}' is already open in an Unreal Editor process (PID {existingPid})");
            }

            // Each configuration has its own editor exe - no flag selects it
            string exeName = configKind == "debug" ? "UnrealEditor-Win64-DebugGame.exe" : "UnrealEditor.exe";
            launchPath = Path.Combine(engine.Value.InstallLocation, "Engine", "Binaries", "Win64", exeName);
            if (!File.Exists(launchPath))
            {
                return ToolResult.Failed($"{exeName} not found: {launchPath}");
            }
            launchArgs = $"\"{projectFile.FullName}\"";
        }
        else
        {
            // Game mode: the project's own executable under
            // <project>\Binaries\Win64 - Development carries no suffix,
            // Debug/Shipping carry the -Win64-<Configuration> suffix.
            // Missing exe means the game target was never built (or, for
            // shipping, the project was never packaged).
            string projectName = Path.GetFileNameWithoutExtension(projectFile.Name);
            string exeName = configKind switch
            {
                "debug" => $"{projectName}-Win64-DebugGame.exe",
                "shipping" => $"{projectName}-Win64-Shipping.exe",
                _ => $"{projectName}.exe"
            };
            launchPath = Path.Combine(projectFile.Directory!.FullName, "Binaries", "Win64", exeName);
            if (!File.Exists(launchPath))
            {
                return ToolResult.Failed(
                    $"Game executable not found: {launchPath} - " +
                    (configKind == "shipping"
                        ? "package the project first (shipping executables come from packaging)"
                        : $"build the Game target first (unreal_build with target='game', configuration='{configKind}')"));
            }
            // The game exe resolves its own project; no arguments. The
            // working directory is the engine install root (Rider parity).
            launchArgs = "";
            workingDir = engine.Value.InstallLocation;
        }

        // 4. Launch detached

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launchPath,
                Arguments = launchArgs,
                UseShellExecute = false
            };
            if (workingDir != "")
                startInfo.WorkingDirectory = workingDir;

            using var process = Process.Start(startInfo);
            if (process == null)
                return ToolResult.Failed($"Failed to start {Path.GetFileName(launchPath)}");

            // Launch command is the scan trigger point: holds the absolute .uproject path, enqueues directly (idempotent,
            // processed asynchronously on a dedicated thread). Does not rely on the handshake-reported projectFile—that is
            // a relative path on the editor side, which may not resolve to the same project under the server CWD.
            _bridge.EnqueueScan(projectFile.FullName);

            return ToolResult.Successful(
                $"Launched '{projectFile.Name}' ({modeKind}, {configKind}) with engine '{engineAssociation}' (PID {process.Id})",
                new
                {
                    projectFile = projectFile.FullName,
                    engineAssociation,
                    mode = modeKind,
                    configuration = configKind,
                    launchPath,
                    processPid = process.Id
                });
        }
        catch (Exception ex)
        {
            return ToolResult.Failed($"Launch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects a running Unreal Editor whose command line references the given
    /// .uproject. Uses a PowerShell + WMI query because .NET cannot read the
    /// command line of processes it did not start — the opener can be any
    /// parent (Epic Launcher, Explorer, another tool). Shared with
    /// BuildTool, which must not compile an editor target while the
    /// editor holds its binaries locked.
    /// </summary>
    internal static bool FindEditorProcess(string projectFilePath, out int processId)
    {
        processId = 0;

        string names = string.Join("','", EditorProcessNames);
        string script =
            $"Get-CimInstance Win32_Process -Filter \"Name='{names}'\" " +
            "| Select-Object ProcessId, CommandLine | ConvertTo-Json -Compress";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(DetectionTimeout))
                return false;

            if (process.ExitCode != 0)
                return false;

            if (string.IsNullOrWhiteSpace(output))
                return false;

            // ConvertTo-Json returns a single object for one match, an array otherwise
            using var document = JsonDocument.Parse(output);
            IEnumerable<JsonElement> rows =
                document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray()
                    : [document.RootElement];

            foreach (JsonElement row in rows)
            {
                string? commandLine = row.TryGetProperty("CommandLine", out JsonElement cmdProp)
                    ? cmdProp.GetString()
                    : null;

                if (string.IsNullOrEmpty(commandLine))
                    continue;

                // Case-insensitive full-path match against the editor command line
                if (commandLine.Contains(projectFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (row.TryGetProperty("ProcessId", out JsonElement pidProp) &&
                        pidProp.TryGetInt32(out int pid))
                    {
                        processId = pid;
                    }

                    return true;
                }
            }

            return false;
        }
        catch
        {
            // Detection unavailable — never block the launch on a failed check
            return false;
        }
    }
}
