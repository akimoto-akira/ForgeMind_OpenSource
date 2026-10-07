// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.OfflineEngineKit;

/// <summary>
/// Open-source permission guard: passes every tool (no commercial entitlement
/// restrictions). A commercial build that replaces IAuthorizationService can
/// supply a rate-limited guard instead.
/// </summary>
internal sealed class ServerPermissionChecker
{
    /// <summary>Returns null to allow the tool; a string is the rejection reason.</summary>
    public string? RequirePermission(string toolName) => null;
}
