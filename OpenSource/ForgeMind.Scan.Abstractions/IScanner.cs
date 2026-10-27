// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Generic scanner contract — the only scan-side concepts the Core / Scan framework
/// understands (refactor plan §3). A scanner knows how to extract structured knowledge
/// from a specific kind of target (an engine installation, a project, any root);
/// everything product-specific (engine names, asset formats, domain semantics) belongs
/// to concrete scanner implementations, never to this contract.
/// </summary>
public interface IScanner
{
    /// <summary>Stable identifier of this scanner (e.g. "ForgeMind.Unreal").</summary>
    string ScannerId { get; }

    /// <summary>Version of this scanner implementation.</summary>
    string ScannerVersion { get; }

    /// <summary>Discovers scannable targets on this machine (best effort; empty when none).</summary>
    IReadOnlyList<ScanTargetDescriptor> DiscoverTargets();

    /// <summary>Runs the scan lifecycle (discover → scan → normalize) over one target.</summary>
    IScanResult Scan(IScanTarget target, CancellationToken cancellationToken = default);
}

/// <summary>A concrete scannable target: one engine installation, one project, any root.</summary>
public interface IScanTarget
{
    /// <summary>Target descriptor (target type, identifier, display name).</summary>
    ScanTargetDescriptor Descriptor { get; }

    /// <summary>Sources the scan reads from (directories, files, streams).</summary>
    IReadOnlyList<IScanSource> Sources { get; }
}

/// <summary>
/// A single source a scan reads from. Kinds are open-ended
/// (directory / file / stream / ...); identifiers must stay machine-independent.
/// </summary>
public interface IScanSource
{
    /// <summary>Source kind identifier (e.g. "directory", "file", "stream").</summary>
    string Kind { get; }

    /// <summary>Machine-independent identifier of the source within the target.</summary>
    string Identifier { get; }
}

/// <summary>Normalized result of a scan: a manifest, the version-isolated entry index, and the
/// view data consumers query through <see cref="IScanDataProvider"/>.</summary>
public interface IScanResult
{
    /// <summary>Scan manifest (format/schema/scanner/target metadata).</summary>
    ScanManifest Manifest { get; }

    /// <summary>All entries produced by the scan, grouped by version namespace.</summary>
    ScanIndex Index { get; }

    /// <summary>View data (roots, classes, functions) that the runtime serves through IScanDataProvider.</summary>
    IScanData Data { get; }
}

/// <summary>
/// Generic scan data as view types (refactor plan §16). This is what an SPK-backed
/// <see cref="IScanDataProvider"/> deserializes from the pack — engine-agnostic views,
/// never scanner-specific model types.
/// </summary>
public interface IScanData
{
    /// <summary>All scanned roots, one per scan scope.</summary>
    IReadOnlyList<ScanRootView> Roots { get; }

    /// <summary>Every class across all versions, in scan order.</summary>
    IReadOnlyList<ScanClassView> Classes { get; }

    /// <summary>Functions grouped by declaring class name (ordinal lookup).</summary>
    IReadOnlyDictionary<string, IReadOnlyList<ScanFunctionView>> FunctionsByClass { get; }

    /// <summary>
    /// UPROPERTY members grouped by declaring class name (ordinal lookup). Optional: packs that
    /// predate property capture carry none, so the default is an empty map rather than a failure.
    /// Consumers must treat "absent" as unknown — never guess (spec §8).
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<ScanPropertyView>> PropertiesByClass
        => new Dictionary<string, IReadOnlyList<ScanPropertyView>>();
}