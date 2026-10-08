// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// Scan scope label: higher number = more fundamental.
/// Parent lookup rule — starting from its own label, only look up scopes with <b>same or higher label</b>;
/// dependency direction always flows from lower label to higher (e.g. a project-plugin class can never inherit from a project-direct-module class).
/// </summary>
public enum ScopeLevel
{
    /// <summary>Project asset (uasset scan source, tagged "asset").</summary>
    ProjectAsset = -2,

    /// <summary>Engine asset (uasset scan source, tagged "asset").</summary>
    EngineAsset = -1,

    /// <summary>Project-direct module.</summary>
    Project = 1,

    /// <summary>Module inside a project plugin.</summary>
    ProjectPlugin = 2,

    /// <summary>Module inside an engine plugin.</summary>
    EnginePlugin = 3,

    /// <summary>Engine-direct module (Engine/Source and Platforms/*/Source).</summary>
    Engine = 4,
}

/// <summary>
/// Kind of "Class" — collective term for UE reflection types (middle layer);
/// one UCLASS / USTRUCT / UENUM / UINTERFACE / UDELEGATE each counts as one "Class".
/// </summary>
public enum UeTypeKind
{
    Class,
    Struct,
    Enum,
    Interface,
    Delegate,
}
