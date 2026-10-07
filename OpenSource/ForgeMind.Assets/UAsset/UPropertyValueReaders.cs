// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// Data-driven property value readers, following UAssetAPI's MainSerializer
/// registry principle: each serialized property type name maps to a reader
/// delegate, so new property coverage is an additive registration rather than
/// a switch rewrite. Short names used inside K2Node tagged properties
/// ("bool"/"int"/"float"/"name"/"string"/"byte"/"object"/"struct") are
/// registered as aliases of their full-name readers.
/// </summary>
internal static class UPropertyValueReaders
{
    private delegate UPropertyValue ValueReader(
        UAssetReader reader, string? enumName, IUAssetNameResolver resolver, int ue4Version);

    private static readonly Dictionary<string, ValueReader> Readers = BuildRegistry();

    public static UPropertyValue ReadValue(UAssetReader reader, string typeName, string? enumName,
        IUAssetNameResolver resolver, int ue4Version)
    {
        if (Readers.TryGetValue(typeName, out ValueReader? single))
        {
            return single(reader, enumName, resolver, ue4Version);
        }

        throw new NotSupportedException($"Unsupported property type '{typeName}'.");
    }

    private static Dictionary<string, ValueReader> BuildRegistry()
    {
        var registry = new Dictionary<string, ValueReader>(StringComparer.Ordinal);

        void Register(string typeName, ValueReader reader, params string[] aliases)
        {
            registry[typeName] = reader;
            foreach (string alias in aliases)
            {
                registry[alias] = reader;
            }
        }

        Register(UAssetVersions.TypeIntProperty, (r, _, _, _) => UPropertyValue.Integer(r.ReadInt32()), "int");
        Register(UAssetVersions.TypeUInt32Property, (r, _, _, _) => UPropertyValue.UnsignedInteger(r.ReadUInt32()));
        Register(UAssetVersions.TypeInt64Property, (r, _, _, _) => UPropertyValue.LongInteger(r.ReadInt64()));
        Register(UAssetVersions.TypeUInt64Property, (r, _, _, _) => UPropertyValue.UnsignedLongInteger(r.ReadUInt64()));
        Register(UAssetVersions.TypeFloatProperty, (r, _, _, _) => UPropertyValue.Floating(r.ReadFloat()), "float");
        Register(UAssetVersions.TypeDoubleProperty, (r, _, _, _) => UPropertyValue.Doubling(r.ReadDouble()));

        // BoolProperty values live in the tag header (1 byte); this body reader
        // (array elements / legacy forms) reads a single byte, matching the
        // on-disk element size of the tagged bool form.
        Register(UAssetVersions.TypeBoolProperty, (r, _, _, _) => UPropertyValue.Boolean(r.ReadByte() != 0), "bool");

        Register(UAssetVersions.TypeByteProperty, (r, e, _, _) => UPropertyValue.ByteValue(r.ReadByte(), e), "byte");
        Register(UAssetVersions.TypeEnumProperty, (r, e, _, _) => UPropertyValue.EnumValue(r.ReadInt64(), e ?? string.Empty));

        Register(UAssetVersions.TypeNameProperty,
            (r, _, _, _) =>
            {
                (int index, int number) = r.ReadFName();
                return UPropertyValue.Name(index, number);
            }, "name");

        Register(UAssetVersions.TypeStrProperty, (r, _, _, _) => UPropertyValue.String(r.ReadFString()), "string");

        Register(UAssetVersions.TypeObjectProperty,
            (r, _, _, _) => UPropertyValue.Object(new FPackageIndex(r.ReadInt32())), "object");

        // Weak/Lazy object pointers and interface references serialize the same
        // FPackageIndex payload as ObjectProperty.
        Register(UAssetVersions.TypeWeakObjectProperty, (r, _, _, _) => UPropertyValue.Object(new FPackageIndex(r.ReadInt32())));
        Register(UAssetVersions.TypeWeakClassProperty, (r, _, _, _) => UPropertyValue.Object(new FPackageIndex(r.ReadInt32())));
        Register(UAssetVersions.TypeLazyObjectProperty, (r, _, _, _) => UPropertyValue.Object(new FPackageIndex(r.ReadInt32())));
        Register(UAssetVersions.TypeInterfaceProperty, (r, _, _, _) => UPropertyValue.Object(new FPackageIndex(r.ReadInt32())));

        // FSoftObjectPath = FString AssetLongPathname + FString SubPathString.
        Register(UAssetVersions.TypeSoftObjectProperty,
            (r, _, _, _) => UPropertyValue.SoftObject(r.ReadFString(), r.ReadFString()));
        Register(UAssetVersions.TypeSoftClassProperty,
            (r, _, _, _) => UPropertyValue.SoftObject(r.ReadFString(), r.ReadFString()));
        Register(UAssetVersions.TypeAssetObjectProperty,
            (r, _, _, _) => UPropertyValue.SoftObject(r.ReadFString(), r.ReadFString()));
        Register(UAssetVersions.TypeAssetClassProperty,
            (r, _, _, _) => UPropertyValue.SoftObject(r.ReadFString(), r.ReadFString()));

        // FScriptDelegate = UObject* reference + function FName.
        Register(UAssetVersions.TypeMulticastDelegateProperty, ReadScriptDelegate);
        Register(UAssetVersions.TypeDelegateProperty, ReadScriptDelegate);

        // MapProperty / SetProperty value bodies: NumKeysToRemove + entry count,
        // then one full tagged property per entry (key and value for maps). The
        // entries reuse UPropertyTag.ReadFrom so nested headers and value
        // bodies are parsed with the exact same rules as top-level properties.
        Register(UAssetVersions.TypeMapProperty, (r, _, res, v) => ReadMap(r, res, v));
        Register(UAssetVersions.TypeSetProperty, (r, _, res, v) => ReadSet(r, res, v));

        return registry;
    }

    /// <summary>MapProperty body: int32 NumKeysToRemove + int32 count + (key tag, value tag) per entry.</summary>
    private static UPropertyValue ReadMap(UAssetReader reader, IUAssetNameResolver resolver, int ue4Version)
    {
        int numKeysToRemove = reader.ReadInt32();
        int entryCount = reader.ReadInt32();
        List<UMapEntry> entries = new(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            UPropertyTag keyTag = UPropertyTag.ReadFrom(reader, resolver, ue4Version);
            UPropertyTag valueTag = UPropertyTag.ReadFrom(reader, resolver, ue4Version);
            entries.Add(new UMapEntry { KeyTag = keyTag, ValueTag = valueTag });
        }

        return UPropertyValue.Map(numKeysToRemove, entries);
    }

    /// <summary>SetProperty body: int32 NumKeysToRemove + int32 count + one tag per entry.</summary>
    private static UPropertyValue ReadSet(UAssetReader reader, IUAssetNameResolver resolver, int ue4Version)
    {
        int numKeysToRemove = reader.ReadInt32();
        int entryCount = reader.ReadInt32();
        List<UPropertyTag> entries = new(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            entries.Add(UPropertyTag.ReadFrom(reader, resolver, ue4Version));
        }

        return UPropertyValue.Set(numKeysToRemove, entries);
    }

    /// <summary>Reads a FScriptDelegate payload: object index + function FName.</summary>
    private static UPropertyValue ReadScriptDelegate(
        UAssetReader reader, string? enumName, IUAssetNameResolver resolver, int ue4Version)
    {
        FPackageIndex obj = new(reader.ReadInt32());
        (int fnIndex, int fnNumber) = reader.ReadFName();
        return UPropertyValue.Delegate(obj, fnIndex, fnNumber);
    }
}
