// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Authorization;

/// <summary>
/// Authorization service abstraction — the sole authorization entry point Core depends on.
/// Core / plugins / tools only recognize this interface and depend on no specific authorization
/// implementation (the open-source <c>OpenAuthorizationService</c> or a commercial plugin provider).
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Authenticates the module identity and returns the entitlement. Core answers via this interface:
    /// "who this module is, whether it is trusted, and what capabilities it has rights to."
    /// </summary>
    /// <param name="module">The identity declaration of the module to authenticate.</param>
    /// <param name="context">Authorization context (machine, session, Core version, etc.).</param>
    /// <returns>Authentication result: carries an <see cref="Entitlement"/> on success, or an error message on failure.</returns>
    AuthorizationResult Authenticate(ModuleIdentity module, AuthorizationContext context);

    /// <summary>
    /// Checks whether the specified capability is authorized. Convenience wrapper around the
    /// <see cref="Authenticate"/> result's Entitlement feature check; returns a non-null error message on failure.
    /// </summary>
    /// <param name="capability">Capability name (e.g., tool name).</param>
    /// <param name="module">The module identity requesting the capability.</param>
    /// <param name="context">Authorization context.</param>
    /// <returns>null indicates authorization passed; non-null indicates an error description of denial.</returns>
    string? RequireCapability(string capability, ModuleIdentity module, AuthorizationContext context);
}