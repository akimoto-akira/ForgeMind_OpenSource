// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Simple, mutable <see cref="IScanData"/> implementation (refactor plan §16).
/// Populated by scanners on the write side and deserialized from SPK on the read side.
/// </summary>
public sealed class ScanData : IScanData
{
    /// <summary>All scanned roots.</summary>
    public List<ScanRootView> RootList { get; } = [];

    /// <summary>Every class across all versions.</summary>
    public List<ScanClassView> ClassList { get; } = [];

    /// <summary>Functions grouped by declaring class name (ordinal lookup).</summary>
    public Dictionary<string, List<ScanFunctionView>> FunctionMap { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// UPROPERTY members per declaring class. Optional payload: writers that predate property
    /// capture (or a pack without it) simply leave this empty, and readers report "unknown".
    /// </summary>
    public Dictionary<string, List<ScanPropertyView>> PropertyMap { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    IReadOnlyList<ScanRootView> IScanData.Roots => RootList;

    /// <inheritdoc />
    IReadOnlyList<ScanClassView> IScanData.Classes => ClassList;

    /// <inheritdoc />
    IReadOnlyDictionary<string, IReadOnlyList<ScanFunctionView>> IScanData.FunctionsByClass =>
        FunctionMap.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<ScanFunctionView>)kvp.Value,
            StringComparer.Ordinal);

    /// <inheritdoc />
    IReadOnlyDictionary<string, IReadOnlyList<ScanPropertyView>> IScanData.PropertiesByClass =>
        PropertyMap.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<ScanPropertyView>)kvp.Value,
            StringComparer.Ordinal);
}