// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ForgeMindServer.Engine;

/// <summary>
/// Host-side public utility for detecting installed Unreal Engine builds and
/// resolving a <c>.uproject</c> EngineAssociation to a concrete installation.
/// Enumerates engines registered by the Epic Launcher (<c>LauncherInstalled.dat</c>)
/// and source builds registered in the Windows registry, and matches an
/// association (launcher version like "5.4" or a source-build GUID) to an install.
/// <para>Shared by the host (engine discovery at startup, S3) and the
/// OfflineEngineKit plugin (engine / project tools). The plugin reaches it only
/// through the host exe reference, never the other way around.</para>
/// </summary>
public static class UnrealEngineLocator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Epic Launcher installation record file.</summary>
    private const string LauncherInstallDatPath =
        @"C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat";

    /// <summary>Registry path where source builds register their GUID → install path mapping.</summary>
    private const string BuildsRegistrySubKey =
        @"SOFTWARE\Epic Games\Unreal Engine\Builds";

    /// <summary>
    /// Collects all Unreal Engine installations:
    /// ① Epic Launcher record file (LauncherInstalled.dat);
    /// ② source builds registered under HKCU\SOFTWARE\Epic Games\Unreal Engine\Builds
    ///    (Windows-only, requires Capability.Registry).
    /// </summary>
    public static UnrealEngineInstallInfo[] GetAllUnrealApplications()
    {
        var result = new List<UnrealEngineInstallInfo>();

        // ① Epic Launcher installed engines
        FileInfo installInfoFile = new(LauncherInstallDatPath);
        if (installInfoFile.Exists)
        {
            try
            {
                string? json = UnrealTextReader.LoadStringAuto(installInfoFile);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    LauncherInstallList? list = JsonSerializer.Deserialize<LauncherInstallList>(json, JsonOptions);
                    if (list?.InstallationList != null)
                        result.AddRange(list.InstallationList);
                }
            }
            catch
            {
                // Ignore malformed LauncherInstalled.dat
            }
        }

        // ② Source builds from the registry (Windows only)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using RegistryKey? unrealBuildKey = Registry.CurrentUser.OpenSubKey(BuildsRegistrySubKey);
                if (unrealBuildKey != null)
                {
                    foreach (string buildName in unrealBuildKey.GetValueNames())
                    {
                        try
                        {
                            if (!Guid.TryParse(buildName, out Guid guid))
                                continue;

                            if (unrealBuildKey.GetValue(buildName) is not string buildPath ||
                                string.IsNullOrWhiteSpace(buildPath))
                            {
                                continue;
                            }

                            DirectoryInfo buildLocation = new(buildPath);

                            // Load the per-build version descriptor
                            FileInfo buildVersionFile =
                                new(Path.Combine(buildLocation.FullName, "Engine", "Build", "Build.version"));
                            string? buildVersionString = UnrealTextReader.LoadString(buildVersionFile, Encoding.UTF8);
                            if (buildVersionString == null)
                                continue;

                            UnrealBuildInfo? buildInfo =
                                JsonSerializer.Deserialize<UnrealBuildInfo>(buildVersionString, JsonOptions);
                            if (buildInfo == null)
                                continue;

                            result.Add(new UnrealEngineInstallInfo
                            {
                                AppName = "Binary build at " + buildLocation.FullName,
                                InstallLocation = buildLocation.FullName,
                                NamespaceId = "regUe",
                                ArtifactId = "Build_" + buildLocation.FullName,
                                ItemId = guid.ToString("N"),
                                AppVersion = buildInfo.Value.ToString()
                            });
                        }
                        catch
                        {
                            // Skip malformed registry entries
                        }
                    }
                }
            }
            catch
            {
                // Registry unavailable — launcher results still apply
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Whether any Unreal Engine (launcher or source build) is installed.
    /// </summary>
    public static bool IsUnrealEngineInstalled()
    {
        foreach (UnrealEngineInstallInfo info in GetAllUnrealApplications())
        {
            string ns = info.NamespaceId?.ToLowerInvariant() ?? "";
            if (ns is "ue" or "regue")
            {
                if ((info.AppName ?? "").ToLowerInvariant().StartsWith("ue_"))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a .uproject EngineAssociation to an installed engine:
    /// first by GUID (source builds), then by "UE_&lt;version&gt;" app name (launcher builds).
    /// Returns <see langword="null"/> when nothing matches.
    /// </summary>
    public static UnrealEngineInstallInfo? FindUnrealEngine(string association)
    {
        UnrealEngineInstallInfo[] allApplications = GetAllUnrealApplications();

        // GUID association → source build registered in the registry
        if (Guid.TryParse(association, out Guid guid))
        {
            string guidString = guid.ToString("N");

            foreach (UnrealEngineInstallInfo app in allApplications)
            {
                if (app.ItemId == guidString)
                {
                    string ns = app.NamespaceId?.ToLowerInvariant() ?? "";
                    if (ns == "regue")
                        return app;

                    if (ns == "ue" && (app.AppName ?? "").ToLowerInvariant().StartsWith("ue_"))
                        return app;
                }
            }
        }

        // Version association → launcher build named "UE_<version>"
        foreach (UnrealEngineInstallInfo app in allApplications)
        {
            if (app.AppName == "UE_" + association)
                return app;
        }

        return null;
    }
}
