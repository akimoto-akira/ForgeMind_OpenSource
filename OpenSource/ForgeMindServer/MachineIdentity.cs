// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace ForgeMindServer;

/// <summary>
/// Local machine identity (the host-side successor of the former MachineCode).
///
/// The authoritative machine-code implementation for the authorization system
/// (ForgeMind.Authorization.Commercial.License.MachineCode) was migrated to the commercial auth plugin
/// together with the fml decode / signature-verification logic; the core library no longer carries
/// license signing / verification logic. This implementation serves only core-library infrastructure:
/// Bridge AES-128 key derivation (same key on the same machine) and populating
/// AuthorizationContext.MachineCode. The algorithm is kept fully identical to the commercial plugin so
/// that the generator side and the verification side produce the same machine code.
/// </summary>
internal static class MachineIdentity
{
    /// <summary>
/// Generates the local machine code (lowercase hex string of a SHA-256 hash).
/// On non-Windows platforms falls back to a hash of hostname + OS info.
    /// </summary>
    public static string Generate()
    {
        string raw = GetHardwareIdentifier();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
/// Collects identifiers from the local hardware.
/// Windows: CPU ProcessorId + motherboard SerialNumber (via WMI).
/// Non-Windows: machine name + OS platform identifier.
    /// </summary>
    private static string GetHardwareIdentifier()
    {
        var sb = new StringBuilder();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var cpuSearcher = new ManagementObjectSearcher(
                    "SELECT ProcessorId FROM Win32_Processor");
                foreach (var obj in cpuSearcher.Get())
                {
                    sb.Append(obj["ProcessorId"]?.ToString() ?? "");
                }

                using var boardSearcher = new ManagementObjectSearcher(
                    "SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (var obj in boardSearcher.Get())
                {
                    sb.Append(obj["SerialNumber"]?.ToString() ?? "");
                }
            }
            catch
            {
                // Fall back to machine name when WMI is unavailable
                sb.Append(Environment.MachineName);
            }
        }
        else
        {
            // Non-Windows fallback
            sb.Append(Environment.MachineName);
            sb.Append(Environment.OSVersion);
        }

        return sb.ToString();
    }
}
