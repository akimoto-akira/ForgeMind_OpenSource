// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Descriptor of a scannable target (refactor plan §3/§9): a target type plus identity
/// metadata. Engine-agnostic — Unreal is just the first target type ("UnrealEngine");
/// future targets (Unity / Godot / custom engines) fit without changing this contract.
/// </summary>
public sealed class ScanTargetDescriptor
{
    /// <summary>Open-ended target type identifier (e.g. "UnrealEngine").</summary>
    public required string TargetType { get; init; }

    /// <summary>Stable target identifier, machine-independent (no absolute paths).</summary>
    public string TargetId { get; init; } = "";

    /// <summary>Display name for diagnostics only — never used as identity.</summary>
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// A version namespace inside a target (e.g. "5.0"). Pure data attribute — never an
/// identity (same rule as the existing scan models: version is data, not identity).
/// </summary>
public readonly record struct ScanVersion(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}