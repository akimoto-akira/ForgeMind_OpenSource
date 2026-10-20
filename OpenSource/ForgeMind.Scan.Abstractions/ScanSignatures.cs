// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>One parameter (pin) of a function signature.</summary>
/// <param name="Name">Parameter name, which is also the pin name.</param>
/// <param name="Type">Declared type; null when unknown.</param>
/// <param name="IsOut">
/// Whether it is an out parameter (a non-const C++ reference "T&amp;"): an out parameter is an
/// OUTPUT pin on the call node, so a call-site out argument must be wired as
/// "call node out pin → variable write", never as a positional input.
/// </param>
public sealed record ScanParameterSignature(string Name, string? Type = null, bool IsOut = false);

/// <summary>
/// A function signature as consumed for call-site wiring: it is what lets a consumer map
/// actual arguments onto pin names.
/// </summary>
/// <param name="Owner">Declaring type name.</param>
/// <param name="Name">Function name.</param>
/// <param name="IsStatic">Whether it is static (a static function's first WorldContextObject argument is implicit).</param>
/// <param name="Params">Parameters in pin order.</param>
public sealed record ScanFunctionSignature(
    string Owner,
    string Name,
    bool IsStatic,
    IReadOnlyList<ScanParameterSignature> Params)
{
    /// <summary>
    /// Whether it is a pure function (no exec pin, so it does not occupy the exec chain).
    /// Consumers must treat "no signature" as impure rather than as pure.
    /// </summary>
    public bool IsPure { get; init; }
}

/// <summary>Outcome of resolving which type declares a called function.</summary>
public enum ScanOwnerLookup
{
    /// <summary>No declaration of that name at all: not an engine/library function, so a lenient fallback applies.</summary>
    NoDeclaration,
    /// <summary>Declarations exist but none matches the call-site types: a real error.</summary>
    NoTypeMatch,
    /// <summary>Resolved; the owner type name is returned.</summary>
    Resolved
}

/// <summary>Shape of a member declared on a parent blueprint asset, as reported through the bridge.</summary>
public enum ScanParentMemberKind
{
    /// <summary>An overridable event.</summary>
    Event,
    /// <summary>An overridable or callable function.</summary>
    Function,
    /// <summary>A variable / field.</summary>
    Variable,
    /// <summary>A component.</summary>
    Component
}