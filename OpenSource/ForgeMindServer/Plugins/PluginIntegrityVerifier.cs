// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Security.Cryptography;
using System.Text;
using ForgeMindServer.Authorization;

namespace ForgeMindServer.Plugins;

/// <summary>Plugin integrity verification status.</summary>
public enum PluginIntegrityStatus
{
    /// <summary>Signed and signature-verified with a matching PayloadHash — an official, trusted distribution.</summary>
    Trusted,

    /// <summary>Development / community mode: no Manifest or no signature. Open-trusted; logged but not blocked.</summary>
    Development,

    /// <summary>PayloadHash does not match the DLL's actual content — an official distribution was altered.</summary>
    IntegrityMismatch,

    /// <summary>Signature could not be verified by any trusted public key — untrusted publisher.</summary>
    SignatureInvalid
}

/// <summary>
/// Plugin integrity verifier (Layer 2 of the three-tier verification model — Integrity).
///
/// Integrity chain: module content → SHA-256 (PayloadHash) → signature (Signature).
/// PayloadHash answers "did the file content change"; the signature answers "was this content
/// issued by a trusted publisher".
/// This layer does not prevent decompilation; it detects whether an official distribution was
/// tampered with. A failing verification (tampered / forged signature) causes the loader to refuse
/// to load the plugin.
/// </summary>
public static class PluginIntegrityVerifier
{
    /// <summary>Computes the SHA-256 of the module content (lowercase hex).</summary>
    public static string ComputeSha256(byte[] payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    /// <summary>
    /// Builds the canonical signing payload that the signature must cover.
    /// <para>
    /// The canonical form binds ModuleId + PublisherId + Version + Capabilities + Compatibility
    /// (MinCoreVersion / MaxCoreVersion) + PayloadHash into a single deterministic byte sequence.
    /// This prevents an attacker from modifying any of these fields (e.g. forging a ModuleId to
    /// match the SecurityOverrideAllowlist) without invalidating the signature
    /// (refactor plan §8: Manifest + PayloadHash + Signature integrity).</para>
    /// <para>
    /// Format (line-based, LF-terminated, fields in fixed order — deterministic across platforms):
    /// <code>
    /// v=2
    /// mid=ModuleId
    /// pid=PublisherId
    /// ver=Version
    /// cap=Capability1,Capability2,...   (sorted, ascending)
   /// min=MinCoreVersion
    /// max=MaxCoreVersion
    /// hash=PayloadHash
    /// </code></para>
    /// </summary>
    public static byte[] BuildSignPayload(ModuleManifest manifest, string payloadHash)
    {
        var sb = new StringBuilder(256);
        sb.AppendLine("v=2");
        sb.Append("mid=").AppendLine(manifest.ModuleId ?? "");
        sb.Append("pid=").AppendLine(manifest.PublisherId ?? "");
        sb.Append("ver=").AppendLine(manifest.Version ?? "");

        // Capabilities sorted for deterministic order
        var sortedCaps = manifest.Capabilities?
            .Where(c => c != null)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        sb.Append("cap=").AppendLine(string.Join(",", sortedCaps));

        sb.Append("min=").AppendLine(manifest.MinCoreVersion ?? "");
        sb.Append("max=").AppendLine(manifest.MaxCoreVersion ?? "");
        sb.Append("hash=").Append(payloadHash ?? "");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Verifies plugin integrity. Returns <see cref="PluginIntegrityStatus.Development"/> when there is no Manifest.
    /// </summary>
    /// <param name="manifest">The plugin Manifest (may be null).</param>
    /// <param name="pluginBytes">The raw bytes of the plugin DLL.</param>
    public static PluginIntegrityStatus Verify(ModuleManifest? manifest, byte[] pluginBytes)
    {
        if (manifest == null)
            return PluginIntegrityStatus.Development;

        string computed = ComputeSha256(pluginBytes);

        // Layer 2a: content hash must match (when PayloadHash was declared)
        if (!string.IsNullOrEmpty(manifest.PayloadHash) &&
            !string.Equals(computed, manifest.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return PluginIntegrityStatus.IntegrityMismatch;
        }

        // No signature: development / community mode (open-trusted)
        if (string.IsNullOrEmpty(manifest.Signature))
            return PluginIntegrityStatus.Development;

        // Layer 2b: signature must be verified by a trusted public key.
        // The signature covers the canonical payload (manifest identity fields + PayloadHash),
        // not just the hash alone — this binds ModuleId / PublisherId / Version / Capabilities /
        // Compatibility to the signature so they cannot be tampered with independently.
        byte[] signData = BuildSignPayload(manifest, computed);

        foreach (string derBase64 in PluginSigningKeys.TrustedPublicKeysDerBase64)
        {
            try
            {
                using RSA rsa = RSA.Create();
                rsa.ImportRSAPublicKey(Convert.FromBase64String(derBase64), out _);

                byte[] signature;
                try
                {
                    signature = Convert.FromBase64String(manifest.Signature);
                }
                catch (FormatException)
                {
                    continue;
                }

                if (rsa.VerifyData(signData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return PluginIntegrityStatus.Trusted;
            }
            catch
            {
                // Try the next trusted public key
            }
        }

        return PluginIntegrityStatus.SignatureInvalid;
    }
}