// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Engine;

/// <summary>
/// Engine kind. The host uniformly discovers and registers these; plugins consume them read-only.
/// Only Unreal is currently implemented; Unity / Godot are placeholders for future expansion.
/// </summary>
public enum EngineKind
{
    Unreal,
    Unity,
    Godot
}

/// <summary>
/// Describes an engine installation (read-only, computed by the host for plugins).
/// <see cref="Root"/> is the engine installation root (for UE, e.g. <c>D:\UE_5.4</c>);
/// plugins use it to compose UBT / Editor / UAT paths.
/// </summary>
/// <param name="Kind">Engine kind.</param>
/// <param name="Root">Engine installation root directory.</param>
/// <param name="Version">Resolved version number.</param>
/// <param name="IsSourceBuild">True if a source build (associated via registry GUID), false for a launcher binary install.</param>
/// <param name="IsCompatible">Whether the host considers this compatible with the current Core release.</param>
public record EngineDescriptor(
    EngineKind Kind,
    string Root,
    Version Version,
    bool IsSourceBuild,
    bool IsCompatible);

/// <summary>
/// Information about the currently open / target project (read-only, computed by the host for plugins).
/// UE-specific fields (<see cref="HasCompiler"/> / <see cref="HasCompanionPlugin"/>) are ignored by other engines.
/// </summary>
/// <param name="Path">Project directory or .uproject path.</param>
/// <param name="Engine">Engine kind the project targets.</param>
/// <param name="EngineVersion">Engine version string (e.g. "5.4" or a source-build GUID).</param>
/// <param name="HasCompiler">Whether a C++ compiler is available (C++ projects).</param>
/// <param name="HasCompanionPlugin">Whether the ForgeMind companion plugin is installed (Bridge online capability).</param>
public record ProjectInfo(
    string Path,
    EngineKind Engine,
    string EngineVersion,
    bool HasCompiler,
    bool HasCompanionPlugin);

/// <summary>
/// Engine version: one enum value per supported engine version.
/// <para>Purpose: inside a plugin, routes to the engine-specific logic via a version <c>switch</c> — there are
/// no per-engine files, and no per-engine tool/plugin registrations. Tool classes keep engine-agnostic names
/// (project / build / editor / launch / engine / switch_version / knowledge); version-specific behavior is
/// dispatched uniformly inside the class using this enum.</para>
/// <para>Granularity: UE is broken down to individual versions (<c>UE_5_0</c> ~ <c>UE_5_8</c>).
/// Unity and Godot are not yet implemented and are therefore not listed; <b>when they land, they will be
/// added at exactly the same granularity as UE</b>, i.e. broken down to individual versions
/// (e.g. <c>Unity_2021</c> / <c>Unity_2022</c>, <c>Godot_4_0</c> / <c>Godot_4_1</c>), not as a single
/// engine-level placeholder.</para>
/// <para><b>The Universal bucket is not a value of this enum</b>: it is a separate concept — any concrete
/// version can match it — so querying a version always hits the range of [that version's bucket + the
/// universal bucket]. See <see cref="EngineVersions.UniversalBucket"/>.</para>
/// <para>Three steps to add a newly supported version: ① append an enum value here; ② add a mapping in
/// <see cref="EngineVersions.Parse"/>; ③ add the corresponding branch in the plugin's switch
/// (fall through to default when the version has no special logic).</para>
/// </summary>
public enum EngineVersion
{
    UE_5_0,
    UE_5_1,
    UE_5_2,
    UE_5_3,
    UE_5_4,
    UE_5_5,
    UE_5_6,
    UE_5_7,
    UE_5_8
}

/// <summary>
/// Parse and display helpers for <see cref="EngineVersion"/>.
/// <para>Parsing is only responsible for "recognizing" a version; tools always keep the original version
/// string internally, so an unlisted new version (parsed as <see langword="null"/>) does not cause feature
/// loss — the switch simply falls through to the default branch and keeps working using the original
/// bucket name.</para>
/// </summary>
public static class EngineVersions
{
    /// <summary>
    /// Universal bucket name: content that applies to all versions. <b>It is not an enum value</b> —
    /// any concrete version matches it, so querying a version always hits the range of
    /// [that version's bucket + universal bucket].
    /// </summary>
    public const string UniversalBucket = "universal";

    /// <summary>Bucket name in "5.4" form → enum value (strict major.minor match).</summary>
    /// <summary>Enum value name → enum ("UE_5_4", case-insensitive) — used for public parameters.</summary>
    private static readonly Dictionary<string, EngineVersion> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UE_5_0"] = EngineVersion.UE_5_0,
        ["UE_5_1"] = EngineVersion.UE_5_1,
        ["UE_5_2"] = EngineVersion.UE_5_2,
        ["UE_5_3"] = EngineVersion.UE_5_3,
        ["UE_5_4"] = EngineVersion.UE_5_4,
        ["UE_5_5"] = EngineVersion.UE_5_5,
        ["UE_5_6"] = EngineVersion.UE_5_6,
        ["UE_5_7"] = EngineVersion.UE_5_7,
        ["UE_5_8"] = EngineVersion.UE_5_8
    };

    /// <summary>Version number → enum ("5.4") — used for .uproject's EngineAssociation.</summary>
    private static readonly Dictionary<string, EngineVersion> ByNumber = new(StringComparer.Ordinal)
    {
        ["5.0"] = EngineVersion.UE_5_0,
        ["5.1"] = EngineVersion.UE_5_1,
        ["5.2"] = EngineVersion.UE_5_2,
        ["5.3"] = EngineVersion.UE_5_3,
        ["5.4"] = EngineVersion.UE_5_4,
        ["5.5"] = EngineVersion.UE_5_5,
        ["5.6"] = EngineVersion.UE_5_6,
        ["5.7"] = EngineVersion.UE_5_7,
        ["5.8"] = EngineVersion.UE_5_8
    };

    /// <summary>
    /// Parses a version in <b>enum value name</b> form ("UE_5_4").
    /// <para>Public parameters (e.g. the <c>engineVersion</c> argument of the knowledge tool) <b>only accept
    /// this form</b>; version-number forms like "5.4" are considered invalid and return
    /// <see langword="null"/>. "universal" is the universal bucket, not a concrete version, also returns
    /// null.</para>
    /// </summary>
    public static EngineVersion? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string text = raw.Trim();

        // The universal bucket is not a concrete version
        if (string.Equals(text, UniversalBucket, StringComparison.OrdinalIgnoreCase))
            return null;

        return ByName.TryGetValue(text, out EngineVersion version) ? version : null;
    }

    /// <summary>
    /// Parses a version from .uproject's EngineAssociation: "5.4" / "5.4.1" → <c>UE_5_4</c>;
    /// direct enum value names ("UE_5_4") are also accepted.
    /// <para>GUID-form associations (source builds) and as-yet-unlisted versions return
    /// <see langword="null"/>.</para>
    /// </summary>
    public static EngineVersion? FromAssociation(string? association)
    {
        if (string.IsNullOrWhiteSpace(association))
            return null;

        string text = association.Trim();

        if (string.Equals(text, UniversalBucket, StringComparison.OrdinalIgnoreCase))
            return null;

        // GUID-form EngineAssociation (source build) has no version number
        if (Guid.TryParse(text, out _))
            return null;

        // Already written as an enum value name
        if (ByName.TryGetValue(text, out EngineVersion byName))
            return byName;

        // "major.minor(.patch)" → normalize to "major.minor"
        string[] parts = text.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
        {
            string bucket = parts[0] + "." + parts[1];
            return ByNumber.TryGetValue(bucket, out EngineVersion byNumber) ? byNumber : null;
        }

        return null;
    }

    /// <summary>
    /// Enum value → version bucket tag. <b>The bucket tag is the enum value name itself</b> (e.g. "UE_5_4");
    /// knowledge base version directories and index entries use it as their identifier.
    /// </summary>
    public static string ToBucket(EngineVersion version) => version.ToString();

    /// <summary>
    /// Validates a bucket name / entry id: only letters, digits, underscore, hyphen, and dot are allowed
    /// (prevents path traversal). Moved up from the former UeVersion.IsValidId as the version bucket structure
    /// was simplified.
    /// </summary>
    public static bool IsValidBucketName(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (char c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
                return false;
        }

        return value != "." && value != "..";
    }
}
