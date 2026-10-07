// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.Scan;

/// <summary>
/// Extension methods that compose the scan lifecycle with SPK persistence
/// (refactor plan §15). A caller invokes <see cref="ScanAndPack"/> to run a scanner
/// and write the result directly to SPK in one step — the "Scanner → SPK" production
/// chain without an intermediate ScanCache.
/// </summary>
public static class ScannerPackExtensions
{
    /// <summary>
    /// Scans the target and packs the result into the SPK writer. Equivalent to
    /// <c>packer.Pack(scanner.Scan(target, token))</c> but convenient for one-shot CLI flows.
    /// </summary>
    public static void ScanAndPack(
        this IScanner scanner,
        IScanTarget target,
        IPackWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(writer);

        IScanResult result = scanner.Scan(target, cancellationToken);
        var packer = new ScanResultPacker(writer);
        packer.Pack(result);
    }

    /// <summary>
    /// Scans multiple targets and packs all results into a single SPK writer. Each
    /// target's entries are written under its own version namespace; the manifest is
    /// merged (union of target versions and data types); view data is written under
    /// <c>data/{version}/</c> so multiple versions never shadow each other.
    /// <para>Per-target failure isolation (refactor plan §4): a single failed target
    /// (e.g. a corrupt engine installation) does not abort the entire scan. Successful
   /// targets are still packed into the SPK; the failure is recorded and surfaced
    /// after packing so the CLI exit code reflects the partial failure.</para>
    /// </summary>
    /// <exception cref="AggregateException">Thrown after successful targets have been
    /// packed when one or more targets failed to scan. The SPK is still valid and
    /// contains all successful results.</exception>
    public static void ScanAndPackMany(
        this IScanner scanner,
        IReadOnlyList<IScanTarget> targets,
        IPackWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(writer);

        var results = new List<IScanResult>(targets.Count);
        var failures = new List<(IScanTarget Target, Exception Error)>();
        foreach (IScanTarget target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(scanner.Scan(target, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add((target, ex));
            }
        }

        var packer = new ScanResultPacker(writer);
        packer.PackMany(results);

        // Surface failures only after successful results are persisted — the SPK is valid
        if (failures.Count > 0)
        {
            throw new AggregateException($"{failures.Count} of {targets.Count} scan target(s) failed",
                failures.Select(f => f.Error));
        }
    }
}