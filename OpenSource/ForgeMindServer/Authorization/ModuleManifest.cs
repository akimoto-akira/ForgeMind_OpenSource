// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Authorization;

/// <summary>
/// Module Manifest. Carried by the plugin DLL during distribution,
/// declaring the module's identity, Core compatibility range, capabilities, and integrity info.
///
/// Integrity chain: module content → SHA-256 (PayloadHash) → signature (Signature).
/// The signature covers a canonical payload binding ModuleId + PublisherId + Version +
/// Capabilities + Compatibility (MinCoreVersion / MaxCoreVersion) + PayloadHash, so tampering
/// with any of these fields invalidates the signature (refactor plan §8).
/// </summary>
public sealed class ModuleManifest
{
    /// <summary>Unique module identifier.</summary>
    public string ModuleId { get; init; } = "";

    /// <summary>Publisher identifier.</summary>
    public string PublisherId { get; init; } = "";

    /// <summary>Module version.</summary>
    public string Version { get; init; } = "";

    /// <summary>Minimum supported Core version.</summary>
    public string MinCoreVersion { get; init; } = "";

    /// <summary>Maximum supported Core version (empty = no upper bound).</summary>
    public string MaxCoreVersion { get; init; } = "";

    /// <summary>Module capability list.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>SHA-256 hash of the module content (hexadecimal lowercase).</summary>
    public string PayloadHash { get; init; } = "";

    /// <summary>Publisher's signature on the PayloadHash (Base64).</summary>
    public string Signature { get; init; } = "";

    /// <summary>
    /// List of default services to replace (e.g., the commercial license plugin replaces the built-in
    /// <c>IAuthorizationService</c>). Core applies service replacements during plugin loading —
    /// Core itself has no concept of "commercial".
    /// </summary>
    public IReadOnlyList<ServiceOverride> ServiceOverrides { get; init; } = [];

    /// <summary>Converts to <see cref="ModuleIdentity"/> (identity portion).</summary>
    public ModuleIdentity ToModuleIdentity() => new()
    {
        ModuleId = ModuleId,
        PublisherId = PublisherId,
        Version = Version,
        MinCoreVersion = MinCoreVersion,
        MaxCoreVersion = MaxCoreVersion,
        Capabilities = Capabilities
    };
}

/// <summary>
/// Service override declaration: during plugin loading, the in-plugin implementation replaces the default service already registered in the container.
/// The commercial license plugin uses this mechanism to override the built-in open-source authorization implementation.
/// </summary>
public sealed class ServiceOverride
{
    /// <summary>The type name of the service to replace (including namespace, e.g., <c>ForgeMindServer.Authorization.IAuthorizationService</c>).</summary>
    public string ServiceType { get; init; } = "";

    /// <summary>The type name of the replacement implementation (within the plugin assembly).</summary>
    public string ImplementationType { get; init; } = "";

    /// <summary>Replacement behavior (<c>ReplaceDefault</c> overrides the default registration).</summary>
    public string Behavior { get; init; } = "ReplaceDefault";
}