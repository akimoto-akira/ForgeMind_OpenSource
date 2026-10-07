// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// One logical entry of a scan package (refactor plan §7/§12): an addressable unit of
/// scan data within a version namespace. Generic by design — Unreal-specific entry
/// types (asset entries, function signatures) live in the Unreal scanner provider,
/// never here.
/// </summary>
public sealed class ScanEntry
{
    /// <summary>Entry identifier, unique within its version namespace (e.g. "code/classes-000").</summary>
    public required string Id { get; init; }

    /// <summary>Version namespace the entry belongs to (e.g. "5.0").</summary>
    public required string Version { get; init; }

    /// <summary>Open-ended data type (code / asset / signature / ...).</summary>
    public required string DataType { get; init; }

    /// <summary>Machine-independent relative path inside the package.</summary>
    public string RelativePath { get; init; } = "";
}

/// <summary>
/// Version-isolated index over a scan's entries (refactor plan §9): one logical data
/// domain per target version. Lookup is namespace → entry id → entry; large packages
/// must never force a single huge entry — chunking is the writer's responsibility.
/// </summary>
public sealed class ScanIndex
{
    private readonly Dictionary<string, List<ScanEntry>> _byVersion = new(StringComparer.Ordinal);

    /// <summary>All version namespaces present in the index, in ordinal order.</summary>
    public IReadOnlyList<string> Versions =>
        _byVersion.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>Adds an entry under its version namespace.</summary>
    public void Add(ScanEntry entry)
    {
        if (!_byVersion.TryGetValue(entry.Version, out List<ScanEntry>? list))
        {
            list = [];
            _byVersion[entry.Version] = list;
        }

        list.Add(entry);
    }

    /// <summary>Entries of one version namespace, in add order; empty when the namespace is unknown.</summary>
    public IReadOnlyList<ScanEntry> EntriesOf(string version) =>
        _byVersion.TryGetValue(version, out List<ScanEntry>? list) ? list : [];

    /// <summary>Finds an entry by id within a version namespace; null when absent.</summary>
    public ScanEntry? Find(string version, string entryId) =>
        EntriesOf(version).FirstOrDefault(e => string.Equals(e.Id, entryId, StringComparison.Ordinal));
}