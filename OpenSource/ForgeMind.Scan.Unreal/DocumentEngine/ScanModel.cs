// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// Scan result data organization (closest to UE's shape):
/// <para>Root → Plugin / Module → Class → Member → Parameter.</para>
/// Unique ownership: each module either belongs directly to the root or to exactly one plugin, no double counting;
/// plugin modules are excluded from the root's direct module set.
/// </summary>
public sealed class ScanRoot
{
    /// <summary>Root path (engine install path or project path) — one root path is one scan scope.</summary>
    public required string RootPath { get; init; }

    /// <summary>Scope level (engine side / project side; asset source see <see cref="ScanSource"/>).</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>Engine version / description — data attribute only, never used as identity.</summary>
    public string Version { get; init; } = "";

    /// <summary>Plugin set (engine side: Engine/Plugins; project side: Project/Plugins).</summary>
    public List<PluginInfo> Plugins { get; } = [];

    /// <summary>Root-direct module set (<b>excluding</b> any modules inside plugins).</summary>
    public List<ModuleInfo> Modules { get; } = [];

    /// <summary>Asset set (uasset scan source, tagged "asset"; engine side = EngineAsset, project side = ProjectAsset).</summary>
    public List<UeAssetEntry> Assets { get; } = [];
}

/// <summary>
/// uasset asset entry — facts from asset deserialization (tagged "asset", distinct from C++ classes scanned from headers).
/// General consumer: includes DSL (all domains) but is not DSL-only; not bound to any single domain.
/// </summary>
public sealed class UeAssetEntry
{
    /// <summary>Full path of the asset file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Asset file name (without extension).</summary>
    public required string Name { get; init; }

    /// <summary>Scope: engine asset (EngineAsset) or project asset (ProjectAsset).</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>UE4 / UE5 file version of the package summary (data attribute only).</summary>
    public int FileVersionUe4 { get; init; }

    public int FileVersionUe5 { get; init; }

    /// <summary>Number of export objects.</summary>
    public int ExportCount { get; init; }

    /// <summary>Exports (object name + class name; class name as in import).</summary>
    public List<UeAssetExport> Exports { get; } = [];

    /// <summary>Files that fail to parse yield not entries; this counts failures (for diagnostics).</summary>
    public int FailedExports { get; init; }
}

/// <summary>One export object inside a uasset.</summary>
public sealed class UeAssetExport
{
    public required string ObjectName { get; init; }

    /// <summary>Class name (when export class is an import, resolves to the referenced class name).</summary>
    public string? ClassName { get; init; }
}

/// <summary>Kind of object a search hit resolves to (lookup and search share the same index; this is the result side of search).</summary>
public enum ScanSearchHitKind
{
    Class,
    Function,
    Property,
    Asset,
}

/// <summary>Search hit entry.</summary>
public sealed class ScanSearchHit
{
    /// <summary>Kind of hit object.</summary>
    public required ScanSearchHitKind Kind { get; init; }

    /// <summary>Hit name (class / function / property / asset name).</summary>
    public required string Name { get; init; }

    /// <summary>Owning class name (populated for function / property hits; empty otherwise).</summary>
    public string? Owner { get; init; }

    /// <summary>Module name (empty for asset hits).</summary>
    public string? Module { get; init; }

    /// <summary>Location (class / member = file:line; asset = file path).</summary>
    public string Location { get; init; } = "";

    /// <summary>Scope level (six-tier label).</summary>
    public required ScopeLevel Scope { get; init; }
}

/// <summary>
/// Plugin (first-class citizen with .uplugin) — another container layer beneath engine / project,
/// one plugin contains multiple modules.
/// </summary>
public sealed class PluginInfo
{
    /// <summary>Plugin name (.uplugin file name without extension).</summary>
    public required string Name { get; init; }

    /// <summary>Plugin directory.</summary>
    public required string Directory { get; init; }

    /// <summary>.uplugin descriptor file path (may be missing — scan is not blocked when directory exists but descriptor is missing).</summary>
    public string? DescriptorFile { get; init; }

    /// <summary>Scope: engine plugin (EnginePlugin) or project plugin (ProjectPlugin).</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>Modules this plugin contains.</summary>
    public List<ModuleInfo> Modules { get; } = [];
}

/// <summary>Module (UBT module rules + header set) — both an organizational unit and the physical unit of scanning and caching.</summary>
public sealed class ModuleInfo
{
    /// <summary>Module name (.Build.cs file name without extension).</summary>
    public required string Name { get; init; }

    /// <summary>Module directory (directory containing .Build.cs).</summary>
    public required string Directory { get; init; }

    /// <summary>.Build.cs rules file path.</summary>
    public string? RulesFile { get; init; }

    /// <summary>Scope level (inherited from container).</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>Classes scanned from this module (middle layer).</summary>
    public List<UeClassInfo> Classes { get; } = [];
}

/// <summary>
/// "Class" — atomic unit of the middle layer: collective term for UCLASS / USTRUCT / UENUM / UINTERFACE / UDELEGATE.
/// Stores only scan facts (own declared members + base class names); inherited members are aggregated by the query service before returning.
/// </summary>
public sealed class UeClassInfo
{
    /// <summary>Type name (with U / A / F / I / E prefix, e.g. AActor).</summary>
    public required string Name { get; init; }

    /// <summary>Kind (class / struct / enum / interface / delegate).</summary>
    public required UeTypeKind Kind { get; init; }

    /// <summary>Source file path of the declaration.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>Line number of the declaration in the source file.</summary>
    public int LineNumber { get; init; }

    /// <summary>Base class name list (records whatever is found; includes interface implementations).</summary>
    public List<string> BaseClasses { get; } = [];

    /// <summary>Specifier list inside the macro parens.</summary>
    public List<string> Specifiers { get; } = [];

    /// <summary>Metadata key-value pairs inside the macro parens.</summary>
    public Dictionary<string, string> Metadata { get; } = [];

    /// <summary>Own declared UFUNCTION members.</summary>
    public List<UeFunctionInfo> Functions { get; } = [];

    /// <summary>Own declared UPROPERTY members.</summary>
    public List<UePropertyInfo> Properties { get; } = [];

    /// <summary>Scope level (inherited from containing module / asset source).</summary>
    public required ScopeLevel Scope { get; set; }
}

/// <summary>UFUNCTION member — parameter name is the Blueprint pin name.</summary>
public sealed class UeFunctionInfo
{
    public required string Name { get; init; }

    /// <summary>Whether static function (static functions imply a leading WorldContextObject parameter).</summary>
    public bool IsStatic { get; init; }

    /// <summary>Return type (e.g. "FString" / "void"; null when scan not covered — acts as wildcard during type matching).</summary>
    public string? ReturnType { get; init; }

    public List<string> Specifiers { get; } = [];

    public Dictionary<string, string> Metadata { get; } = [];

    public string FilePath { get; init; } = "";

    public int LineNumber { get; init; }

    /// <summary>Parameter list (order is pin order).</summary>
    public List<UeParameterInfo> Parameters { get; } = [];
}

/// <summary>UPROPERTY member.</summary>
public sealed class UePropertyInfo
{
    public required string Name { get; init; }

    /// <summary>Declared type (may be empty — not required when scan not covered).</summary>
    public string? Type { get; init; }

    public List<string> Specifiers { get; } = [];

    public Dictionary<string, string> Metadata { get; } = [];

    public string FilePath { get; init; } = "";

    public int LineNumber { get; init; }
}

/// <summary>UFUNCTION parameter — name / type / default value, directly maps to a Blueprint pin.</summary>
public sealed class UeParameterInfo
{
    /// <summary>Parameter name (i.e. pin name).</summary>
    public required string Name { get; init; }

    /// <summary>Parameter type (may be empty — not required when scan not covered).</summary>
    public string? Type { get; init; }

    /// <summary>Default value in the C++ declaration (may be empty — no default is not the same as default = null).</summary>
    public string? DefaultValue { get; init; }
}
