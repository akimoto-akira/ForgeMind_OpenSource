// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Authorization;

/// <summary>
/// Module identity. Answers "who are you":
/// the module's unique identifier, publisher, version, Core compatibility, and capability declarations.
/// Identity is separate from Entitlement (<see cref="Entitlement"/>) — identity describes what the module is,
/// entitlement describes what capabilities the module has rights to.
/// </summary>
public sealed class ModuleIdentity
{
    /// <summary>Unique module identifier (e.g., <c>ForgeMind.Blueprint</c>).</summary>
    public string ModuleId { get; init; } = "";

    /// <summary>Publisher identifier (e.g., <c>ForgeMind</c>).</summary>
    public string PublisherId { get; init; } = "";

    /// <summary>Module version (semantic version number, e.g., <c>1.2.0</c>).</summary>
    public string Version { get; init; } = "";

    /// <summary>Declares the minimum supported Core version.</summary>
    public string MinCoreVersion { get; init; } = "";

    /// <summary>Declares the maximum supported Core version (empty = no upper bound).</summary>
    public string MaxCoreVersion { get; init; } = "";

    /// <summary>List of capabilities declared by the module (e.g., <c>Blueprint.Create</c>).</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>
    /// Whether the module is compatible with the specified Core version.
    /// Simple string version comparison; can be upgraded to semantic version comparison later.
    /// </summary>
    public bool IsCompatibleWithCore(string coreVersion)
    {
        if (MinCoreVersion.Length > 0 &&
            string.CompareOrdinal(coreVersion, MinCoreVersion) < 0)
            return false;

        if (MaxCoreVersion.Length > 0 &&
            string.CompareOrdinal(coreVersion, MaxCoreVersion) > 0)
            return false;

        return true;
    }
}