// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Authorization.Open;

/// <summary>
/// Built-in open-source authorization service — Core's default authorization provider (open-source edition).
///
/// The essence of open-source authorization: **does not parse any license files** (does not read .fml,
/// does not generate trial licenses, does not connect to license servers), and provides callers with a
/// Community contract with **all capabilities open**:
///
/// - Capability range is wildcard (<see cref="OpenCapabilities"/> = <c>*</c>), no restrictions —
///   all module/plugin capabilities are allowed (including future Material / Niagara plugins).
/// - The UI tool (license) returns authorization info normally (Community / all capabilities).
/// - License management actions (import license file / server license) are provided by the commercial
///   plugin only. The open-source edition implements <see cref="IAuthorizationService"/> only — the
///   commercial license tool detects the absence of the <c>ILicenseAdmin</c> interface and returns a
///   "Community edition unsupported" message accordingly.
/// </summary>
public sealed class OpenAuthorizationService : IAuthorizationService
{
    /// <summary>Authorization provider identifier.</summary>
    public const string ProviderName = "open";

    /// <summary>Open-source edition version identifier (Community Edition).</summary>
    public const string CommunityEdition = "Community";

    /// <summary>Open-source authorization capability range: full wildcard (Community Edition has no restrictions).</summary>
    public static IReadOnlySet<string> OpenCapabilities { get; } = new HashSet<string>(
        ["*"], StringComparer.Ordinal);

    /// <summary>Open-source edition allows any capability (wildcard).</summary>
    public static bool IsOpenCapability(string capability) => true;

    /// <summary>Authenticates module identity: open-source edition always succeeds, returns a full-capability Community entitlement.</summary>
    public AuthorizationResult Authenticate(ModuleIdentity module, AuthorizationContext context)
    {
        return AuthorizationResult.Success(new Entitlement
        {
            Product = "ForgeMind",
            Module = module.ModuleId.Length > 0 ? module.ModuleId : "ForgeMind.Core",
            Edition = CommunityEdition,
            Licensee = "Open Source / Community",
            Features = ["*"],
            ExpiresAt = null,
            EntitlementId = "open-community",
            Provider = ProviderName
        });
    }

    /// <summary>Open-source edition does not block any capability.</summary>
    public string? RequireCapability(string capability, ModuleIdentity module, AuthorizationContext context)
    {
        return null;
    }
}