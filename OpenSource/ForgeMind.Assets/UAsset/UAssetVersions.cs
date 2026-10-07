// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// Package version constants mirroring
/// <c>Engine/Source/Runtime/Core/Public/UObject/ObjectVersion.h</c> in
/// Unreal Engine 5.0.3. All numeric values were extracted from that header;
/// keep them in sync if a different engine version needs to be targeted.
/// </summary>
public static class UAssetVersions
{
    /// <summary>
    /// <c>PACKAGE_FILE_TAG</c>: 32-bit magic written at offset 0 of every
    /// uasset. Little-endian bytes on disk are <c>C1 83 2A 9E</c>.
    /// </summary>
    public const uint PackageFileTag = 0x9E2A83C1;

    /// <summary>
    /// <c>PACKAGE_FILE_TAG_SWAPPED</c>: magic of a big-endian (byteswapped)
    /// package. Not consumed by this module, listed for completeness.
    /// </summary>
    public const uint PackageFileTagSwapped = 0xC1832A9E;

    /// <summary>
    /// <c>CurrentLegacyFileVersion</c> in PackageFileSummary.cpp. Values &lt; 0
    /// mean the modern version fields follow; -8 additionally carries the UE5
    /// version integer. Unchanged since UE 4.22.
    /// </summary>
    public const int LegacyFileVersion = -8;

    /// <summary>
    /// <c>VER_UE4_AUTOMATIC_VERSION</c> used by UE 5.0.3
    /// (EUnrealEngineObjectUE4Version, baseline VER_UE4_OLDEST_LOADABLE_PACKAGE = 214).
    /// </summary>
    public const int CurrentUe4Version = 522;

    /// <summary>
    /// <c>EUnrealEngineObjectUE5Version::AUTOMATIC_VERSION</c> (INITIAL_VERSION = 1000).
    /// </summary>
    public const int CurrentUe5Version = 1004;

    // --- EUnrealEngineObjectUE4Version thresholds referenced by this module ---
    // Values were re-verified against ObjectVersion.h of UE 5.0.3 by parsing
    // the enum from VER_UE4_OLDEST_LOADABLE_PACKAGE = 214; the result
    // terminates at VER_UE4_AUTOMATIC_VERSION = 522, matching real 5.0.3
    // assets (FileVersionUE4 = 522 in their package header).

    /// <summary>VER_UE4_WORLD_LEVEL_INFO</summary>
    public const int VerUe4WorldTileInfo = 224;

    /// <summary>VER_UE4_ADDED_CHUNKID_TO_ASSETDATA_AND_UPACKAGE (single ChunkID form)</summary>
    public const int VerUe4AddedChunkId = 278;

    /// <summary>VER_UE4_CHANGED_CHUNKID_TO_BE_AN_ARRAY_OF_CHUNKIDS</summary>
    public const int VerUe4ChunkIds = 326;

    /// <summary>VER_UE4_ENGINE_VERSION_OBJECT</summary>
    public const int VerUe4EngineVersionObject = 336;

    /// <summary>VER_UE4_LOAD_FOR_EDITOR_GAME</summary>
    public const int VerUe4LoadForEditorGame = 365;

    /// <summary>VER_UE4_ADD_STRING_ASSET_REFERENCES_MAP</summary>
    public const int VerUe4StringAssetReferences = 384;

    /// <summary>VER_UE4_STRUCT_GUID_IN_PROPERTY_TAG</summary>
    public const int VerUe4StructGuidInPropertyTag = 441;

    /// <summary>VER_UE4_PACKAGE_SUMMARY_HAS_COMPATIBLE_ENGINE_VERSION</summary>
    public const int VerUe4CompatEngineVersion = 444;

    /// <summary>VER_UE4_SERIALIZE_TEXT_IN_PACKAGES</summary>
    public const int VerUe4SerializeTextInPackages = 459;

    /// <summary>VER_UE4_COOKED_ASSETS_IN_EDITOR_SUPPORT</summary>
    public const int VerUe4CookedAssetsInEditorSupport = 485;

    /// <summary>VER_UE4_PROPERTY_GUID_IN_PROPERTY_TAG</summary>
    public const int VerUe4PropertyGuidInPropertyTag = 503;

    /// <summary>VER_UE4_NAME_HASHES_SERIALIZED</summary>
    public const int VerUe4NameHashesSerialized = 504;

    /// <summary>VER_UE4_PRELOAD_DEPENDENCIES_IN_COOKED_EXPORTS</summary>
    public const int VerUe4PreloadDependencies = 507;

    /// <summary>VER_UE4_TemplateIndex_IN_COOKED_EXPORTS</summary>
    public const int VerUe4TemplateIndex = 508;

    /// <summary>VER_UE4_PROPERTY_TAG_SET_MAP_SUPPORT</summary>
    public const int VerUe4PropertyTagSetMap = 509;

    /// <summary>VER_UE4_ARRAY_PROPERTY_INNER_TAGS</summary>
    public const int VerUe4ArrayPropertyInnerTags = 515;

    /// <summary>VER_UE4_ADDED_SEARCHABLE_NAMES</summary>
    public const int VerUe4SearchableNames = 510;

    /// <summary>VER_UE4_64BIT_EXPORTMAP_SERIALSIZES</summary>
    public const int VerUe4_64BitExportMapSizes = 511;

    /// <summary>VER_UE4_ADDED_PACKAGE_SUMMARY_LOCALIZATION_ID</summary>
    public const int VerUe4LocalizationId = 516;

    /// <summary>VER_UE4_ADDED_PACKAGE_OWNER</summary>
    public const int VerUe4PackageOwner = 518;

    /// <summary>VER_UE4_NON_OUTER_PACKAGE_IMPORT</summary>
    public const int VerUe4NonOuterPackageImport = 520;

    // --- EUnrealEngineObjectUE5Version thresholds ---

    /// <summary>EUnrealEngineObjectUE5Version::NAMES_REFERENCED_FROM_EXPORT_DATA</summary>
    public const int VerUe5NamesReferencedFromExportData = 1001;

    /// <summary>EUnrealEngineObjectUE5Version::PAYLOAD_TOC</summary>
    public const int VerUe5PayloadToc = 1002;

    /// <summary>EUnrealEngineObjectUE5Version::OPTIONAL_RESOURCES</summary>
    public const int VerUe5OptionalResources = 1003;

    // --- UE property type names (FPropertyTag.Type / FName comparison index) ---

    public const string TypeIntProperty = "IntProperty";
    public const string TypeUInt32Property = "UInt32Property";
    public const string TypeInt64Property = "Int64Property";
    public const string TypeUInt64Property = "UInt64Property";
    public const string TypeFloatProperty = "FloatProperty";
    public const string TypeDoubleProperty = "DoubleProperty";
    public const string TypeBoolProperty = "BoolProperty";
    public const string TypeByteProperty = "ByteProperty";
    public const string TypeEnumProperty = "EnumProperty";
    public const string TypeNameProperty = "NameProperty";
    public const string TypeStrProperty = "StrProperty";
    public const string TypeObjectProperty = "ObjectProperty";
    public const string TypeStructProperty = "StructProperty";
    public const string TypeArrayProperty = "ArrayProperty";
    public const string TypeSetProperty = "SetProperty";
    public const string TypeMapProperty = "MapProperty";
    public const string TypeInterfaceProperty = "InterfaceProperty";
    public const string TypeSoftObjectProperty = "SoftObjectProperty";
    public const string TypeSoftClassProperty = "SoftClassProperty";
    public const string TypeAssetObjectProperty = "AssetObjectProperty";
    public const string TypeAssetClassProperty = "AssetClassProperty";
    public const string TypeWeakObjectProperty = "WeakObjectProperty";
    public const string TypeWeakClassProperty = "WeakClassProperty";
    public const string TypeLazyObjectProperty = "LazyObjectProperty";
    public const string TypeMulticastDelegateProperty = "MulticastDelegateProperty";
    public const string TypeDelegateProperty = "DelegateProperty";
    public const string TypeTextProperty = "TextProperty";
}