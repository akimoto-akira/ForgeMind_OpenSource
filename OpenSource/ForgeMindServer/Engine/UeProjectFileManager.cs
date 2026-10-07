// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;

namespace ForgeMindServer.Engine;

/// <summary>
/// Host-side public utility for locating and parsing Unreal Engine
/// <c>.uproject</c> files. Shared by both host core infrastructure
/// (DSL, offline scan index, MCP handler, <c>UnrealProjectFilesTool</c>) and the
/// OfflineEngineKit plugin — the plugin reaches it only through the host exe
/// reference, never the other way around. The plugin owns the higher-level
/// analysis behavior (plugin/companion detection, integrity scoring); this class
/// only provides the file-level primitives.
/// </summary>
public static class UeProjectFileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Checks whether the directory contains a valid UE project, returning the
    /// best project file (the one whose stem matches the directory name).
    /// </summary>
    public static bool CheckProjectInDirectory(DirectoryInfo directory, out FileInfo? projectFile)
    {
        projectFile = null;

        try
        {
            FileInfo[] projectFiles = GetProjectFiles(directory);
            if (projectFiles.Length == 0)
                return false;

            projectFile = SelectBestProjectFile(directory, projectFiles);
            return ValidateProjectFile(projectFile);
        }
        catch
        {
            projectFile = null;
            return false;
        }
    }

    /// <summary>Gets all .uproject files in the directory (top level only).</summary>
    public static FileInfo[] GetProjectFiles(DirectoryInfo directory)
    {
        try
        {
            return directory.GetFiles("*.uproject", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Picks the project file whose stem matches the directory name when ambiguous.</summary>
    public static FileInfo SelectBestProjectFile(DirectoryInfo directory, FileInfo[] projectFiles)
    {
        if (projectFiles.Length == 1)
            return projectFiles[0];

        string dirName = directory.Name;
        FileInfo? matchingFile = projectFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f.Name), dirName, StringComparison.OrdinalIgnoreCase));

        return matchingFile ?? projectFiles[0];
    }

    /// <summary>Validates a project file: must parse as JSON with FileVersion &gt; 0.</summary>
    public static bool ValidateProjectFile(FileInfo projectFile)
    {
        if (!projectFile.Exists)
            return false;

        try
        {
            string? content = UnrealTextReader.LoadStringAuto(projectFile);
            if (string.IsNullOrWhiteSpace(content))
                return false;

            UnrealProjectDescription? projectDesc = JsonSerializer.Deserialize<UnrealProjectDescription>(content, JsonOptions);
            return projectDesc.HasValue && projectDesc.Value.FileVersion > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses the project description from a .uproject file. Throws on failure.
    /// </summary>
    public static UnrealProjectDescription ParseProjectDescription(FileInfo projectFile)
    {
        string? content = UnrealTextReader.LoadStringAuto(projectFile);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Project file is empty");

        UnrealProjectDescription? desc = JsonSerializer.Deserialize<UnrealProjectDescription>(content, JsonOptions);
        return desc ?? throw new InvalidOperationException("Failed to parse project description");
    }

    /// <summary>
    /// Reads the EngineAssociation of the project in the given directory.
    /// Returns null when the directory is not a valid UE project or parsing fails.
    /// </summary>
    public static string? GetEngineAssociation(DirectoryInfo directory)
    {
        if (!CheckProjectInDirectory(directory, out FileInfo? projectFile))
            return null;

        try
        {
            return ParseProjectDescription(projectFile!).EngineAssociation;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a valid .uproject from a path that may point to the project
    /// file itself or to the folder containing it. Returns null when none found.
    /// </summary>
    public static FileInfo? ResolveProjectFile(string path)
    {
        try
        {
            if (File.Exists(path) && path.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))
            {
                var file = new FileInfo(path);
                return ValidateProjectFile(file) ? file : null;
            }

            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
                return null;

            return CheckProjectInDirectory(directory, out FileInfo? projectFile) ? projectFile : null;
        }
        catch
        {
            return null;
        }
    }
}
