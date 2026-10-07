// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// UAsset 模块类型位于根命名空间 ForgeMindServer（文件夹不映射命名空间）

using ForgeMind.Assets;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// 资产类型过滤器：决定一个 .uasset 是否属于需要索引的类型。
/// <para>
/// 默认实现 <see cref="BlueprintOnlyFilter"/> 只保留蓝图类资产，
/// 非蓝图类型（贴图、材质、动画序列、音频等）直接跳过以避免爆内存。
/// 未来可通过实现此接口扩展为支持其他资产类型。
/// </para>
/// </summary>
internal interface IAssetTypeFilter
{
    /// <summary>
    /// 判断给定的导出对象列表是否表示该资产属于需要索引的类型。
    /// </summary>
    /// <param name="exportClassNames">该 uasset 所有导出对象的类名集合（已去 null）。</param>
    /// <returns>true = 需要索引；false = 跳过。</returns>
    bool ShouldIndex(IEnumerable<string> exportClassNames);
}

/// <summary>
/// 默认过滤器：只保留蓝图类资产。
/// <para>
/// 判据：导出表中存在任一类名包含 "BlueprintGeneratedClass" 的导出对象。
/// 这覆盖了标准蓝图、控件蓝图、动画蓝图等所有 BlueprintGeneratedClass 变体。
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
/// uasset 资产扫描器：反序列化 .uasset 的包摘要与导出对象，产出「资产」来源的索引条目。
/// 单文件解析失败不中断扫描（跳过并计数，供诊断）。
/// <para>
/// 默认只索引蓝图类资产（<see cref="BlueprintOnlyFilter"/>），非蓝图类型跳过以避免爆内存。
/// 可通过 <see cref="ActiveFilter"/> 替换为其他过滤器以支持更多资产类型。
/// </para>
/// </summary>
internal static class UAssetScanner
{
    private static readonly string[] SkippedDirNames =
    [
        "Binaries", "Intermediate", "Saved", "DerivedDataCache", ".git", "node_modules",
    ];

    /// <summary>
    /// 当前生效的资产类型过滤器（默认 <see cref="BlueprintOnlyFilter"/>）。
    /// 替换此字段即可扩展为支持其他资产类型——无需修改扫描器本身。
    /// </summary>
    public static IAssetTypeFilter ActiveFilter { get; set; } = new BlueprintOnlyFilter();

    /// <summary>扫描目录下全部 .uasset；解析失败数通过 <paramref name="failedCount"/> 报告（供诊断）。</summary>
    public static List<UeAssetEntry> Scan(string directory, ScopeLevel scope, out int failedCount)
    {
        var entries = new List<UeAssetEntry>();
        failedCount = 0;
        if (!Directory.Exists(directory))
            return entries;

        foreach (string file in Directory.EnumerateFiles(directory, "*.uasset", SearchOption.AllDirectories))
        {
            // 跳过中间产物目录（父目录一级判断，如 Intermediate/xxx.uasset）
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

            // 先收集所有导出对象的类名，用于类型过滤
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

            // 类型过滤：非目标类型直接跳过（不产出条目，不占内存）
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
            return null; // 单文件失败跳过（非 UE 包 / 加密 / 截断等）
        }
    }
}
