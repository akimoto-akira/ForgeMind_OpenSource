// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Diagnostics;
using ForgeMindServer;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.OfflineEngineKit;

/// <summary>
/// Engine-agnostic IDE / build-system project file management (Requirements.md 2.4.2).
/// <para>Regenerating project files is engine-specific (each engine emits its own
/// .sln / .cmake / .code-workspace / .vcxproj), so the difference is dispatched by
/// <see cref="EngineVersion"/> rather than split across engine-specific files or
/// engine-specific tool registrations.</para>
/// </summary>
public sealed class ProjectFilesTool : ITool
{
    /// <summary>Engine-agnostic tool name.</summary>
    public string Name => "project_files";

    public string Description =>
        "Regenerate the IDE solution and project files of an engine project. " +
        "Requires the 'path' parameter - either the project file or the folder containing it. " +
        "Note: the regeneration runs tens of seconds to several minutes.";

    public string GetDisplayName() => "Regenerate Project Files";

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
                    ["description"] = "Path to the project file, or the folder containing it"
                }
            },
            ["required"] = new[] { "path" }
        };
    }

    private readonly ServerPermissionChecker _permChecker;

    internal ProjectFilesTool(ServerPermissionChecker permChecker)
    {
        _permChecker = permChecker;
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        // Dispatch by engine version: one branch per supported version. They currently share
        // one implementation; when a version genuinely differs, change that case only - no new
        // engine-specific file, no extra registration.
        EngineVersion? engineVersion = ResolveEngineVersion(parameters);
        return engineVersion switch
        {
            EngineVersion.UE_5_0 => RunProjectFiles(parameters),
            EngineVersion.UE_5_1 => RunProjectFiles(parameters),
            EngineVersion.UE_5_2 => RunProjectFiles(parameters),
            EngineVersion.UE_5_3 => RunProjectFiles(parameters),
            EngineVersion.UE_5_4 => RunProjectFiles(parameters),
            EngineVersion.UE_5_5 => RunProjectFiles(parameters),
            EngineVersion.UE_5_6 => RunProjectFiles(parameters),
            EngineVersion.UE_5_7 => RunProjectFiles(parameters),
            EngineVersion.UE_5_8 => RunProjectFiles(parameters),
            _ => RunProjectFiles(parameters)
        };
    }

    /// <summary>
    /// Resolves the engine version from 'path' for dispatch. Null when the path is absent or
    /// invalid, or when the version is not one of the listed ones - those fall to the default
    /// branch.
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
    /// Implementation shared by every version today: resolve the project file, then hand off to
    /// the engine's own project-file generator and wait for it.
    /// </summary>
    private ToolResult RunProjectFiles(Dictionary<string, object> parameters)
    {
        string? permError = _permChecker.RequirePermission(Name);
        if (permError != null)
            return ToolResult.Failed(permError);

        if (!parameters.TryGetValue("path", out object? pathObj) ||
            string.IsNullOrWhiteSpace(pathObj?.ToString()))
        {
            return ToolResult.Failed("Missing 'path' parameter");
        }

        FileInfo? projectFile = ProjectTool.ResolveProjectFile(pathObj.ToString()!);
        if (projectFile == null)
        {
            return ToolResult.Failed(
                $"No valid project file found at '{pathObj}' - expected a project file or the folder containing it");
        }

        return Regenerate(projectFile);
    }

    /// <summary>
    /// Fixed location of the Epic Launcher's UnrealVersionSelector, which performs the
    /// project file regeneration for the associated engine.
    /// </summary>
    private const string UnrealVersionSelectorPath =
        @"C:\Program Files (x86)\Epic Games\Launcher\Engine\Binaries\Win64\UnrealVersionSelector.exe";

    /// <summary>
    /// Generous wait limit for the regeneration, which normally runs tens of seconds to
    /// several minutes.
    /// </summary>
    private static readonly TimeSpan RegenerationTimeout = TimeSpan.FromMinutes(10);

    private static ToolResult Regenerate(FileInfo projectFile)
    {
        if (!File.Exists(UnrealVersionSelectorPath))
            return ToolResult.Failed($"UnrealVersionSelector.exe not found: {UnrealVersionSelectorPath}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = UnrealVersionSelectorPath,
                Arguments = $"/projectfiles \"{projectFile.FullName}\"",
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return ToolResult.Failed("Failed to start UnrealVersionSelector.exe");

            if (!process.WaitForExit(RegenerationTimeout))
                return ToolResult.Failed(
                    $"UnrealVersionSelector.exe did not finish within {RegenerationTimeout.TotalMinutes:0} minutes");

            if (process.ExitCode != 0)
                return ToolResult.Failed(
                    $"UnrealVersionSelector.exe exited with code {process.ExitCode} for '{projectFile.Name}'");

            return ToolResult.Successful(
                $"Regenerated Visual Studio project files for '{projectFile.Name}'",
                new
                {
                    projectFile = projectFile.FullName,
                    command = $"\"{UnrealVersionSelectorPath}\" /projectfiles \"{projectFile.FullName}\"",
                    exitCode = process.ExitCode
                });
        }
        catch (Exception ex)
        {
            return ToolResult.Failed($"Regeneration failed: {ex.Message}");
        }
    }
}
