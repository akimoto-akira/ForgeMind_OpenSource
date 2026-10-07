// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Authorization;

/// <summary>
/// Entitlement: answers "are you qualified to use these capabilities":
/// product, module, edition, feature set, machine/session, expiration, and enterprise policy./// Authorization results no longer return a bare bool but the full Entitlement—
/// plugins/tools determine what to enable based on the feature list within.
/// </summary>
public sealed class Entitlement
{
    /// <summary>Product identifier (e.g., <c>ForgeMind</c>).</summary>
    public string Product { get; init; } = "";

    /// <summary>Module identifier (e.g., <c>Blueprint</c>).</summary>
    public string Module { get; init; } = "";

    /// <summary>Edition identifier (e.g., <c>Community</c> / <c>Pro</c> / <c>Enterprise</c>).</summary>
    public string Edition { get; init; } = "";

    /// <summary>Licensee (individual or organization name).</summary>
    public string Licensee { get; init; } = "";

    /// <summary>Feature list associated with this entitlement (e.g., <c>Blueprint.Create</c>).</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>Expiration time (UTC). null indicates perpetual / enterprise with no expiration.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Unique identifier of the license file.</summary>
    public string EntitlementId { get; init; } = "";

    /// <summary>Authorization provider identifier (<c>open</c> / <c>commercial</c>, etc.).</summary>
    public string Provider { get; init; } = "";

    /// <summary>Whether the entitlement is still active (not expired).</summary>
    public bool IsActive =>
        ExpiresAt == null || ExpiresAt.Value > DateTime.UtcNow;

    /// <summary>Checks whether a specific capability is granted (<c>*</c> represents all capabilities).</summary>
    public bool CheckCapability(string capability) =>
        Features.Contains("*") || Features.Contains(capability);
}