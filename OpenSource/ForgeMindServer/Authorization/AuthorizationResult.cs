// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Authorization;

/// <summary>
/// Authorization result. Carries the full <see cref="Entitlement"/> on success;
/// carries machine-readable/human-readable error descriptions on failure.
/// No longer uses a bare bool to express authorization status — callers check abilities via Entitlement.
/// </summary>
public sealed class AuthorizationResult
{
    private AuthorizationResult(bool isAuthorized, Entitlement? entitlement, string? errorCode, string? errorMessage)
    {
        IsAuthorized = isAuthorized;
        Entitlement = entitlement;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Whether authorization was successful.</summary>
    public bool IsAuthorized { get; }

    /// <summary>The entitlement on authorization success. Null on failure.</summary>
    public Entitlement? Entitlement { get; }

    /// <summary>Machine-readable error code on failure (e.g., <c>expired</c> / <c>machine-mismatch</c>).</summary>
    public string? ErrorCode { get; }

    /// <summary>Human-readable error message on failure (can be used directly as ToolResult.Failed message).</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful result.</summary>
    public static AuthorizationResult Success(Entitlement entitlement) =>
        new(true, entitlement, null, null);

    /// <summary>Creates a failed result.</summary>
    public static AuthorizationResult Fail(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage);
}