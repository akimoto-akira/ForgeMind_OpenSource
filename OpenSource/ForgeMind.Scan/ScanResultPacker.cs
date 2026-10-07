// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.Scan;

/// <summary>
/// Serializes one or more <see cref="IScanResult"/>s into an SPK pack via <see cref="IPackWriter"/>
/// (refactor plan §15/§18 fix). This is the direct "Scanner → SPK" bridge — no intermediate
/// ScanCache, no loose files, no converter chain.
/// <para>
/// Layout inside the pack (version-isolated to prevent cross-version shadowing):
/// <code>
/// manifest.json                   — ScanManifest (scanner/target/schema metadata)
/// index/{version}/entries.json    — ScanEntry[] per version
/// data/{version}/roots.json      — ScanRootView[]
/// data/{version}/classes.json    — ScanClassView[]
/// data/{version}/functions.json  — Dictionary&lt;string, ScanFunctionView[]&gt;
/// </code>
/// The layout is stable and version-stamped in <see cref="ScanManifest.FormatVersion"/>;
/// the runtime read-side (§16) mirrors it through <see cref="IPackReader"/>. Multiple version
/// data is written under distinct <c>data/{version}/</c> prefixes so later targets never
/// shadow earlier ones (refactor plan §18).</para>
/// </summary>
public sealed class ScanResultPacker
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IPackWriter _writer;

    /// <summary>Creates a packer that writes to the given SPK writer.</summary>
    public ScanResultPacker(IPackWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <summary>
    /// Packs a single-version scan result into the SPK writer. Manifest is overwritten;
    /// multi-target packs should use <see cref="PackMany"/> to accumulate TargetVersions.
    /// View data is written under <c>data/{version}/</c> so multiple versions never shadow
    /// each other (refactor plan §18 fix).
    /// </summary>
    public void Pack(IScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _writer.Write("manifest.json", JsonSerializer.SerializeToUtf8Bytes(result.Manifest, _jsonOptions));

        // index/{version}/entries.json — per-version entry batches (version-isolated)
        foreach (string version in result.Index.Versions)
        {
            IReadOnlyList<ScanEntry> entries = result.Index.EntriesOf(version);
            string path = $"index/{SanitizeVersion(version)}/entries.json";
            _writer.Write(path, JsonSerializer.SerializeToUtf8Bytes(entries, _jsonOptions));
        }

        // data/{version}/* — version-isolated view data (refactor plan §18 fix)
        string versionScope = result.Index.Versions.FirstOrDefault()
            ?? SanitizeVersion(result.Manifest.TargetVersions.FirstOrDefault() ?? "unknown");
        string dataPrefix = $"data/{SanitizeVersion(versionScope)}/";

        _writer.Write(dataPrefix + "roots.json",
            JsonSerializer.SerializeToUtf8Bytes(result.Data.Roots, _jsonOptions));

        _writer.Write(dataPrefix + "classes.json",
            JsonSerializer.SerializeToUtf8Bytes(result.Data.Classes, _jsonOptions));

        var funcMap = result.Data.FunctionsByClass.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList());
        _writer.Write(dataPrefix + "functions.json",
            JsonSerializer.SerializeToUtf8Bytes(funcMap, _jsonOptions));

        _writer.FlushAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Packs multiple version-scoped results into the same writer. The manifest is merged
    /// across all results (TargetVersions and DataTypes are unioned), and each version's
    /// view data is written under its own <c>data/{version}/</c> prefix so later scans never
    /// shadow earlier ones (refactor plan §18 fix).
    /// </summary>
    public void PackMany(IReadOnlyList<IScanResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        // Pass 1: merge manifests across all results
        var mergedTargetVersions = new SortedSet<string>(StringComparer.Ordinal);
        var mergedDataTypes = new SortedSet<string>(StringComparer.Ordinal);
        ScanManifest? first = null;

        foreach (IScanResult result in results)
        {
            first ??= result.Manifest;
            foreach (string v in result.Manifest.TargetVersions)
                mergedTargetVersions.Add(v);
            foreach (string t in result.Manifest.DataTypes)
                mergedDataTypes.Add(t);
        }

        if (first != null)
        {
            var mergedManifest = new ScanManifest
            {
                FormatVersion = first.FormatVersion,
                SchemaVersion = first.SchemaVersion,
                ScannerId = first.ScannerId,
                ScannerVersion = first.ScannerVersion,
                TargetType = first.TargetType,
                TargetVersions = mergedTargetVersions.ToArray(),
                DataTypes = mergedDataTypes.ToArray(),
                CreatedAt = first.CreatedAt,
            };
            _writer.Write("manifest.json", JsonSerializer.SerializeToUtf8Bytes(mergedManifest, _jsonOptions));
        }

        // Pass 2: write each version's data under its isolated path
        foreach (IScanResult result in results)
        {
            foreach (string version in result.Index.Versions)
            {
                IReadOnlyList<ScanEntry> entries = result.Index.EntriesOf(version);
                string path = $"index/{SanitizeVersion(version)}/entries.json";
                _writer.Write(path, JsonSerializer.SerializeToUtf8Bytes(entries, _jsonOptions));
            }

            string versionScope = result.Index.Versions.FirstOrDefault()
                ?? SanitizeVersion(result.Manifest.TargetVersions.FirstOrDefault() ?? "unknown");
            string dataPrefix = $"data/{SanitizeVersion(versionScope)}/";

            _writer.Write(dataPrefix + "roots.json",
                JsonSerializer.SerializeToUtf8Bytes(result.Data.Roots, _jsonOptions));
            _writer.Write(dataPrefix + "classes.json",
                JsonSerializer.SerializeToUtf8Bytes(result.Data.Classes, _jsonOptions));

            var funcMap = result.Data.FunctionsByClass.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList());
            _writer.Write(dataPrefix + "functions.json",
                JsonSerializer.SerializeToUtf8Bytes(funcMap, _jsonOptions));
        }

        _writer.FlushAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sanitizes a version string for use as a path segment. Replaces characters that are
    /// invalid in SPK paths (path separators, wildcards) with underscores.
    /// </summary>
    private static string SanitizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            return "_";

        var sb = new System.Text.StringBuilder(version.Length);
        foreach (char c in version)
        {
            sb.Append(c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        }
        return sb.ToString();
    }
}
