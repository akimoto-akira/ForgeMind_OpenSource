// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// Resolves FName literals against the package name table.
/// Implemented by <see cref="UAssetPackage"/>.
/// </summary>
public interface IUAssetNameResolver
{
    /// <summary>Returns the name table index for <paramref name="name"/>, adding it when missing.</summary>
    int GetNameIndex(string name);

    /// <summary>Returns the name literal for a table index (throws when out of range).</summary>
    string GetName(int index);
}

/// <summary>
/// Mirrors <c>FPropertyTag</c> (PropertyTag.h): the header leading every
/// tagged property in an object data section. A tag whose Name field is
/// NAME_None terminates the property stream (UStruct::SerializeTaggedProperties
/// writes an empty FName after the last property).
///
/// Layout reference: PropertyTag.cpp operator&lt;&lt;(FStructuredArchive::FSlot, FPropertyTag&amp;)
/// in UnrealEngine-5.0.3-release.
/// </summary>
public sealed class UPropertyTag
{
    /// <summary>Property name (FName literal).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>UE property class name, e.g. "IntProperty" (FName literal).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Number component of the Type FName (almost always 0).</summary>
    public int TypeNumber { get; set; }

    /// <summary>
    /// Byte size of the serialized value only (excluding the tag header).
    /// Backfilled during save like UStruct does: Size = Tell() - DataOffset.
    /// </summary>
    public int Size { get; set; }

    /// <summary>Index for static array properties; 0 for regular ones.</summary>
    public int ArrayIndex { get; set; }

    /// <summary>Struct class name; only meaningful for StructProperty.</summary>
    public string StructName { get; set; } = string.Empty;

    /// <summary>Script struct GUID, serialized after StructName.</summary>
    public UAssetGuid StructGuid { get; set; }

    /// <summary>bool value stored in the header of BoolProperty tags (1 byte).</summary>
    public byte BoolVal { get; set; }

    /// <summary>Enum type name; present for ByteProperty (legacy enums) and EnumProperty.</summary>
    public string EnumName { get; set; } = string.Empty;

    /// <summary>Element type name of ArrayProperty (written since VAR_UE4_ARRAY_PROPERTY_INNER_TAGS).</summary>
    public string InnerType { get; set; } = string.Empty;

    /// <summary>Map value type name (VER_UE4_PROPERTY_TAG_SET_MAP_SUPPORT); read but not consumed for values.</summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>Property GUID flag, serialized since VER_UE4_PROPERTY_GUID_IN_PROPERTY_TAG.</summary>
    public bool HasPropertyGuid { get; set; }

    public UAssetGuid PropertyGuid { get; set; }

    /// <summary>Deserialized (or pending serialization) property value.</summary>
    public UPropertyValue? Value { get; set; }

    /// <summary>True for the None-name terminator tag that ends a property stream.</summary>
    public bool IsNone => Name == "None";

    /// <summary>
    /// Deserializes one tagged property. Returns a tag with
    /// <see cref="IsNone"/> == true for the None-name terminator.
    ///
    /// Based on operator&lt;&lt;(FStructuredArchive::FSlot, FPropertyTag&amp;) in
    /// UnrealEngine-5.0.3-release PropertyTag.cpp.
    /// </summary>
    public static UPropertyTag ReadFrom(UAssetReader reader, IUAssetNameResolver resolver, int ue4Version)
    {
        (int nameIndex, _) = reader.ReadFName();
        if (nameIndex == 0) // NAME_None: no further tags in this stream
        {
            return new UPropertyTag { Name = "None" };
        }

        UPropertyTag tag = new() { Name = resolver.GetName(nameIndex) };
        (int typeIndex, int typeNumber) = reader.ReadFName();
        tag.Type = resolver.GetName(typeIndex);
        tag.TypeNumber = typeNumber;
        tag.Size = reader.ReadInt32();
        tag.ArrayIndex = reader.ReadInt32();

        // Type-specific header fields are only serialized when Type.GetNumber() == 0.
        // See PropertyTag.cpp line 116: if (Tag.Type.GetNumber() == 0) { ... }
        if (typeNumber == 0)
        {
            // Comparison by FName entry ID (index). In UE, NAME_StructProperty etc.
            // are pre-defined FName entries whose comparison index matches the
            // string literal. We compare by string here since our name table
            // stores the literal values.
            //
            // Note: K2Node tagged properties often use short type names
            // (e.g. "bool" instead of "BoolProperty", "int" instead of
            // "IntProperty", "float" instead of "FloatProperty"). These short
            // names have the same tag header layout as their full-name
            // counterparts, so we match both forms.
            switch (tag.Type)
            {
                case UAssetVersions.TypeStructProperty:
                case "struct":
                    (int structNameIndex, _) = reader.ReadFName();
                    tag.StructName = resolver.GetName(structNameIndex);
                    if (ue4Version >= UAssetVersions.VerUe4StructGuidInPropertyTag)
                    {
                        tag.StructGuid = reader.ReadGuid();
                    }
                    break;
                case UAssetVersions.TypeBoolProperty:
                case "bool":
                    tag.BoolVal = reader.ReadByte();
                    break;
                case UAssetVersions.TypeByteProperty:
                case "byte":
                case UAssetVersions.TypeEnumProperty:
                case "enum":
                    (int enumNameIndex, _) = reader.ReadFName();
                    tag.EnumName = resolver.GetName(enumNameIndex);
                    break;
                case UAssetVersions.TypeArrayProperty:
                case "array":
                    if (ue4Version >= UAssetVersions.VerUe4ArrayPropertyInnerTags)
                    {
                        (int innerTypeIndex, _) = reader.ReadFName();
                        tag.InnerType = resolver.GetName(innerTypeIndex);
                    }
                    break;
                case "SetProperty":
                case "set":
                    if (ue4Version >= UAssetVersions.VerUe4PropertyTagSetMap)
                    {
                        (int innerTypeIndex, _) = reader.ReadFName();
                        tag.InnerType = resolver.GetName(innerTypeIndex);
                    }
                    break;
                case "MapProperty":
                case "map":
                    if (ue4Version >= UAssetVersions.VerUe4PropertyTagSetMap)
                    {
                        (int innerTypeIndex, _) = reader.ReadFName();
                        tag.InnerType = resolver.GetName(innerTypeIndex);
                        (int valueTypeIndex, _) = reader.ReadFName();
                        tag.ValueType = resolver.GetName(valueTypeIndex);
                    }
                    break;
            }
        }

        // PropertyGuid (VER_UE4_PROPERTY_GUID_IN_PROPERTY_TAG = 503).
        // HasPropertyGuid is serialized as uint8 (1 byte), NOT bool (4 bytes)!
        // See PropertyTag.h: "uint8 HasPropertyGuid = 0;"
        // and PropertyTag.cpp: Slot << SA_ATTRIBUTE("HasPropertyGuid", Tag.HasPropertyGuid)
        // which calls Ar << HasPropertyGuid → operator<<(uint8&) = 1 byte.
        if (ue4Version >= UAssetVersions.VerUe4PropertyGuidInPropertyTag)
        {
            tag.HasPropertyGuid = reader.ReadByte() != 0;
            if (tag.HasPropertyGuid)
            {
                tag.PropertyGuid = reader.ReadGuid();
            }
        }

        // Read the property value body (Size bytes).
        // Based on FPropertyTag::SerializeTaggedProperty: the value body starts
        // here. For BoolProperty, the value body is empty (the bool value is in
        // the tag header's BoolVal byte, and Serialize(nullptr, 0) is called).
        long valueStart = reader.Position;

        if (tag.Size == 0)
        {
            // BoolProperty keeps its value in the header byte; expose it as a
            // Boolean value so callers read/write a consistent shape. Other
            // empty bodies carry no value.
            tag.Value = tag.Type is UAssetVersions.TypeBoolProperty or "bool"
                ? UPropertyValue.Boolean(tag.BoolVal != 0)
                : null;
        }
        else
        {
            try
            {
                if (tag.Type == UAssetVersions.TypeStructProperty
                    || tag.Type == "struct")
                {
                    tag.Value = ReadStructValue(reader, tag.StructName, resolver, ue4Version);
                }
                else
                {
                    tag.Value = tag.Type switch
                    {
                        UAssetVersions.TypeArrayProperty => UPropertyValue.ReadArrayValue(reader, tag.InnerType, resolver, ue4Version),
                        _ => UPropertyValue.ReadValue(reader, tag.Type, tag.EnumName, resolver, ue4Version),
                    };
                }
            }
            catch (Exception)
            {
                // Unsupported property type or parse error: skip the value body
                // using tag.Size so subsequent tags can still be read correctly.
                tag.Value = null;
            }

            // Always align the reader to the end of the value body, exactly
            // like UE's SerializeTaggedProperty does:
            //   if (Size && (EndOfProperty - StartOfProperty != Size))
            //       UnderlyingArchive.Seek(StartOfProperty + Size);
            // This is critical because value body readers may not consume
            // exactly Size bytes (e.g. StructProperty value bodies contain
            // nested tagged properties with their own headers, but simple
            // struct readers like Guid only read 16 raw bytes).
            reader.Seek(valueStart + tag.Size);
        }

        return tag;
    }

    /// <summary>
    /// Reads a StructProperty value. For simple structs whose value body is
    /// raw bytes (not nested tagged properties), reads the raw bytes directly
    /// to avoid misparse. Falls back to ReadStructFields for unknown structs.
    /// </summary>
    private static UPropertyValue ReadStructValue(UAssetReader reader,
        string structName, IUAssetNameResolver resolver, int ue4Version)
    {
        return structName switch
        {
            "Guid" => UPropertyValue.RawBytes(reader.ReadBytes(16)),
            "Vector" => UPropertyValue.RawBytes(reader.ReadBytes(12)),
            "Vector2D" => UPropertyValue.RawBytes(reader.ReadBytes(8)),
            "Rotator" => UPropertyValue.RawBytes(reader.ReadBytes(12)),
            "LinearColor" => UPropertyValue.RawBytes(reader.ReadBytes(16)),
            "Color" => UPropertyValue.RawBytes(reader.ReadBytes(4)),
            "Quat" => UPropertyValue.RawBytes(reader.ReadBytes(16)),
            "Vector2f" => UPropertyValue.RawBytes(reader.ReadBytes(8)),
            "Vector4" => UPropertyValue.RawBytes(reader.ReadBytes(16)),
            "IntPoint" => UPropertyValue.RawBytes(reader.ReadBytes(8)),
            _ => UPropertyValue.Struct(UPropertyValue.ReadStructFields(reader, resolver, ue4Version)),
        };
    }

    /// <summary>
    /// Serializes one tagged property, mirroring the ReadFrom layout: header
    /// (name/type/size/array index + type-specific fields + optional GUID),
    /// then the value body. The body is buffered first so <see cref="Size"/>
    /// can be backfilled exactly like UStruct::SerializeTaggedProperties does
    /// (Size = Tell() - DataOffset).
    /// </summary>
    public void WriteTo(UAssetWriter writer, IUAssetNameResolver resolver, int ue4Version)
    {
        writer.WriteFName(resolver.GetNameIndex(Name));
        writer.WriteFName(resolver.GetNameIndex(Type), TypeNumber);

        // BoolProperty stores its value in the header and writes an empty body.
        bool isHeaderBool = Type is UAssetVersions.TypeBoolProperty or "bool";

        long sizePlaceholderPosition = writer.Position;
        writer.WriteInt32(0); // Size placeholder
        writer.WriteInt32(ArrayIndex);
        if (TypeNumber == 0)
        {
            switch (Type)
            {
                case UAssetVersions.TypeStructProperty:
                case "struct":
                    writer.WriteFName(resolver.GetNameIndex(StructName));
                    if (ue4Version >= UAssetVersions.VerUe4StructGuidInPropertyTag)
                    {
                        writer.WriteGuid(StructGuid);
                    }
                    break;
                case UAssetVersions.TypeBoolProperty:
                case "bool":
                    bool headerBool = Value is { Kind: UPropertyValueKind.Bool } boolValue
                        ? boolValue.Bool
                        : BoolVal != 0;
                    writer.WriteByte(headerBool ? (byte)1 : (byte)0);
                    break;
                case UAssetVersions.TypeByteProperty:
                case "byte":
                case UAssetVersions.TypeEnumProperty:
                case "enum":
                    writer.WriteFName(resolver.GetNameIndex(EnumName));
                    break;
                case UAssetVersions.TypeArrayProperty:
                case "array":
                    if (ue4Version >= UAssetVersions.VerUe4ArrayPropertyInnerTags)
                    {
                        writer.WriteFName(resolver.GetNameIndex(InnerType));
                    }
                    break;
                case "SetProperty":
                case "set":
                    if (ue4Version >= UAssetVersions.VerUe4PropertyTagSetMap)
                    {
                        writer.WriteFName(resolver.GetNameIndex(InnerType));
                    }
                    break;
                case "MapProperty":
                case "map":
                    if (ue4Version >= UAssetVersions.VerUe4PropertyTagSetMap)
                    {
                        writer.WriteFName(resolver.GetNameIndex(InnerType));
                        writer.WriteFName(resolver.GetNameIndex(ValueType));
                    }
                    break;
            }
        }

        if (ue4Version >= UAssetVersions.VerUe4PropertyGuidInPropertyTag)
        {
            writer.WriteByte(HasPropertyGuid ? (byte)1 : (byte)0);
            if (HasPropertyGuid)
            {
                writer.WriteGuid(PropertyGuid);
            }
        }

        if (isHeaderBool)
        {
            // Body is empty; Size stays 0 (see PropertyTag.cpp: BoolProperty
            // writes its value in the header's BoolVal byte).
            writer.WriteInt32At(sizePlaceholderPosition, 0);
            return;
        }

        // Buffer the value body so the header's Size field is exact.
        long bodyStart = writer.Position;
        using (MemoryStream bodyStream = new())
        {
            UAssetWriter bodyWriter = new(bodyStream);
            if (Value is not null)
            {
                Value.WriteValue(bodyWriter, resolver, ue4Version, InnerType, StructName);
            }

            writer.WriteBytes(bodyStream.ToArray());
        }

        int bodySize = (int)(writer.Position - bodyStart);
        writer.WriteInt32At(sizePlaceholderPosition, bodySize);
    }
}
