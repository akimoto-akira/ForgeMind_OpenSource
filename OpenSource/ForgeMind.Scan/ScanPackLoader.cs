// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.Scan;

/// <summary>
/// Convenience loaders for <see cref="SpkScanDataProvider"/> (refactor plan §16/§17).
/// <para>
/// Three backing stores, one contract:
/// <code>
/// File on disk          → FilePackReader   → ScanPackReader → SpkScanDataProvider
/// Any seekable Stream   → StreamPackReader → ScanPackReader → SpkScanDataProvider
/// Embedded Resource     → ManifestResourceStream → StreamPackReader → …
/// </code>
/// The runtime picks the store that fits the deployment: external file for development,
/// embedded resource for shipped packages. The SPK format is identical in both cases.</para>
/// </summary>
public static class ScanPackLoader
{
    /// <summary>
    /// Loads scan data from an .spk file on disk. The file stays open (read-shared) until
    /// the returned provider is disposed.
    /// </summary>
    public static SpkScanDataProvider FromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var fileReader = new FilePackReader(filePath);
        var scanReader = new ScanPackReader(fileReader);
        return new SpkScanDataProvider(scanReader);
    }

    /// <summary>
    /// Loads scan data from an .spk file on disk, scoped to a single version namespace
    /// (e.g. "5.0"). Only that version's data is loaded — same-named classes from different
    /// versions never collide (refactor plan §8).
    /// </summary>
    public static SpkScanDataProvider FromFileVersionScoped(string filePath, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var fileReader = new FilePackReader(filePath);
        var scanReader = new ScanPackReader(fileReader);
        return SpkScanDataProvider.CreateVersionScope(scanReader, version);
    }

    /// <summary>
    /// Loads scan data from any seekable, readable <see cref="Stream"/> (e.g.
    /// <see cref="MemoryStream"/>, <c>ManifestResourceStream</c>). The stream is owned by
    /// the reader chain and disposed when the provider is disposed.
    /// </summary>
    public static SpkScanDataProvider FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var streamReader = new StreamPackReader(stream);
        var scanReader = new ScanPackReader(streamReader);
        return new SpkScanDataProvider(scanReader);
    }

    /// <summary>
    /// Loads scan data from a seekable stream, scoped to a single version namespace
    /// (e.g. "5.0"). Only that version's data is loaded — same-named classes from different
    /// versions never collide (refactor plan §8).
    /// </summary>
    public static SpkScanDataProvider FromStreamVersionScoped(Stream stream, string version)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var streamReader = new StreamPackReader(stream);
        var scanReader = new ScanPackReader(streamReader);
        return SpkScanDataProvider.CreateVersionScope(scanReader, version);
    }

    /// <summary>
    /// Lists all version namespaces available in an .spk file on disk, so the caller can pick
    /// one and call <see cref="FromFileVersionScoped"/>. Returns an empty list when no
    /// version-scoped data exists.
    /// </summary>
    public static IReadOnlyList<string> ListVersions(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        using var fileReader = new FilePackReader(filePath);
        var scanReader = new ScanPackReader(fileReader);
        return scanReader.Versions;
    }

    /// <summary>
    /// Loads scan data from an embedded resource (refactor plan §16).
    /// <para>
    /// The resource must be a valid .spk pack embedded at build time
    /// (<c>&lt;EmbeddedResource&gt;</c> in csproj). The <c>ManifestResourceStream</c>
    /// is seekable and readable, so <see cref="StreamPackReader"/> handles it directly.</para>
    /// <para>
    /// Resource name resolution uses <see cref="Assembly.GetManifestResourceStream(string)"/>;
    /// for default-namespace resources pass the full name (e.g.
    /// "ForgeMind.Scan.Unreal.engine.spk").</para>
    /// </summary>
    /// <param name="assembly">Assembly that contains the embedded resource.</param>
    /// <param name="resourceName">Full resource name (including default namespace prefix).</param>
    /// <returns>A provider backed by the embedded SPK; dispose to release the stream.</returns>
    public static SpkScanDataProvider FromEmbeddedResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(resourceName);

        Stream? stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException(
                $"Embedded resource '{resourceName}' not found in assembly '{assembly.GetName().Name}'.",
                nameof(resourceName));

        return FromStream(stream);
    }

    /// <summary>
    /// Attempts to load scan data from an embedded resource; returns null when the resource
    /// is absent (lenient — the caller may fall back to another source).
    /// </summary>
    public static SpkScanDataProvider? TryFromEmbeddedResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(resourceName);

        Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        return FromStream(stream);
    }
}