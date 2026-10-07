// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.DSL;

/// <summary>
/// Thrown when an unrecognized domain attribute appears in DSL code.
/// Unrecognized domains never silently fall back (AI Collaboration Spec.md §2.3).
/// </summary>
public sealed class UnrecognizedDomainException : Exception
{
    /// <summary>The detected candidate attribute name (may be empty).</summary>
    public IReadOnlyList<string> FoundAttributes { get; }

    public UnrecognizedDomainException(string message, IReadOnlyList<string>? foundAttributes = null)
        : base(message)
    {
        FoundAttributes = foundAttributes ?? Array.Empty<string>();
    }
}
