// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// Parses a uasset byte stream into a <see cref="UAssetPackage"/> and writes
/// packages back. The stream must start with the package file tag 0x9E2A83C1.
///
/// Layout reference: FLinkerLoad in UnrealEngine-5.0.3-release. The save path
/// follows UAssetAPI's PackageWriter principle: sections are written in load
/// order (summary → name table → gatherable text → imports → exports →
/// depends → soft references → searchable names → object data) and every
/// summary offset/count is backfilled after the data is laid out.
///
/// Limitation: exports whose payload could not be modelled (opaque
/// RawData) round-trip byte for byte, but unversioned property streams
/// (UAssetAPI FUnversionedHeader/FFragment) are not decomposed into named
/// properties — see UAssetVersions for the version coverage of this module.
/// </summary>
public static class UAssetSerializer
{
    /// <summary>
    /// Parses a uasset byte stream into a <see cref="UAssetPackage"/>. The
    /// stream must start with the package file tag 0x9E2A83C1.
    /// </summary>
    public static UAssetPackage Load(Stream stream)
    {
        UAssetReader reader = new(stream);
        uint tag = reader.ReadUInt32();
        if (tag != UAssetVersions.PackageFileTag)
        {
            if (tag == UAssetVersions.PackageFileTagSwapped)
            {
                throw new InvalidDataException("Big-endian (byteswapped) uassets are not supported.");
            }

            throw new InvalidDataException($"Not a uasset: expected package tag 0x9E2A83C1, found 0x{tag:x8}.");
        }

        int legacy = reader.ReadInt32();
        if (legacy >= 0)
        {
            throw new InvalidDataException($"Unsupported legacy package version {legacy}: UE3-era packages are not supported.");
        }

        if (legacy < UAssetVersions.LegacyFileVersion)
        {
            throw new InvalidDataException($"Unsupported legacy file version {legacy} (newer than the latest supported format).");
        }

        if (legacy != -4)
        {
            _ = reader.ReadInt32(); // LegacyUE3Version
        }

        int ue4Version = reader.ReadInt32();
        if (ue4Version <= 0)
        {
            throw new InvalidDataException("Package carries no UE4 version field.");
        }

        int ue5Version = legacy <= -8 ? reader.ReadInt32() : 0;
        int licenseeUe4 = reader.ReadInt32();

        UAssetPackage package = new();
        PackageFileSummary summary = package.Summary;
        summary.FileVersionUe4 = ue4Version;
        summary.FileVersionUe5 = ue5Version;
        summary.FileVersionLicenseeUe4 = licenseeUe4;

        // Custom version container
        if (legacy <= -2)
        {
            int customVersionCount = reader.ReadInt32();
            for (int i = 0; i < customVersionCount; i++)
            {
                if (legacy == -2)
                {
                    summary.CustomVersions.Add(new CustomVersionEntry(reader.ReadInt32(), reader.ReadInt32()));
                }
                else if (legacy >= -5)
                {
                    UAssetGuid key = reader.ReadGuid();
                    int version = reader.ReadInt32();
                    reader.ReadFString(); // FriendlyName
                    summary.CustomVersions.Add(new CustomVersionEntry(key, version));
                }
                else
                {
                    summary.CustomVersions.Add(new CustomVersionEntry(reader.ReadGuid(), reader.ReadInt32()));
                }
            }
        }

        summary.TotalHeaderSize = reader.ReadInt32();
        summary.FolderName = reader.ReadFString();
        summary.PackageFlags = reader.ReadInt32();
        summary.NameCount = reader.ReadInt32();
        summary.NameOffset = reader.ReadInt32();

        if (ue4Version >= UAssetVersions.VerUe4LocalizationId)
        {
            summary.LocalizationId = reader.ReadFString();
        }

        if (ue4Version >= UAssetVersions.VerUe4SerializeTextInPackages)
        {
            summary.GatherableTextDataCount = reader.ReadInt32();
            summary.GatherableTextDataOffset = reader.ReadInt32();
        }

        summary.ExportCount = reader.ReadInt32();
        summary.ExportOffset = reader.ReadInt32();
        summary.ImportCount = reader.ReadInt32();
        summary.ImportOffset = reader.ReadInt32();
        summary.DependsOffset = reader.ReadInt32();

        if (ue4Version >= UAssetVersions.VerUe4StringAssetReferences)
        {
            summary.SoftPackageReferencesCount = reader.ReadInt32();
            summary.SoftPackageReferencesOffset = reader.ReadInt32();
        }

        if (ue4Version >= UAssetVersions.VerUe4SearchableNames)
        {
            summary.SearchableNamesOffset = reader.ReadInt32();
        }

        summary.ThumbnailTableOffset = reader.ReadInt32();
        summary.Guid = reader.ReadGuid();

        if (ue4Version >= UAssetVersions.VerUe4PackageOwner)
        {
            summary.PersistentGuid = reader.ReadGuid();

            if (ue4Version < UAssetVersions.VerUe4NonOuterPackageImport)
            {
                reader.ReadGuid(); // OwnerPersistentGuid, unused
            }
        }

        int generationCount = reader.ReadInt32();
        for (int i = 0; i < generationCount; i++)
        {
            summary.Generations.Add(new GenerationInfo { ExportCount = reader.ReadInt32(), NameCount = reader.ReadInt32() });
        }

        if (ue4Version >= UAssetVersions.VerUe4EngineVersionObject)
        {
            summary.SavedByEngineVersion = ReadEngineVersion(reader);
        }
        else
        {
            int engineChangelist = reader.ReadInt32();
            summary.SavedByEngineVersion = (4, 0, 0, engineChangelist, string.Empty);
        }

        if (ue4Version >= UAssetVersions.VerUe4CompatEngineVersion)
        {
            summary.CompatibleWithEngineVersion = ReadEngineVersion(reader);
        }
        else
        {
            summary.CompatibleWithEngineVersion = summary.SavedByEngineVersion;
        }

        summary.CompressionFlags = reader.ReadInt32();
        int compressedChunkCount = reader.ReadInt32();
        for (int i = 0; i < compressedChunkCount; i++)
        {
            // Compressed chunk headers are kept verbatim (16 bytes each) so a
            // later save can reproduce them byte for byte.
            summary.CompressedChunksRaw.Add(reader.ReadBytes(16));
        }

        summary.PackageSource = reader.ReadInt32();

        int additionalPackagesToCookCount = reader.ReadInt32();
        for (int i = 0; i < additionalPackagesToCookCount; i++)
        {
            summary.AdditionalPackagesToCook.Add(reader.ReadFString());
        }

        if (legacy > -7)
        {
            int numTextureAllocations = reader.ReadInt32();
            if (numTextureAllocations != 0)
            {
                throw new InvalidDataException($"Unexpected NumTextureAllocations {numTextureAllocations} in package summary.");
            }
        }

        summary.AssetRegistryDataOffset = reader.ReadInt32();
        summary.BulkDataStartOffset = reader.ReadInt64();

        if (ue4Version >= UAssetVersions.VerUe4WorldTileInfo)
        {
            summary.WorldTileInfoDataOffset = reader.ReadInt32();
        }

        if (ue4Version >= UAssetVersions.VerUe4ChunkIds)
        {
            int chunkCount = reader.ReadInt32();
            for (int i = 0; i < chunkCount; i++)
            {
                summary.ChunkIds.Add(reader.ReadInt32());
            }
        }
        else if (ue4Version >= UAssetVersions.VerUe4AddedChunkId)
        {
            int chunkId = reader.ReadInt32();
            if (chunkId >= 0)
            {
                summary.ChunkIds.Add(chunkId);
            }
        }

        if (ue4Version >= UAssetVersions.VerUe4PreloadDependencies)
        {
            summary.PreloadDependencyCount = reader.ReadInt32();
            summary.PreloadDependencyOffset = reader.ReadInt32();
        }
        else
        {
            summary.PreloadDependencyCount = -1;
            summary.PreloadDependencyOffset = 0;
        }

        if (ue5Version >= UAssetVersions.VerUe5NamesReferencedFromExportData)
        {
            summary.NamesReferencedFromExportDataCount = reader.ReadInt32();
        }
        else
        {
            summary.NamesReferencedFromExportDataCount = summary.NameCount;
        }

        if (ue5Version >= UAssetVersions.VerUe5PayloadToc)
        {
            summary.PayloadTocOffset = reader.ReadInt64();
        }
        else
        {
            summary.PayloadTocOffset = -1;
        }

        // Name table.
        reader.Seek(summary.NameOffset);
        for (int i = 0; i < summary.NameCount; i++)
        {
            package.GetNameIndex(reader.ReadNameEntry(ue4Version >= UAssetVersions.VerUe4NameHashesSerialized));
        }

        // Gatherable text data.
        reader.Seek(summary.GatherableTextDataOffset);
        for (int i = 0; i < summary.GatherableTextDataCount; i++)
        {
            package.GatherableTexts.Add(ReadGatherableText(reader));
        }

        // Import map.
        reader.Seek(summary.ImportOffset);
        for (int i = 0; i < summary.ImportCount; i++)
        {
            package.Imports.Add(ReadImport(reader, package, ue4Version, ue5Version));
        }

        // Export map.
        reader.Seek(summary.ExportOffset);
        for (int i = 0; i < summary.ExportCount; i++)
        {
            package.Exports.Add(ReadExport(reader, package, ue4Version, ue5Version));
        }

        // Depends map: no total count prefix, exactly ExportCount entries.
        reader.Seek(summary.DependsOffset);
        for (int i = 0; i < summary.ExportCount; i++)
        {
            int dependencyCount = reader.ReadInt32();
            reader.Seek(reader.Position + dependencyCount * 4);
        }

        // Soft package references.
        reader.Seek(summary.SoftPackageReferencesOffset);
        for (int i = 0; i < summary.SoftPackageReferencesCount; i++)
        {
            (int index, int number) = reader.ReadFName();
            string name = package.GetName(index);
            package.SoftPackageReferences.Add(number != 0 ? $"{name}_{number}" : name);
        }

        // Searchable names.
        if (summary.SearchableNamesOffset > 0)
        {
            reader.Seek(summary.SearchableNamesOffset);
            int searchableCount = reader.ReadInt32();
            for (int i = 0; i < searchableCount; i++)
            {
                int key = reader.ReadInt32();
                int nameCount = reader.ReadInt32();
                List<(int Index, int Number)> names = new(nameCount);
                for (int j = 0; j < nameCount; j++)
                {
                    (int index, int number) = reader.ReadFName();
                    names.Add((index, number));
                }

                package.SearchableNames.Add((new FPackageIndex(key), names));
            }
        }

        // Serialized object data.
        if (summary.GatherableTextDataOffset > 0)
        {
            reader.Seek(summary.GatherableTextDataOffset);
        }
        else
        {
            reader.Seek(summary.TotalHeaderSize);
        }

        foreach (ObjectExport export in package.Exports)
        {
            if (export.SerialOffset <= 0 || export.SerialSize <= 0)
            {
                export.Data ??= new ObjectExportData();
                continue;
            }

            reader.Seek(export.SerialOffset);
            byte[] raw = reader.ReadBytes((int)export.SerialSize);
            ObjectExportData? data = TryParseExportData(raw, package, ue4Version);
            export.Data = data ?? new ObjectExportData { RawData = raw };
        }

        return package;
    }

    private static (int Major, int Minor, int Patch, int Changelist, string Branch) ReadEngineVersion(UAssetReader reader) =>
        (reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), (int)reader.ReadUInt32(), reader.ReadFString());

    // --- gatherable text data ---

    private static GatherableTextData ReadGatherableText(UAssetReader reader)
    {
        GatherableTextData text = new()
        {
            NamespaceName = reader.ReadFString(),
            SourceString = reader.ReadFString(),
        };
        ReadLocMetadataObject(reader, text.SourceStringMetaData);

        int contextCount = reader.ReadInt32();
        for (int i = 0; i < contextCount; i++)
        {
            text.Contexts.Add(ReadTextSiteContext(reader));
        }

        return text;
    }

    private static TextSourceSiteContext ReadTextSiteContext(UAssetReader reader)
    {
        TextSourceSiteContext context = new()
        {
            KeyName = reader.ReadFString(),
            SiteDescription = reader.ReadFString(),
            IsEditorOnly = reader.ReadBool(),
            IsOptional = reader.ReadBool(),
        };
        ReadLocMetadataObject(reader, context.InfoMetaData);
        ReadLocMetadataObject(reader, context.KeyMetaData);

        return context;
    }

    private static void ReadLocMetadataObject(UAssetReader reader, List<LocMetadataEntry> entries)
    {
        int entryCount = reader.ReadInt32();
        for (int i = 0; i < entryCount; i++)
        {
            LocMetadataEntry entry = new()
            {
                Key = reader.ReadFString(),
                Value = ReadLocMetadataValue(reader),
            };
            entries.Add(entry);
        }
    }

    private static LocMetadataValue ReadLocMetadataValue(UAssetReader reader)
    {
        LocMetadataValue value = new()
        {
            Type = reader.ReadInt32(),
        };
        switch (value.Type)
        {
            case 1: // ELocMetadataType::Boolean
                value.BoolValue = reader.ReadBool();
                break;
            case 2: // ELocMetadataType::String
                value.StringValue = reader.ReadFString();
                break;
            case 3: // ELocMetadataType::Array
            {
                int elementCount = reader.ReadInt32();
                for (int i = 0; i < elementCount; i++)
                {
                    value.ArrayElements.Add(ReadLocMetadataValue(reader));
                }

                break;
            }
            case 4: // ELocMetadataType::Object
                ReadLocMetadataObject(reader, value.ObjectEntries);
                break;
        }

        return value;
    }

    // --- import / export map ---

    private static ObjectImport ReadImport(UAssetReader reader, UAssetPackage package, int ue4Version, int ue5Version)
    {
        int classPackageIndex = reader.ReadFName().Index;
        int classNameIndex = reader.ReadFName().Index;
        FPackageIndex outerIndex = new(reader.ReadInt32());
        int objectNameIndex = reader.ReadFName().Index;
        int packageNameIndex = ue4Version >= UAssetVersions.VerUe4NonOuterPackageImport ? reader.ReadFName().Index : 0;
        bool importOptional = ue5Version >= UAssetVersions.VerUe5OptionalResources && reader.ReadBool();
        return new ObjectImport
        {
            ClassPackage = package.GetName(classPackageIndex),
            ClassName = package.GetName(classNameIndex),
            OuterIndex = outerIndex,
            ObjectName = package.GetName(objectNameIndex),
            PackageName = package.GetName(packageNameIndex),
            bImportOptional = importOptional,
        };
    }

    private static ObjectExport ReadExport(UAssetReader reader, UAssetPackage package, int ue4Version, int ue5Version)
    {
        ObjectExport export = new()
        {
            ClassIndex = new FPackageIndex(reader.ReadInt32()),
            SuperIndex = new FPackageIndex(reader.ReadInt32()),
        };
        if (ue4Version >= UAssetVersions.VerUe4TemplateIndex)
        {
            export.TemplateIndex = new FPackageIndex(reader.ReadInt32());
        }

        export.OuterIndex = new FPackageIndex(reader.ReadInt32());
        export.ObjectName = package.GetName(reader.ReadFName().Index);
        export.ObjectFlags = reader.ReadInt32();
        if (ue4Version >= UAssetVersions.VerUe4_64BitExportMapSizes)
        {
            export.SerialSize = reader.ReadInt64();
            export.SerialOffset = reader.ReadInt64();
        }
        else
        {
            export.SerialSize = reader.ReadInt32();
            export.SerialOffset = reader.ReadInt32();
        }

        export.bForcedExport = reader.ReadBool();
        export.bNotForClient = reader.ReadBool();
        export.bNotForServer = reader.ReadBool();
        export.PackageGuid = reader.ReadGuid();
        export.PackageFlags = reader.ReadInt32();
        if (ue4Version >= UAssetVersions.VerUe4LoadForEditorGame)
        {
            export.bNotAlwaysLoadedForEditorGame = reader.ReadBool();
        }

        if (ue4Version >= UAssetVersions.VerUe4CookedAssetsInEditorSupport)
        {
            export.bIsAsset = reader.ReadBool();
        }

        if (ue5Version >= UAssetVersions.VerUe5OptionalResources)
        {
            export.bGeneratePublicHash = reader.ReadBool();
        }

        if (ue4Version >= UAssetVersions.VerUe4PreloadDependencies)
        {
            export.FirstExportDependency = reader.ReadInt32();
            ReadDependencies(reader, export.SerializationBeforeSerializationDependencies);
            ReadDependencies(reader, export.CreateBeforeSerializationDependencies);
            ReadDependencies(reader, export.SerializationBeforeCreateDependencies);
            ReadDependencies(reader, export.CreateBeforeCreateDependencies);
        }

        return export;
    }

    private static void ReadDependencies(UAssetReader reader, List<FPackageIndex> target)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            target.Add(new FPackageIndex(reader.ReadInt32()));
        }
    }

    // --- export payloads (tagged properties) ---

    /// <summary>Parses a tagged-property section; returns null when it contains data we cannot represent.</summary>
    private static ObjectExportData? TryParseExportData(byte[] raw, UAssetPackage package, int ue4Version)
    {
        try
        {
            using MemoryStream stream = new(raw);
            UAssetReader reader = new(stream);
            ObjectExportData data = new() { RawData = raw };
            while (reader.Position < raw.Length)
            {
                UPropertyTag property = UPropertyTag.ReadFrom(reader, package, ue4Version);
                if (property.IsNone)
                {
                    break;
                }

                data.Properties.Add(property);
            }

            return data;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // --- save (UAssetAPI PackageWriter principle: write sections in load
    //     order, backfill every summary offset/count afterwards) ---

    /// <summary>
    /// Writes a <see cref="UAssetPackage"/> back to a uasset byte stream.
    /// Exports whose payload is opaque (RawData) round-trip unchanged; modelled
    /// exports are re-serialized from their tagged property trees.
    /// </summary>
    public static void Save(Stream stream, UAssetPackage package)
    {
        package.PrepareNames();
        UAssetWriter writer = new(stream);
        PackageFileSummary s = package.Summary;
        int ue4 = s.FileVersionUe4;
        int ue5 = s.FileVersionUe5;

        writer.WriteUInt32(UAssetVersions.PackageFileTag);
        writer.WriteInt32(UAssetVersions.LegacyFileVersion); // -8
        writer.WriteInt32(ue4);
        writer.WriteInt32(ue5);
        writer.WriteInt32(s.FileVersionLicenseeUe4);

        writer.WriteInt32(s.CustomVersions.Count);
        foreach (CustomVersionEntry cv in s.CustomVersions)
        {
            if (cv.Key is null)
            {
                throw new InvalidDataException("Legacy-format custom versions cannot be saved in a -8 package.");
            }

            writer.WriteGuid(cv.Key.Value);
            writer.WriteInt32(cv.Version);
        }

        long totalHeaderSizePos = writer.Position;
        writer.WriteInt32(0);
        writer.WriteFString(s.FolderName);
        writer.WriteInt32(s.PackageFlags);

        long nameCountPos = writer.Position;
        writer.WriteInt32(0);
        long nameOffsetPos = writer.Position;
        writer.WriteInt32(0);

        if (ue4 >= UAssetVersions.VerUe4LocalizationId)
        {
            writer.WriteFString(s.LocalizationId);
        }

        long gatherableCountPos = 0;
        long gatherableOffsetPos = 0;
        if (ue4 >= UAssetVersions.VerUe4SerializeTextInPackages)
        {
            gatherableCountPos = writer.Position;
            writer.WriteInt32(package.GatherableTexts.Count);
            gatherableOffsetPos = writer.Position;
            writer.WriteInt32(0);
        }

        long exportCountPos = writer.Position;
        writer.WriteInt32(package.Exports.Count);
        long exportOffsetPos = writer.Position;
        writer.WriteInt32(0);
        long importCountPos = writer.Position;
        writer.WriteInt32(package.Imports.Count);
        long importOffsetPos = writer.Position;
        writer.WriteInt32(0);
        long dependsOffsetPos = writer.Position;
        writer.WriteInt32(0);

        long softRefCountPos = 0;
        long softRefOffsetPos = 0;
        if (ue4 >= UAssetVersions.VerUe4StringAssetReferences)
        {
            softRefCountPos = writer.Position;
            writer.WriteInt32(package.SoftPackageReferences.Count);
            softRefOffsetPos = writer.Position;
            writer.WriteInt32(0);
        }

        long searchableOffsetPos = 0;
        if (ue4 >= UAssetVersions.VerUe4SearchableNames)
        {
            searchableOffsetPos = writer.Position;
            writer.WriteInt32(0); // backfilled when the package carries searchable names
        }

        writer.WriteInt32(s.ThumbnailTableOffset);
        writer.WriteGuid(s.Guid);

        if (ue4 >= UAssetVersions.VerUe4PackageOwner)
        {
            writer.WriteGuid(s.PersistentGuid);
        }

        writer.WriteInt32(s.Generations.Count);
        foreach (GenerationInfo generation in s.Generations)
        {
            writer.WriteInt32(generation.ExportCount);
            writer.WriteInt32(generation.NameCount);
        }

        if (ue4 >= UAssetVersions.VerUe4EngineVersionObject)
        {
            WriteEngineVersion(writer, s.SavedByEngineVersion);
        }
        else
        {
            writer.WriteInt32(s.SavedByEngineVersion.Changelist);
        }

        if (ue4 >= UAssetVersions.VerUe4CompatEngineVersion)
        {
            WriteEngineVersion(writer, s.CompatibleWithEngineVersion);
        }

        writer.WriteInt32(s.CompressionFlags);
        writer.WriteInt32(s.CompressedChunksRaw.Count);
        foreach (byte[] chunk in s.CompressedChunksRaw)
        {
            writer.WriteBytes(chunk.Length == 16 ? chunk : new byte[16]);
        }

        writer.WriteInt32(s.PackageSource);
        writer.WriteInt32(s.AdditionalPackagesToCook.Count);
        foreach (string additionalPackage in s.AdditionalPackagesToCook)
        {
            writer.WriteFString(additionalPackage);
        }

        writer.WriteInt32((int)s.AssetRegistryDataOffset);
        writer.WriteInt64(s.BulkDataStartOffset);

        if (ue4 >= UAssetVersions.VerUe4WorldTileInfo)
        {
            writer.WriteInt32((int)s.WorldTileInfoDataOffset);
        }

        if (ue4 >= UAssetVersions.VerUe4ChunkIds)
        {
            writer.WriteInt32(s.ChunkIds.Count);
            foreach (int chunkId in s.ChunkIds)
            {
                writer.WriteInt32(chunkId);
            }
        }
        else if (ue4 >= UAssetVersions.VerUe4AddedChunkId && s.ChunkIds.Count > 0)
        {
            writer.WriteInt32(s.ChunkIds[0]);
        }

        if (ue4 >= UAssetVersions.VerUe4PreloadDependencies)
        {
            writer.WriteInt32(s.PreloadDependencyCount);
            writer.WriteInt32(s.PreloadDependencyOffset);
        }

        if (ue5 >= UAssetVersions.VerUe5NamesReferencedFromExportData)
        {
            writer.WriteInt32(s.NamesReferencedFromExportDataCount);
        }

        if (ue5 >= UAssetVersions.VerUe5PayloadToc)
        {
            writer.WriteInt64(s.PayloadTocOffset);
        }

        // Name table (starts right after the summary, padded to 4 bytes).
        writer.Seek((writer.Position + 3) & ~3);
        writer.WriteInt32At(totalHeaderSizePos, (int)writer.Position);
        writer.WriteInt32At(nameOffsetPos, (int)writer.Position);
        writer.WriteInt32At(nameCountPos, package.Names.Count);
        for (int i = 0; i < package.Names.Count; i++)
        {
            writer.WriteNameEntry(package.Names[i], ue4 >= UAssetVersions.VerUe4NameHashesSerialized);
        }

        // Gatherable text data.
        if (ue4 >= UAssetVersions.VerUe4SerializeTextInPackages)
        {
            writer.WriteInt32At(gatherableOffsetPos, (int)writer.Position);
            foreach (GatherableTextData text in package.GatherableTexts)
            {
                WriteGatherableText(writer, text);
            }
        }

        // Import map.
        writer.WriteInt32At(importOffsetPos, (int)writer.Position);
        foreach (ObjectImport import in package.Imports)
        {
            writer.WriteFName(package.GetNameIndex(import.ClassPackage));
            writer.WriteFName(package.GetNameIndex(import.ClassName));
            writer.WriteInt32(import.OuterIndex.Raw);
            writer.WriteFName(package.GetNameIndex(import.ObjectName));
            if (ue4 >= UAssetVersions.VerUe4NonOuterPackageImport)
            {
                writer.WriteFName(package.GetNameIndex(import.PackageName));
            }

            if (ue5 >= UAssetVersions.VerUe5OptionalResources)
            {
                writer.WriteBool(import.bImportOptional);
            }
        }

        // Export map. Serial size/offset are placeholders until the object
        // data section is laid out below.
        var exportDataPositions = new List<(long SizePos, long OffsetPos, ObjectExport Export)>();
        writer.WriteInt32At(exportOffsetPos, (int)writer.Position);
        foreach (ObjectExport export in package.Exports)
        {
            writer.WriteInt32(export.ClassIndex.Raw);
            writer.WriteInt32(export.SuperIndex.Raw);
            if (ue4 >= UAssetVersions.VerUe4TemplateIndex)
            {
                writer.WriteInt32(export.TemplateIndex.Raw);
            }

            writer.WriteInt32(export.OuterIndex.Raw);
            writer.WriteFName(package.GetNameIndex(export.ObjectName));
            writer.WriteInt32(export.ObjectFlags);

            long sizePos = writer.Position;
            long offsetPos;
            if (ue4 >= UAssetVersions.VerUe4_64BitExportMapSizes)
            {
                writer.WriteInt64(0);
                offsetPos = writer.Position;
                writer.WriteInt64(0);
            }
            else
            {
                writer.WriteInt32(0);
                offsetPos = writer.Position;
                writer.WriteInt32(0);
            }

            exportDataPositions.Add((sizePos, offsetPos, export));

            writer.WriteBool(export.bForcedExport);
            writer.WriteBool(export.bNotForClient);
            writer.WriteBool(export.bNotForServer);
            writer.WriteGuid(export.PackageGuid);
            writer.WriteInt32(export.PackageFlags);
            if (ue4 >= UAssetVersions.VerUe4LoadForEditorGame)
            {
                writer.WriteBool(export.bNotAlwaysLoadedForEditorGame);
            }

            if (ue4 >= UAssetVersions.VerUe4CookedAssetsInEditorSupport)
            {
                writer.WriteBool(export.bIsAsset);
            }

            if (ue5 >= UAssetVersions.VerUe5OptionalResources)
            {
                writer.WriteBool(export.bGeneratePublicHash);
            }

            if (ue4 >= UAssetVersions.VerUe4PreloadDependencies)
            {
                writer.WriteInt32(export.FirstExportDependency);
                WriteDependencies(writer, export.SerializationBeforeSerializationDependencies);
                WriteDependencies(writer, export.CreateBeforeSerializationDependencies);
                WriteDependencies(writer, export.SerializationBeforeCreateDependencies);
                WriteDependencies(writer, export.CreateBeforeCreateDependencies);
            }
        }

        // Depends map: not collected at load time (preloads live in the export
        // map entries above); written as empty per-export dependency lists —
        // the editor rebuilds them on the next save.
        writer.WriteInt32At(dependsOffsetPos, (int)writer.Position);
        for (int i = 0; i < package.Exports.Count; i++)
        {
            writer.WriteInt32(0);
        }

        // Soft package references.
        if (ue4 >= UAssetVersions.VerUe4StringAssetReferences)
        {
            writer.WriteInt32At(softRefOffsetPos, (int)writer.Position);
            foreach (string softReference in package.SoftPackageReferences)
            {
                (string baseName, int number) = SplitSoftReference(softReference);
                writer.WriteFName(package.GetNameIndex(baseName), number);
            }
        }

        // Searchable names (only when the source package carried them).
        if (ue4 >= UAssetVersions.VerUe4SearchableNames && package.SearchableNames.Count > 0)
        {
            writer.WriteInt32At(searchableOffsetPos, (int)writer.Position);
            writer.WriteInt32(package.SearchableNames.Count);
            foreach ((FPackageIndex key, List<(int Index, int Number)> names) in package.SearchableNames)
            {
                writer.WriteInt32(key.Raw);
                writer.WriteInt32(names.Count);
                foreach ((int index, int number) in names)
                {
                    writer.WriteFName(index, number);
                }
            }
        }

        // Serialized object data: modelled exports are re-serialized from
        // their tagged property trees; opaque exports round-trip verbatim.
        for (int i = 0; i < package.Exports.Count; i++)
        {
            ObjectExport export = package.Exports[i];
            (long sizePos, long offsetPos, _) = exportDataPositions[i];
            if (export.Data is null)
            {
                continue;
            }

            writer.WriteInt32At(offsetPos, (int)writer.Position);
            long dataStart = writer.Position;

            if (export.Data.RawData is { } raw && export.Data.Properties.Count == 0)
            {
                writer.WriteBytes(raw);
            }
            else
            {
                foreach (UPropertyTag property in export.Data.Properties)
                {
                    property.WriteTo(writer, package, ue4);
                }

                writer.WriteFName(package.GetNameIndex("None"));
            }

            long size = writer.Position - dataStart;
            if (ue4 >= UAssetVersions.VerUe4_64BitExportMapSizes)
            {
                writer.WriteInt64At(sizePos, size);
                writer.WriteInt64At(offsetPos, dataStart);
            }
            else
            {
                writer.WriteInt32At(sizePos, (int)size);
                writer.WriteInt32At(offsetPos, (int)dataStart);
            }

            export.SerialSize = size;
            export.SerialOffset = dataStart;
        }

        // The name table count feeds the summary's NameCount; backfill it now
        // that PrepareNames has finalized every literal.
        writer.WriteInt32At(nameCountPos, package.Names.Count);
    }

    private static void WriteDependencies(UAssetWriter writer, List<FPackageIndex> dependencies)
    {
        writer.WriteInt32(dependencies.Count);
        foreach (FPackageIndex dependency in dependencies)
        {
            writer.WriteInt32(dependency.Raw);
        }
    }

    private static void WriteEngineVersion(UAssetWriter writer, (int Major, int Minor, int Patch, int Changelist, string Branch) version)
    {
        writer.WriteUInt16((ushort)version.Major);
        writer.WriteUInt16((ushort)version.Minor);
        writer.WriteUInt16((ushort)version.Patch);
        writer.WriteUInt32((uint)version.Changelist);
        writer.WriteFString(version.Branch);
    }

    private static void WriteGatherableText(UAssetWriter writer, GatherableTextData text)
    {
        writer.WriteFString(text.NamespaceName);
        writer.WriteFString(text.SourceString);
        WriteLocMetadataObject(writer, text.SourceStringMetaData);
        writer.WriteInt32(text.Contexts.Count);
        foreach (TextSourceSiteContext context in text.Contexts)
        {
            writer.WriteFString(context.KeyName);
            writer.WriteFString(context.SiteDescription);
            writer.WriteBool(context.IsEditorOnly);
            writer.WriteBool(context.IsOptional);
            WriteLocMetadataObject(writer, context.InfoMetaData);
            WriteLocMetadataObject(writer, context.KeyMetaData);
        }
    }

    private static void WriteLocMetadataObject(UAssetWriter writer, List<LocMetadataEntry> entries)
    {
        writer.WriteInt32(entries.Count);
        foreach (LocMetadataEntry entry in entries)
        {
            writer.WriteFString(entry.Key);
            WriteLocMetadataValue(writer, entry.Value);
        }
    }

    private static void WriteLocMetadataValue(UAssetWriter writer, LocMetadataValue value)
    {
        writer.WriteInt32(value.Type);
        switch (value.Type)
        {
            case 1:
                writer.WriteBool(value.BoolValue);
                break;
            case 2:
                writer.WriteFString(value.StringValue);
                break;
            case 3:
                writer.WriteInt32(value.ArrayElements.Count);
                foreach (LocMetadataValue element in value.ArrayElements)
                {
                    WriteLocMetadataValue(writer, element);
                }

                break;
            case 4:
                WriteLocMetadataObject(writer, value.ObjectEntries);
                break;
        }
    }

    /// <summary>Splits a soft reference literal "Name" / "Name_2" into (name, number).</summary>
    private static (string BaseName, int Number) SplitSoftReference(string softReference)
    {
        int lastUnderscore = softReference.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore < softReference.Length - 1
            && int.TryParse(softReference[(lastUnderscore + 1)..], out int number))
        {
            return (softReference[..lastUnderscore], number);
        }

        return (softReference, 0);
    }
}
