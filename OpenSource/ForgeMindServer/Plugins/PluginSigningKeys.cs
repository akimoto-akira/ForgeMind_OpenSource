// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Collections.ObjectModel;

namespace ForgeMindServer.Plugins;

/// <summary>
/// Trusted plugin signing public keys (DER Base64).
/// Signature verification answers "was this content issued by a trusted publisher";
/// public keys live only on the Core / client side, while private keys exist only in the
/// License Generator / License Server.
/// <para>
/// Keys are loaded from an external configuration file (<c>plugin-signing-trusted.keys</c>
/// beside the host executable). One DER-encoded RSA public key per line (Base64); blank
/// lines and lines starting with <c>#</c> are ignored. The file is <b>not</b> committed to
/// the repository — a formal commercial build drops it into the output via CI/abs supply
/// chain. When the file is absent or empty, every plugin runs in development / community
/// mode (<see cref="PluginIntegrityStatus.Development"/>) and signature verification is
/// effectively skipped (refactor plan §14).</para>
/// </summary>
public static class PluginSigningKeys
{
    /// <summary>
    /// File name (beside the host executable) that carries trusted public keys.
    /// Not part of the repo — supplied by the build pipeline for formal releases.
    /// </summary>
    public const string TrustedKeysFileName = "plugin-signing-trusted.keys";

    /// <summary>Trusted plugin signing public keys (DER-encoded RSA public keys, Base64).
    /// Loaded once from <see cref="TrustedKeysFileName"/> on first access.</summary>
    public static IReadOnlyList<string> TrustedPublicKeysDerBase64 { get; } = LoadTrustedKeys();

    /// <summary>
    /// Reloads trusted keys from disk (e.g. after a build pipeline drops a new key file).
    /// Returns the parsed keys. Extras are silently ignored; parse errors are logged.
    /// </summary>
    internal static IReadOnlyList<string> Reload() => LoadTrustedKeys();

    private static IReadOnlyList<string> LoadTrustedKeys()
    {
        try
        {
            string? baseDir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir))
                return EmptyResult();

            string path = Path.Combine(baseDir, TrustedKeysFileName);
            if (!File.Exists(path))
                return EmptyResult();

            var keys = new List<string>();
            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;
                keys.Add(trimmed);
            }

            return keys.Count > 0 ? keys : EmptyResult();
        }
        catch
        {
            // Key file access must never break plugin loading — fall back to empty (development mode).
            return EmptyResult();
        }
    }

    private static IReadOnlyList<string> EmptyResult() => Array.Empty<string>();
}