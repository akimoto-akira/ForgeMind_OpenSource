// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// Runtime value of a tagged property. One object tree mirrors the property
/// values laid out in the package object data section. Read side follows
/// UAssetAPI's MainSerializer registry approach: the property type name maps
/// to a typed reader via <see cref="UPropertyValueReaders"/>.
/// </summary>
public sealed class UPropertyValue
{
    public UPropertyValueKind Kind { get; }

    public long Int64 { get; }

    public ulong UInt64 { get; }

    public float Float { get; }

    public double Double { get; }

    public bool Bool { get; }

    public byte Byte { get; }

    public string? Str { get; }

    /// <summary>Byte property that carries an enum type name (legacy UEnum).</summary>
    public string? EnumName { get; }

    public int NameIndex { get; }

    public int NameNumber { get; }

    public FPackageIndex ObjectIndex { get; }

    public IReadOnlyList<UPropertyTag> StructFields { get; }

    public string InnerType { get; }

    public IReadOnlyList<UPropertyValue> ArrayItems { get; }

    /// <summary>Raw bytes for value bodies not modelled as tagged properties (e.g. Guid, TextProperty history).</summary>
    public byte[]? RawData { get; }

    /// <summary>FSoftObjectPath: asset (long) pathname and sub-path.</summary>
    public string? SoftAssetPath { get; }

    public string? SoftSubPath { get; }

    /// <summary>FScriptDelegate object reference.</summary>
    public FPackageIndex DelegateObject { get; }

    public int DelegateFunctionNameIndex { get; }

    public int DelegateFunctionNameNumber { get; }

    public int NumKeysToRemove { get; }

    public IReadOnlyList<UMapEntry> MapEntries { get; }

    public IReadOnlyList<UPropertyTag> SetEntries { get; }

    private UPropertyValue(UPropertyValueKind kind, long int64, ulong uint64, float f,
        double d, bool b, byte by, string? str, string? enumName, int nameIndex,
        int nameNumber, FPackageIndex objectIndex, IReadOnlyList<UPropertyTag>? structFields,
        string innerType, IReadOnlyList<UPropertyValue>? arrayItems, byte[]? rawData = null,
        string? softAssetPath = null, string? softSubPath = null,
        FPackageIndex delegateObject = default, int delegateFnIndex = 0, int delegateFnNumber = 0,
        int numKeysToRemove = 0, IReadOnlyList<UMapEntry>? mapEntries = null,
        IReadOnlyList<UPropertyTag>? setEntries = null)
    {
        Kind = kind;
        Int64 = int64;
        UInt64 = uint64;
        Float = f;
        Double = d;
        Bool = b;
        Byte = by;
        Str = str;
        EnumName = enumName;
        NameIndex = nameIndex;
        NameNumber = nameNumber;
        ObjectIndex = objectIndex;
        StructFields = structFields ?? System.Array.Empty<UPropertyTag>();
        InnerType = innerType;
        ArrayItems = arrayItems ?? System.Array.Empty<UPropertyValue>();
        RawData = rawData;
        SoftAssetPath = softAssetPath;
        SoftSubPath = softSubPath;
        DelegateObject = delegateObject;
        DelegateFunctionNameIndex = delegateFnIndex;
        DelegateFunctionNameNumber = delegateFnNumber;
        NumKeysToRemove = numKeysToRemove;
        MapEntries = mapEntries ?? System.Array.Empty<UMapEntry>();
        SetEntries = setEntries ?? System.Array.Empty<UPropertyTag>();
    }

    public static UPropertyValue Integer(long value) => new(UPropertyValueKind.Int32, value, 0, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue UnsignedInteger(ulong value) => new(UPropertyValueKind.UInt32, 0, value, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue LongInteger(long value) => new(UPropertyValueKind.Int64, value, 0, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue UnsignedLongInteger(ulong value) => new(UPropertyValueKind.UInt64, 0, value, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue Floating(float value) => new(UPropertyValueKind.Float, 0, 0, value, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue Doubling(double value) => new(UPropertyValueKind.Double, 0, 0, 0, value, false, 0, null, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue Boolean(bool value) => new(UPropertyValueKind.Bool, 0, 0, 0, 0, value, 0, null, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue ByteValue(byte value, string? enumName) => new(UPropertyValueKind.Byte, 0, 0, 0, 0, false, value, null, enumName, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue EnumValue(long value, string enumName) => new(UPropertyValueKind.Enum, value, 0, 0, 0, false, 0, null, enumName, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue Name(int index, int number = 0) => new(UPropertyValueKind.Name, 0, 0, 0, 0, false, 0, null, null, index, number, default, null, string.Empty, null);

    public static UPropertyValue String(string value) => new(UPropertyValueKind.Str, 0, 0, 0, 0, false, 0, value, null, 0, 0, default, null, string.Empty, null);

    public static UPropertyValue Object(FPackageIndex index) => new(UPropertyValueKind.Object, 0, 0, 0, 0, false, 0, null, null, 0, 0, index, null, string.Empty, null);

    public static UPropertyValue Struct(IReadOnlyList<UPropertyTag> fields) => new(UPropertyValueKind.Struct, 0, 0, 0, 0, false, 0, null, null, 0, 0, default, fields, string.Empty, null);

    public static UPropertyValue Array(string innerType, IReadOnlyList<UPropertyValue> items) => new(UPropertyValueKind.Array, 0, 0, 0, 0, false, 0, null, null, 0, 0, default, null, innerType, items);

    /// <summary>FSoftObjectPath (SoftObject/SoftClass/AssetObject/AssetClass Property).</summary>
    public static UPropertyValue SoftObject(string assetPath, string subPath) => new(UPropertyValueKind.SoftObject, 0, 0, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null, null, assetPath, subPath);

    /// <summary>FScriptDelegate (MulticastDelegateProperty / DelegateProperty).</summary>
    public static UPropertyValue Delegate(FPackageIndex objectIndex, int fnNameIndex, int fnNameNumber) => new(UPropertyValueKind.Delegate, 0, 0, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null, null, null, null, objectIndex, fnNameIndex, fnNameNumber);

    /// <summary>MapProperty value body (per-entry key/value tagged properties).</summary>
    public static UPropertyValue Map(int numKeysToRemove, IReadOnlyList<UMapEntry> entries) => new(UPropertyValueKind.Map, 0, 0, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null, null, null, null, default, 0, 0, numKeysToRemove, entries);

    /// <summary>SetProperty value body (per-entry tagged properties).</summary>
    public static UPropertyValue Set(int numKeysToRemove, IReadOnlyList<UPropertyTag> entries) => new(UPropertyValueKind.Set, 0, 0, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null, null, null, null, default, 0, 0, numKeysToRemove, null, entries);

    /// <summary>Wraps raw bytes for value bodies this module models opaquely (e.g. Guid, TextProperty history).</summary>
    public static UPropertyValue RawBytes(byte[] bytes) => new((UPropertyValueKind)(-1), 0, 0, 0, 0, false, 0, null, null, 0, 0, default, null, string.Empty, null, bytes);

    // ---- Write side (Save use: value-body 1:1 writeback matching reader) ----

    /// <summary>
    /// Serializes this value body. <paramref name="innerType"/> / <paramref
    /// name="structName"/> carry the context set by the owning tag header.
    /// BoolProperty bodies are handled by the tag header (empty body here).
    /// </summary>
    internal void WriteValue(UAssetWriter writer, IUAssetNameResolver resolver, int ue4Version,
        string innerType = "", string structName = "")
    {
        switch (Kind)
        {
            case UPropertyValueKind.Int32:
                writer.WriteInt32((int)Int64);
                break;
            case UPropertyValueKind.UInt32:
                writer.WriteUInt32((uint)UInt64);
                break;
            case UPropertyValueKind.Int64:
                writer.WriteInt64(Int64);
                break;
            case UPropertyValueKind.UInt64:
                writer.WriteUInt64(UInt64);
                break;
            case UPropertyValueKind.Float:
                writer.WriteFloat(Float);
                break;
            case UPropertyValueKind.Double:
                writer.WriteDouble(Double);
                break;
            case UPropertyValueKind.Bool:
                writer.WriteByte(Bool ? (byte)1 : (byte)0);
                break;
            case UPropertyValueKind.Byte:
                writer.WriteByte(Byte);
                break;
            case UPropertyValueKind.Enum:
                writer.WriteInt64(Int64);
                break;
            case UPropertyValueKind.Name:
                writer.WriteFName(NameIndex, NameNumber);
                break;
            case UPropertyValueKind.Str:
                writer.WriteFString(Str ?? string.Empty);
                break;
            case UPropertyValueKind.Object:
                writer.WriteInt32(ObjectIndex.Raw);
                break;
            case UPropertyValueKind.SoftObject:
                writer.WriteFString(SoftAssetPath ?? string.Empty);
                writer.WriteFString(SoftSubPath ?? string.Empty);
                break;
            case UPropertyValueKind.Delegate:
                writer.WriteInt32(DelegateObject.Raw);
                writer.WriteFName(DelegateFunctionNameIndex, DelegateFunctionNameNumber);
                break;
            case UPropertyValueKind.Struct:
                if (RawData is not null)
                {
                    writer.WriteBytes(RawData);
                    break;
                }

                foreach (UPropertyTag field in StructFields)
                {
                    field.WriteTo(writer, resolver, ue4Version);
                }

                writer.WriteFName(resolver.GetNameIndex("None"));
                break;
            case UPropertyValueKind.Array:
                writer.WriteInt32(ArrayItems.Count);
                foreach (UPropertyValue item in ArrayItems)
                {
                    item.WriteValue(writer, resolver, ue4Version, innerType: InnerType);
                }

                break;
            case UPropertyValueKind.Map:
                writer.WriteInt32(NumKeysToRemove);
                writer.WriteInt32(MapEntries.Count);
                foreach (UMapEntry entry in MapEntries)
                {
                    entry.KeyTag.WriteTo(writer, resolver, ue4Version);
                    entry.ValueTag.WriteTo(writer, resolver, ue4Version);
                }

                break;
            case UPropertyValueKind.Set:
                writer.WriteInt32(NumKeysToRemove);
                writer.WriteInt32(SetEntries.Count);
                foreach (UPropertyTag entry in SetEntries)
                {
                    entry.WriteTo(writer, resolver, ue4Version);
                }

                break;
            default:
                if (RawData is not null)
                {
                    writer.WriteBytes(RawData);
                    return;
                }

                throw new NotSupportedException($"Cannot serialize property value of kind '{Kind}'.");
        }
    }

    // ---- Read side (UAssetAPI MainSerializer principle: type name -> registry reader) ----

    /// <summary>
    /// Deserializes a property value by its UE type name (used both for tagged
    /// property values and array elements, which carry no tags themselves).
    /// Dispatches through <see cref="UPropertyValueReaders"/> so the supported
    /// type set is data-driven and extensible.
    /// </summary>
    internal static UPropertyValue ReadValue(UAssetReader reader, string typeName, string? enumName,
        IUAssetNameResolver resolver, int ue4Version)
    {
        return UPropertyValueReaders.ReadValue(reader, typeName, enumName, resolver, ue4Version);
    }

    /// <summary>
    /// Reads an array value: an int32 element count followed by the raw
    /// elements (no tags), typed by <paramref name="innerType"/>.
    /// </summary>
    internal static UPropertyValue ReadArrayValue(UAssetReader reader, string innerType,
        IUAssetNameResolver resolver, int ue4Version)
    {
        int count = reader.ReadInt32();
        List<UPropertyValue> items = new(count);
        for (int i = 0; i < count; i++)
        {
            items.Add(ReadValue(reader, innerType, null, resolver, ue4Version));
        }

        return Array(innerType, items);
    }

    /// <summary>
    /// Reads nested struct properties until the None-name terminator tag.
    /// </summary>
    internal static List<UPropertyTag> ReadStructFields(UAssetReader reader,
        IUAssetNameResolver resolver, int ue4Version)
    {
        List<UPropertyTag> fields = new();
        while (true)
        {
            UPropertyTag tag = UPropertyTag.ReadFrom(reader, resolver, ue4Version);
            if (tag.IsNone)
            {
                break;
            }

            fields.Add(tag);
        }

        return fields;
    }
}
