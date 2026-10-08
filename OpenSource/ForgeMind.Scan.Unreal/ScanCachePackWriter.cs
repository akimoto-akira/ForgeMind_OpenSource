// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Speedy;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// Writes the raw algorithm-produced <see cref="ScanRoot"/> tree into an SPK, with the same layout as the historical ScanCache directory:
/// <code>
/// {version}/engine.json     — engine-side code scan result
/// {version}/assets.json     — engine-side asset scan result
/// </code>
/// <para>
/// No manifest, no index/, no data/ — anything absent from ScanCache is not invented here.
/// One file pair per version; versions do not interfere with each other.</para>
/// </summary>
public sealed class ScanCachePackWriter
{
    private readonly IPackWriter _writer;

    public ScanCachePackWriter(IPackWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <summary>
    /// Writes one version's scan results. Same-named entries are overwritten as a whole (rescanning the same version refreshes it).
    /// </summary>
    public void Write(string version, IEnumerable<ScanRoot> codeRoots, IEnumerable<ScanRoot> assetRoots)
    {
        string prefix = SanitizeVersion(version);

        _writer.Write($"{prefix}/engine.json", ScanCachePersistence.SerializeEngineRoots(codeRoots));
        _writer.Write($"{prefix}/assets.json", ScanCachePersistence.SerializeAssetRoots(assetRoots));
    }

    /// <summary>
    /// Version string used as a path segment: replaces path separators and wildcards with underscores to avoid writing illegal entry paths.
    /// </summary>
    private static string SanitizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "_";

        var sb = new System.Text.StringBuilder(version.Length);
        foreach (char c in version)
            sb.Append(c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        return sb.ToString();
    }
}
