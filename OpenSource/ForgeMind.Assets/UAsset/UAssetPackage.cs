// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// In-memory model of a uasset package: name table (index 0 is always
/// "None"), import map, export map, gatherable texts, soft package references
/// and the per-export tagged-property payloads. This class is both the save
/// input and the load output of <see cref="UAssetSerializer"/>, and acts as
/// the FName resolver while (de)serializing property tags.
/// </summary>
public sealed class UAssetPackage : IUAssetNameResolver
{
    /// <summary>Package file summary (counts/offsets are maintained by the serializer).</summary>
    public PackageFileSummary Summary { get; } = new();

    private readonly List<string> _names = new() { "None" };

    private readonly Dictionary<string, int> _nameToIndex = new() { ["None"] = 0 };

    public IReadOnlyList<string> Names => _names;

    public List<ObjectImport> Imports { get; } = new();

    public List<ObjectExport> Exports { get; } = new();

    public List<GatherableTextData> GatherableTexts { get; } = new();

    public List<string> SoftPackageReferences { get; } = new();

    /// <summary>
    /// Searchable names map: package index keys with their name lists
    /// (serialized since VER_UE4_ADDED_SEARCHABLE_NAMES).
    /// </summary>
    public List<(FPackageIndex Key, List<(int Index, int Number)> Names)> SearchableNames { get; } = new();

    /// <summary>
    /// Returns the name table index for <paramref name="name"/>, inserting it
    /// when missing (empty strings map to index 0, "None").
    /// </summary>
    public int GetNameIndex(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }

        if (_nameToIndex.TryGetValue(name, out int existing))
        {
            return existing;
        }

        int index = _names.Count;
        _names.Add(name);
        _nameToIndex.Add(name, index);
        return index;
    }

    /// <summary>Returns the literal for <paramref name="index"/>, or indexes into NAME_None for 0.</summary>
    public string GetName(int index)
    {
        if (index == 0)
        {
            return "None";
        }

        if (index < 0 || index >= _names.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Name table index {index} is out of range (table has {_names.Count} entries).");
        }

        return _names[index];
    }

    /// <summary>
    /// Prepares every FName literal used by the summary, imports, exports and
    /// property trees so that Summary.NameCount is final before the header is
    /// written. Called automatically by <see cref="UAssetSerializer.Save"/>.
    /// </summary>
    public void PrepareNames()
    {
        GetNameIndex(Summary.FolderName);
        foreach (ObjectImport import in Imports)
        {
            GetNameIndex(import.ClassPackage);
            GetNameIndex(import.ClassName);
            GetNameIndex(import.ObjectName);
            GetNameIndex(import.PackageName);
        }

        foreach (ObjectExport export in Exports)
        {
            GetNameIndex(export.ObjectName);
            if (export.Data is not null)
            {
                foreach (UPropertyTag tag in export.Data.Properties)
                {
                    PrepareTagNames(tag);
                }
            }
        }
    }

    private void PrepareTagNames(UPropertyTag tag)
    {
        GetNameIndex(tag.Name);
        GetNameIndex(tag.Type);
        if (tag.Type == UAssetVersions.TypeStructProperty)
        {
            GetNameIndex(tag.StructName);
        }
        else if (tag.Type is UAssetVersions.TypeByteProperty or UAssetVersions.TypeEnumProperty)
        {
            GetNameIndex(tag.EnumName);
        }
        else if (tag.Type == UAssetVersions.TypeArrayProperty)
        {
            GetNameIndex(tag.InnerType);
        }

        if (tag.Value is null)
        {
            return;
        }

        if (tag.Value.Kind == UPropertyValueKind.Struct)
        {
            foreach (UPropertyTag field in tag.Value.StructFields)
            {
                PrepareTagNames(field);
            }
        }
        else if (tag.Value.Kind == UPropertyValueKind.Array)
        {
            GetNameIndex(tag.InnerType);
        }
    }

    /// <summary>Adds an export referencing the given class name; returns the new export.</summary>
    public ObjectExport AddExport(string objectName, string className, FPackageIndex outer = default)
    {
        ObjectExport export = new()
        {
            ObjectName = objectName,
            ClassIndex = GetImportIndexForClass(className),
            OuterIndex = outer,
            Data = new ObjectExportData(),
        };
        Exports.Add(export);
        return export;
    }

    private FPackageIndex GetImportIndexForClass(string className)
    {
        // Reuse an existing import with the same class name when possible.
        for (int i = 0; i < Imports.Count; i++)
        {
            if (Imports[i].ClassName == className)
            {
                return FPackageIndex.FromImport(i);
            }
        }

        Imports.Add(new ObjectImport
        {
            ClassPackage = "/Script/CoreUObject",
            ClassName = className,
            ObjectName = className,
            PackageName = "/Script/CoreUObject",
        });
        return FPackageIndex.FromImport(Imports.Count - 1);
    }
}