// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;

namespace ForgeMind.Scan;

/// <summary>
/// A simple, concrete <see cref="IScanTarget"/> for programmatic construction
/// (refactor plan §15). Carries a descriptor and a fixed source list — sufficient
/// for CLI flows and tests. Scanner providers that need richer target resolution
/// can implement <see cref="IScanTarget"/> directly.
/// </summary>
public sealed class SimpleScanTarget : IScanTarget
{
    /// <summary>Target descriptor (type, id, display name). Required — caller must supply.</summary>
    public required ScanTargetDescriptor Descriptor { get; init; }

    /// <summary>Sources the scan reads from (directories, files, streams).</summary>
    public IReadOnlyList<IScanSource> Sources { get; init; } = [];
}

/// <summary>
/// A simple, concrete <see cref="IScanSource"/> — a kind tag plus a machine-independent
/// identifier (e.g. kind "directory", identifier "C:/Program Files/Epic/UE_5.0").
/// </summary>
public sealed class SimpleScanSource : IScanSource
{
    /// <summary>Source kind ("directory", "file", "stream", ...).</summary>
    public string Kind { get; init; } = "";

    /// <summary>Machine-independent identifier of the source within the target.</summary>
    public string Identifier { get; init; } = "";
}