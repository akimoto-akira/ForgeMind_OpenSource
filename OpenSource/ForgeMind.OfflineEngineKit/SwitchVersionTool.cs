// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Diagnostics;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.OfflineEngineKit;

/// <summary>
/// Unreal Engine version switch tool ("Switch Engine Version").
/// Launches the official UnrealVersionSelector with <c>/switchversion</c> so
/// the user can move the project to another engine version through the
/// official dialog, then reports how the project's EngineAssociation changed.
/// <para>Pipeline: resolve the .uproject (accepts the file itself or its
/// folder, error when missing) → verify UnrealVersionSelector.exe exists
/// (error when missing) → read the EngineAssociation before the switch →
/// run <c>UnrealVersionSelector.exe /switchversion "&lt;uproject&gt;"</c> and
/// wait for the dialog to close → read the EngineAssociation again and
/// report the change.</para>
/// </summary>
public class SwitchVersionTool : ITool
{
    private readonly ServerPermissionChecker _permChecker;

    internal SwitchVersionTool(ServerPermissionChecker permChecker)
    {
        _permChecker = permChecker;
    }

    /// <summary>Engine-agnostic tool name.</summary>
    public string Name => "switch_version";
    /// <summary>
    /// Fixed location of the Epic Launcher's UnrealVersionSelector, which
    /// performs the engine version switch for the project.
    /// </summary>
    private const string UnrealVersionSelectorPath =
        @"C:\Program Files (x86)\Epic Games\Launcher\Engine\Binaries\Win64\UnrealVersionSelector.exe";

    /// <summary>
    /// Wait limit for the switch process. The official dialog is interactive
    /// and the following conversion may run several minutes.
    /// </summary>
    private static readonly TimeSpan SwitchVersionTimeout = TimeSpan.FromMinutes(10);

    public string Description =>
        "Launch the official Unreal version switch dialog to change the engine version of an Unreal Engine project. " +
        "Requires the 'path' parameter — either the .uproject file or the folder containing it. " +
        "The tool monitors the project's EngineAssociation before and after the switch and reports the change.";

    public string GetDisplayName() => "Switch Engine Version";

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
                }
            },
            ["required"] = new[] { "path" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        string? permError = _permChecker.RequirePermission(Name);
        if (permError != null)
            return ToolResult.Failed(permError);

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

        // 2. The Epic Launcher's UnrealVersionSelector performs the switch
        if (!File.Exists(UnrealVersionSelectorPath))
        {
            return ToolResult.Failed($"UnrealVersionSelector.exe not found: {UnrealVersionSelectorPath}");
        }

        // 3. EngineAssociation before the switch — the comparison baseline
        string associationBefore = ProjectTool.GetEngineAssociation(projectFile.Directory!) ?? "";

        // Route by engine version: one branch per supported version. Currently all versions
        // fall through to RunSwitch; if a version diverges later, modify its case directly.
        EngineVersion? engineVersion = EngineVersions.FromAssociation(associationBefore);
        return engineVersion switch
        {
            EngineVersion.UE_5_0 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_1 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_2 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_3 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_4 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_5 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_6 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_7 => RunSwitch(projectFile, associationBefore),
            EngineVersion.UE_5_8 => RunSwitch(projectFile, associationBefore),
            _ => RunSwitch(projectFile, associationBefore)
        };
    }

    /// <summary>
    /// Switch execution shared across all versions: launches the official switch dialog and reports EngineAssociation changes.
    /// If a version diverges later, modify the corresponding case in <see cref="Execute"/> to branch differently.
    /// </summary>
    private static ToolResult RunSwitch(FileInfo projectFile, string associationBefore)
    {
        // 4. Run the switch dialog — interactive, then conversion may follow
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = UnrealVersionSelectorPath,
                Arguments = $"/switchversion \"{projectFile.FullName}\"",
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return ToolResult.Failed("Failed to start UnrealVersionSelector.exe");

            if (!process.WaitForExit(SwitchVersionTimeout))
                return ToolResult.Failed(
                    $"UnrealVersionSelector.exe did not finish within {SwitchVersionTimeout.TotalMinutes:0} minutes");

            if (process.ExitCode != 0)
                return ToolResult.Failed(
                    $"UnrealVersionSelector.exe exited with code {process.ExitCode} for '{projectFile.Name}'");
        }
        catch (Exception ex)
        {
            return ToolResult.Failed($"Version switch failed: {ex.Message}");
        }

        // 5. EngineAssociation after the switch — report the change
        string associationAfter = ProjectTool.GetEngineAssociation(projectFile.Directory!) ?? "";
        bool changed = !string.Equals(associationBefore, associationAfter, StringComparison.Ordinal);

        return ToolResult.Successful(
            changed
                ? $"Switched '{projectFile.Name}' from '{associationBefore}' to '{associationAfter}'"
                : $"No version change detected for '{projectFile.Name}' (dialog cancelled or same version selected)",
            new
            {
                projectFile = projectFile.FullName,
                engineAssociationBefore = associationBefore,
                engineAssociationAfter = associationAfter,
                changed
            });
    }
}
