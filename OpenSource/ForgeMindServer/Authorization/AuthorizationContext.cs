// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Authorization;

/// <summary>
/// Authorization context. Carries environmental information during an authorization request:
/// the requester's machine code, machine name, process/session identifier, etc.
/// The authorization service uses this context to interpret module entitlements.
/// </summary>
public sealed class AuthorizationContext
{
    /// <summary>The requester's machine code (e.g., the result of the commercial authorization plugin's <c>MachineCode.Generate()</c>).</summary>
    public string MachineCode { get; init; } = "";

    /// <summary>The requester's machine name.</summary>
    public string MachineName { get; init; } = "";

    /// <summary>The requester's process/session identifier (optional, used for multi-session concurrency checks).</summary>
    public string SessionId { get; init; } = "";

    /// <summary>The Core's own version (used by the authorization service to verify module compatibility).</summary>
    public string CoreVersion { get; init; } = "";
}