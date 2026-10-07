// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Authorization;

namespace ForgeMindServer;

/// <summary>
/// Public accessor for host runtime state. Commercial plugins (license / machine-code tools) use this
/// to read the current authorization service, module identity, and authorization context without
/// requiring internal visibility into <see cref="ForgeMindServerHost"/>.
/// </summary>
public static class HostAccessor
{
    /// <summary>The current authorization service (open-source or commercial-overridden).</summary>
    public static IAuthorizationService AuthorizationService => ForgeMindServerHost.AuthorizationService;

    /// <summary>The host's own module identity.</summary>
    public static ModuleIdentity CoreModuleIdentity => ForgeMindServerHost.CoreModuleIdentity;

    /// <summary>The host's own authorization context (machine code + core version).</summary>
    public static AuthorizationContext CoreAuthorizationContext => ForgeMindServerHost.CoreAuthorizationContext;
}
