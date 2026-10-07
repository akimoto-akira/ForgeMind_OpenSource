// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// Mirrors <c>FPackageIndex</c> (ObjectResource.h): a single int32 encoding an
/// index into the import map (negative, -Index-1) or the export map (positive,
/// Index-1). Zero always means "null".
/// </summary>
public readonly struct FPackageIndex : IEquatable<FPackageIndex>
{
    /// <summary>Raw int32 as stored in the package.</summary>
    public int Raw { get; }

    /// <summary>Raw int32 as stored in the package (positive: export, negative: import, 0: null).</summary>
    public FPackageIndex(int raw) => Raw = raw;

    public static FPackageIndex Null => new(0);

    /// <summary>Package index pointing at import map slot <paramref name="importIndex"/>.</summary>
    public static FPackageIndex FromImport(int importIndex) => new(-(importIndex + 1));

    /// <summary>Package index pointing at export map slot <paramref name="exportIndex"/>.</summary>
    public static FPackageIndex FromExport(int exportIndex) => new(exportIndex + 1);

    public bool IsNull => Raw == 0;

    /// <summary>True when this index references an import (negative raw value).</summary>
    public bool IsImport => Raw < 0;

    /// <summary>True when this index references an export (positive raw value).</summary>
    public bool IsExport => Raw > 0;

    /// <summary>Import map slot index (valid only when <see cref="IsImport"/>).</summary>
    public int ToImportIndex() => -Raw - 1;

    /// <summary>Export map slot index (valid only when <see cref="IsExport"/>).</summary>
    public int ToExportIndex() => Raw - 1;

    public bool Equals(FPackageIndex other) => Raw == other.Raw;

    public override bool Equals(object? obj) => obj is FPackageIndex other && Equals(other);

    public override int GetHashCode() => Raw;

    public override string ToString() => Raw switch
    {
        0 => "null",
        < 0 => $"import[{ToImportIndex()}]",
        _ => $"export[{ToExportIndex()}]",
    };
}

/// <summary>
/// One <c>FCustomVersion</c> entry of the package custom version container.
/// Serialized in "Optimized" format (int32 count, then per entry a GUID and an
/// int32 version) since UE 4.22.
/// </summary>
public sealed class CustomVersionEntry
{
    /// <summary>
    /// Custom version GUID (Optimized format: legacy -6..-8; Guids format:
    /// legacy -3..-5). Null for the Enums format (legacy -2), where only the
    /// legacy int32 tag is available.
    /// </summary>
    public UAssetGuid? Key { get; set; }

    /// <summary>Legacy int32 tag of the Enums format (legacy -2).</summary>
    public int? LegacyTag { get; set; }

    public int Version { get; set; }

    public CustomVersionEntry(UAssetGuid key, int version)
    {
        Key = key;
        Version = version;
    }

    public CustomVersionEntry(int legacyTag, int version)
    {
        LegacyTag = legacyTag;
        Version = version;
    }
}

/// <summary>
/// One <c>FGenerationInfo</c>: exported object / name counts of a save
/// generation, serialized as two int32.
/// </summary>
public sealed class GenerationInfo
{
    public int ExportCount { get; set; }

    public int NameCount { get; set; }
}

/// <summary>
/// Maps <c>FTextSourceSiteContext</c> entries stored by the gatherable text
/// data map (Internationalization, editor only).
/// </summary>
public sealed class TextSourceSiteContext
{
    public string KeyName { get; set; } = string.Empty;

    public string SiteDescription { get; set; } = string.Empty;

    public bool IsEditorOnly { get; set; }

    public bool IsOptional { get; set; }

    public List<LocMetadataEntry> InfoMetaData { get; } = new();

    public List<LocMetadataEntry> KeyMetaData { get; } = new();
}

/// <summary>
/// Mirrors <c>FGatherableTextData</c> in binary form: NamespaceName,
/// FTextSourceData (SourceString + meta data), then the source site contexts.
/// </summary>
public sealed class GatherableTextData
{
    public string NamespaceName { get; set; } = string.Empty;

    public string SourceString { get; set; } = string.Empty;

    public List<LocMetadataEntry> SourceStringMetaData { get; } = new();

    public List<TextSourceSiteContext> Contexts { get; } = new();
}

/// <summary>
/// One key/value pair of <c>FLocMetadataObject</c>, mirroring the binary
/// shape written by <c>operator&lt;&lt;(FStructuredArchive::FSlot, FLocMetadataObject&amp;)</c>:
/// FString Key followed by a <see cref="LocMetadataValue"/>.
/// </summary>
public sealed class LocMetadataEntry
{
    public string Key { get; set; } = string.Empty;

    public LocMetadataValue Value { get; set; } = new();
}

/// <summary>
/// Mirrors <c>FLocMetadataValue</c> as stored on disk: an ELocMetadataType
/// (int32: 1=Boolean, 2=String, 3=Array, 4=Object) followed by the payload.
/// </summary>
public sealed class LocMetadataValue
{
    public int Type { get; set; }

    public bool BoolValue { get; set; }

    public string StringValue { get; set; } = string.Empty;

    public List<LocMetadataValue> ArrayElements { get; } = new();

    public List<LocMetadataEntry> ObjectEntries { get; } = new();
}

/// <summary>
/// One entry of the import map, mirroring <c>FObjectImport</c>. Name fields
/// are resolved to literals by the owning <see cref="UAssetPackage"/>.
/// </summary>
public sealed class ObjectImport
{
    /// <summary>Name of the package containing the class (FName).</summary>
    public string ClassPackage { get; set; } = string.Empty;

    /// <summary>Class name (FName).</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Outer object reference (FPackageIndex).</summary>
    public FPackageIndex OuterIndex { get; set; }

    /// <summary>Object name (FName).</summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Package name (FName, editor builds). Present only for packages newer
    /// than VER_UE4_NON_OUTER_PACKAGE_IMPORT.
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>UE 5 OPTIONAL_RESOURCES addition; serialized as a 32-bit bool.</summary>
    public bool bImportOptional { get; set; }
}

/// <summary>
/// The raw tagged-property payload of an exported object, i.e. the object data
/// section between SerialOffset and SerialOffset + SerialSize.
/// </summary>
public sealed class ObjectExportData
{
    /// <summary>Tagged properties of the exported object, terminated by a None-name tag.</summary>
    public List<UPropertyTag> Properties { get; } = new();

    /// <summary>
    /// Verbatim payload bytes when the object data could not be parsed (e.g.
    /// script exports); written back unchanged during a later save.
    /// </summary>
    public byte[]? RawData { get; set; }
}

/// <summary>
/// One entry of the export map, mirroring <c>FObjectExport</c>.
/// </summary>
public sealed class ObjectExport
{
    public FPackageIndex ClassIndex { get; set; }

    public FPackageIndex SuperIndex { get; set; }

    public FPackageIndex TemplateIndex { get; set; }

    public FPackageIndex OuterIndex { get; set; }

    public string ObjectName { get; set; } = string.Empty;

    public int ObjectFlags { get; set; }

    /// <summary>Size of the serialized object data (int64 since UE 4.25).</summary>
    public long SerialSize { get; set; }

    /// <summary>Absolute offset of the serialized object data in the file.</summary>
    public long SerialOffset { get; set; }

    public bool bForcedExport { get; set; }

    public bool bNotForClient { get; set; }

    public bool bNotForServer { get; set; }

    /// <summary>Legacy package GUID, serialized between the client/server flags and PackageFlags.</summary>
    public UAssetGuid PackageGuid { get; set; }

    public int PackageFlags { get; set; }

    public bool bNotAlwaysLoadedForEditorGame { get; set; }

    public bool bIsAsset { get; set; }

    public bool bGeneratePublicHash { get; set; }

    public int FirstExportDependency { get; set; }

    public List<FPackageIndex> SerializationBeforeSerializationDependencies { get; } = new();

    public List<FPackageIndex> CreateBeforeSerializationDependencies { get; } = new();

    public List<FPackageIndex> SerializationBeforeCreateDependencies { get; } = new();

    public List<FPackageIndex> CreateBeforeCreateDependencies { get; } = new();

    /// <summary>Parsed tagged properties (or an empty list when the raw payload was opaque).</summary>
    public ObjectExportData? Data { get; set; }
}

/// <summary>
/// Mirrors <c>FPackageFileSummary</c> (PackageFileSummary.h). All counts and
/// offsets are stored as they appear in the file; offsets are backfilled by
/// <see cref="UAssetSerializer"/> during save.
/// </summary>
public sealed class PackageFileSummary
{
    public int FileVersionUe4 { get; set; } = UAssetVersions.CurrentUe4Version;

    public int FileVersionUe5 { get; set; } = UAssetVersions.CurrentUe5Version;

    public int FileVersionLicenseeUe4 { get; set; }

    public List<CustomVersionEntry> CustomVersions { get; } = new();

    public int TotalHeaderSize { get; set; }

    public string FolderName { get; set; } = "/";

    public int PackageFlags { get; set; }

    public int NameCount { get; set; }

    public int NameOffset { get; set; }

    public string LocalizationId { get; set; } = string.Empty;

    public int GatherableTextDataCount { get; set; }

    public int GatherableTextDataOffset { get; set; }

    public int ExportCount { get; set; }

    public int ExportOffset { get; set; }

    public int ImportCount { get; set; }

    public int ImportOffset { get; set; }

    public int DependsOffset { get; set; }

    public int SoftPackageReferencesCount { get; set; }

    public int SoftPackageReferencesOffset { get; set; }

    public int SearchableNamesOffset { get; set; }

    public int ThumbnailTableOffset { get; set; }

    public UAssetGuid Guid { get; set; }

    public UAssetGuid PersistentGuid { get; set; }

    public List<GenerationInfo> Generations { get; } = new();

    /// <summary>SavedByEngineVersion, serialized as int32s + branch name (only when &gt;= VER_UE4_ENGINE_VERSION_OBJECT).</summary>
    public (int Major, int Minor, int Patch, int Changelist, string Branch) SavedByEngineVersion { get; set; }

    /// <summary>CompatibleWithEngineVersion (only when &gt;= VER_UE4_PACKAGE_SUMMARY_HAS_COMPATIBLE_ENGINE_VERSION).</summary>
    public (int Major, int Minor, int Patch, int Changelist, string Branch) CompatibleWithEngineVersion { get; set; }

    public int CompressionFlags { get; set; }

    /// <summary>Compressed chunk headers kept verbatim (16 bytes each) for lossless round-trips.</summary>
    public List<byte[]> CompressedChunksRaw { get; } = new();

    public int PackageSource { get; set; }

    /// <summary>AdditionalPackagesToCook FStrings of the summary.</summary>
    public List<string> AdditionalPackagesToCook { get; } = new();

    public long AssetRegistryDataOffset { get; set; }

    public long BulkDataStartOffset { get; set; }

    public long WorldTileInfoDataOffset { get; set; }

    public List<int> ChunkIds { get; } = new();

    public int PreloadDependencyCount { get; set; }

    public int PreloadDependencyOffset { get; set; }

    public int NamesReferencedFromExportDataCount { get; set; }

    public long PayloadTocOffset { get; set; } = -1;
}