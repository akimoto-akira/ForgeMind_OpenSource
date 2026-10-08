// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// UAsset module types live in the root namespace ForgeMindServer (folder does not map to namespace)

using ForgeMind.Assets;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// Asset type filter: decides whether a .uasset belongs to a type that needs indexing.
/// <para>
/// The default implementation <see cref="BlueprintOnlyFilter"/> keeps only blueprint-class assets;
/// non-blueprint types (textures, materials, animation sequences, audio, etc.) are skipped to avoid memory blowup.
/// Extend by implementing this interface to support other asset types in the future.
/// </para>
/// </summary>
internal interface IAssetTypeFilter
{
    /// <summary>
    /// Determines whether the given export-class list means the asset belongs to a type that needs indexing.
    /// </summary>
    /// <param name="exportClassNames">Set of all export-object class names for this uasset (nulls already removed).</param>
    /// <returns>true = needs indexing; false = skip.</returns>
    bool ShouldIndex(IEnumerable<string> exportClassNames);
}

/// <summary>
/// Default filter: keeps only blueprint-class assets.
/// <para>
/// Criterion: the export table contains any export object whose class name contains "BlueprintGeneratedClass".
/// This covers standard Blueprints, Widget Blueprints, Animation Blueprints, and all other BlueprintGeneratedClass variants.
/// </para>
/// </summary>
internal sealed class BlueprintOnlyFilter : IAssetTypeFilter
{
    public bool ShouldIndex(IEnumerable<string> exportClassNames)
    {
        foreach (string className in exportClassNames)
        {
            if (className.Contains("BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// uasset asset scanner: deserializes .uasset package summary and export objects, yielding index entries from the "asset" source.
/// Single-file parse failure does not halt scanning (skipped and counted, for diagnostics).
/// <para>
/// By default only blueprint-class assets are indexed (<see cref="BlueprintOnlyFilter"/>); non-blueprint types are skipped to avoid memory blowup.
/// Replace via <see cref="ActiveFilter"/> to support more asset types.
/// </para>
/// </summary>
internal static class UAssetScanner
{
    private static readonly string[] SkippedDirNames =
    [
        "Binaries", "Intermediate", "Saved", "DerivedDataCache", ".git", "node_modules",
    ];

    /// <summary>
    /// Currently active asset type filter (default <see cref="BlueprintOnlyFilter"/>).
    /// Replace this field to support more asset types — no scanner modifications needed.
    /// </summary>
    public static IAssetTypeFilter ActiveFilter { get; set; } = new BlueprintOnlyFilter();

    /// <summary>Scans all .uasset files in the directory; the parse-failure count is reported via <paramref name="failedCount"/> (for diagnostics).</summary>
    public static List<UeAssetEntry> Scan(string directory, ScopeLevel scope, out int failedCount)
    {
        var entries = new List<UeAssetEntry>();
        failedCount = 0;
        if (!Directory.Exists(directory))
            return entries;

        foreach (string file in Directory.EnumerateFiles(directory, "*.uasset", SearchOption.AllDirectories))
        {
            // Skip intermediate product directories (one-level parent judgment, e.g. Intermediate/xxx.uasset)
            string? parent = Path.GetDirectoryName(Path.GetDirectoryName(file));
            if (parent is not null &&
                SkippedDirNames.Contains(Path.GetFileName(parent), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            UeAssetEntry? entry = TryLoad(file, scope);
            if (entry is null)
            {
                failedCount++;
                continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static UeAssetEntry? TryLoad(string filePath, ScopeLevel scope)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            UAssetPackage package = UAssetSerializer.Load(stream);

            // First collect all export object class names for type filtering
            var exportClassNames = new List<string>(package.Exports.Count);
            foreach (ObjectExport export in package.Exports)
            {
                string? className = null;
                if (export.ClassIndex.IsImport)
                {
                    int importIndex = export.ClassIndex.ToImportIndex();
                    if (importIndex >= 0 && importIndex < package.Imports.Count)
                        className = package.Imports[importIndex].ObjectName;
                }

                if (className is not null)
                    exportClassNames.Add(className);
            }

            // Type filter: non-target types skipped directly (no entry produced, no memory used)
            if (!ActiveFilter.ShouldIndex(exportClassNames))
                return null;

            var entry = new UeAssetEntry
            {
                FilePath = filePath,
                Name = Path.GetFileNameWithoutExtension(filePath),
                Scope = scope,
                FileVersionUe4 = package.Summary.FileVersionUe4,
                FileVersionUe5 = package.Summary.FileVersionUe5,
                ExportCount = package.Exports.Count,
            };

            foreach (ObjectExport export in package.Exports)
            {
                string? className = null;
                if (export.ClassIndex.IsImport)
                {
                    int importIndex = export.ClassIndex.ToImportIndex();
                    if (importIndex >= 0 && importIndex < package.Imports.Count)
                        className = package.Imports[importIndex].ObjectName;
                }

                entry.Exports.Add(new UeAssetExport
                {
                    ObjectName = export.ObjectName,
                    ClassName = className,
                });
            }

            return entry;
        }
        catch
        {
            return null; // Single-file failure skipped (non-UE package / encrypted / truncated, etc.)
        }
    }
}
