// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer.Authorization;

namespace ForgeMindServer.Plugins;

/// <summary>
/// Plugin Manifest loader. The Manifest is stored alongside its DLL, sharing the same base name:
/// <c>ForgeMind.Blueprint.dll</c> ↔ <c>ForgeMind.Blueprint.manifest.json</c>.
/// The Manifest declares module identity, Core compatibility range, capabilities, content hash, and
/// signature — it is the authoritative reference by which Core knows the plugin (see
/// <see cref="ModuleManifest"/>).
/// </summary>
public static class ManifestLoader
{
    /// <summary>Manifest file extension.</summary>
    public const string ManifestExtension = ".manifest.json";

    /// <summary>
    /// Derives the Manifest path from a DLL path and checks for its existence.
    /// Returns null when the DLL has no sibling Manifest (treated as a development / community plugin).
    /// </summary>
    public static string? ResolveManifestPath(string dllPath)
    {
        string? directory = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(directory))
            return null;

        string name = Path.GetFileNameWithoutExtension(dllPath);
        string candidate = Path.Combine(directory, name + ManifestExtension);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Reads and parses the Manifest file. Returns null when the Manifest file is absent
    /// (development/community mode, open trust). When the Manifest file exists but cannot be parsed
    /// (malformed JSON / schema mismatch / empty content), throws <see cref="InvalidDataException"/>
    /// so the loader can distinguish "no manifest" from "corrupt manifest" — corrupting a manifest
    /// must not silently grant development policy.
    /// </summary>
    public static ModuleManifest? LoadManifest(string? manifestPath)
    {
        if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            return null;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        string json = File.ReadAllText(manifestPath);
        ModuleManifest? manifest = JsonSerializer.Deserialize<ModuleManifest>(json, options);
        return manifest ?? throw new InvalidDataException(
            $"Manifest exists but deserialized to null (schema mismatch or empty content): {manifestPath}");
    }
}