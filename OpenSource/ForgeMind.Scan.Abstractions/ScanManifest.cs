// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Well-known data type identifiers used by the built-in scanners. Open-ended —
/// future targets may introduce additional types without changing the contract.
/// </summary>
public static class ScanDataTypes
{
    public const string Code = "code";
    public const string Asset = "asset";
    public const string Signature = "signature";
    public const string Index = "index";
}

/// <summary>
/// SPK top-level manifest model (refactor plan §8): format/scanner/target/schema
/// metadata. Pure data — serialized into manifest/package.json; never carries
/// machine-specific paths (sanitize before write, refactor plan §6/§12).
/// </summary>
public sealed class ScanManifest
{
    public int FormatVersion { get; init; } = 1;

    public int SchemaVersion { get; init; } = 1;

    /// <summary>Scanner that produced the package (e.g. "ForgeMind.Unreal").</summary>
    public required string ScannerId { get; init; }

    public string ScannerVersion { get; init; } = "";

    /// <summary>Open-ended target type (e.g. "UnrealEngine").</summary>
    public required string TargetType { get; init; }

    /// <summary>All target versions covered by the package (e.g. "5.0", "5.8").</summary>
    public IReadOnlyList<string> TargetVersions { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Data types present in the package (code / asset / signature / ...).</summary>
    public IReadOnlyList<string> DataTypes { get; init; } = [];
}