// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.IO;
using System.Text.Json;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.Assets;

/// <summary>
/// Offline asset reading tool.
/// <para>Reads asset binaries straight from disk — no engine runtime and no editor
/// connection. Which engine dialect is used to read a file is decided by its engine
/// version (<see cref="EngineVersion"/>), so the tool name stays engine-agnostic
/// ("asset"); a file that matches no supported engine is rejected up front by its
/// package tag.</para>
/// <para>Supported actions:</para>
/// <list type="bullet">
/// <item><c>info</c> — package summary (name, versions, engine version, import/export counts)</item>
/// <item><c>imports</c> — import table (external classes / packages depended on)</item>
/// <item><c>exports</c> — export table (object names, class names, properties)</item>
/// <item><c>properties</c> — tagged properties of a specific export</item>
/// <item><c>graph</c> — restore a graph offline (nodes, pins and their connections)</item>
/// <item><c>read_ir</c> — read the asset IR offline, JSON identical to the bridge
/// <c>get_asset_ir</c>, with a <c>level</c> parameter (summary / graph / full)</item>
/// </list>
/// </summary>
public class AssetTool : ITool
{
    private readonly ILogger _logger = LogManager.Instance.GetLogger<AssetTool>();
    private readonly ServerPermissionChecker _permChecker;

    internal AssetTool(ServerPermissionChecker permChecker) => _permChecker = permChecker;

    /// <summary>Engine-agnostic tool name.</summary>
    public string Name => "asset";

    public string Description =>
        "Read engine asset files offline, without a running editor. " +
        "Actions: 'info' (package summary: versions, engine version, import/export counts), " +
        "'imports' (list import table entries), " +
        "'exports' (list export table entries with object names and class names), " +
        "'properties' (read tagged properties of a specific export, pass export_index), " +
        "'graph' (restore a graph offline: nodes with pins and their connections; " +
        "optional graph_name, defaults to the main event graph), " +
        "'read_ir' (read the asset IR offline with the SAME JSON structure as bridge get_asset_ir; " +
        "optional level: summary/graph/full, defaults to summary; optional graph_name). " +
        "Requires the 'path' parameter pointing to an asset file. " +
        "The engine used to read the file is decided by its engine version; files matching " +
        "no supported engine are rejected immediately.";

    public string GetDisplayName() => "Read Asset Offline";

    public Dictionary<string, object> GetParameterSchema()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["action"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The action to perform: info, imports, exports, properties, graph, read_ir",
                    ["enum"] = new[] { "info", "imports", "exports", "properties", "graph", "read_ir" }
                },
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Absolute path to the .uasset file"
                },
                ["export_index"] = new Dictionary<string, object>
                {
                    ["type"] = "integer",
                    ["description"] = "Index into the export table (0-based) for the 'properties' action"
                },
                ["graph_name"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Graph name for the 'graph' / 'read_ir' action (defaults to EventGraph)"
                },
                ["level"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "IR detail level for the 'read_ir' action: summary (default) / graph / full",
                    ["enum"] = new[] { "summary", "graph", "full" }
                }
            },
            ["required"] = new[] { "action", "path" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        string? permError = _permChecker.RequirePermission(Name);
        if (permError != null)
            return ToolResult.Failed(permError);

        if (!parameters.TryGetValue("action", out object? actionObj))
            return ToolResult.Failed("Missing 'action' parameter");

        if (!parameters.TryGetValue("path", out object? pathObj) ||
            string.IsNullOrWhiteSpace(pathObj?.ToString()))
        {
            return ToolResult.Failed("Missing 'path' parameter");
        }

        string action = actionObj?.ToString()?.ToLowerInvariant() ?? "";
        string path = pathObj.ToString()!;

        if (!File.Exists(path))
            return ToolResult.Failed($"File does not exist: {path}");

        // Engine-agnostic pre-check: the package file tag decides whether this is a UE asset
        if (!IsUnrealAsset(path))
        {
            return ToolResult.Failed(
                $"Not an Unreal Engine asset: '{path}'. " +
                "The file does not start with the UE package tag (0x9E2A83C1). " +
                "This tool currently only supports Unreal Engine .uasset files.");
        }

        // Loaded once — version routing and every action share this package
        UAssetPackage package;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            package = UAssetSerializer.Load(stream);
        }
        catch (InvalidDataException ex)
        {
            return ToolResult.Failed($"Failed to parse asset: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error(null, "[Asset] Failed to load '{0}': {1}", path, ex.Message);
            return ToolResult.Failed($"Failed to load asset '{path}': {ex.Message}");
        }

        // Route by engine version: one branch per supported version. All versions share
        // the same implementation today; when a version diverges, rewrite its case.
        // Unknown or not-yet-listed versions fall to default and keep working.
        EngineVersion? engineVersion = ResolveEngineVersion(package);
        return engineVersion switch
        {
            EngineVersion.UE_5_0 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_1 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_2 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_3 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_4 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_5 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_6 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_7 => RunAsset(action, path, package, parameters),
            EngineVersion.UE_5_8 => RunAsset(action, path, package, parameters),
            _ => RunAsset(action, path, package, parameters)
        };
    }

    /// <summary>
    /// Resolves the engine version recorded in the asset package summary.
    /// Returns null when that version is not one of the listed ones.
    /// </summary>
    private static EngineVersion? ResolveEngineVersion(UAssetPackage package)
    {
        (int major, int minor, _, _, _) = package.Summary.SavedByEngineVersion;
        return EngineVersions.FromAssociation($"{major}.{minor}");
    }

    /// <summary>
    /// Action dispatch shared by every version. When one version needs different
    /// behavior, rewrite the corresponding case in <see cref="Execute"/>.
    /// </summary>
    private static ToolResult RunAsset(
        string action, string path, UAssetPackage package, Dictionary<string, object> parameters) =>
        action switch
        {
            "info" => ReadInfo(path, package),
            "imports" => ReadImports(package),
            "exports" => ReadExports(package),
            "properties" => ReadProperties(parameters, package),
            "graph" => ReadGraph(parameters, package),
            "read_ir" => ReadIR(parameters, package),
            _ => ToolResult.Failed($"Unknown action: {action}")
        };

    /// <summary>
    /// Decides whether a file is an Unreal Engine asset by its package file tag.
    /// UE .uasset files start with PACKAGE_FILE_TAG (0x9E2A83C1) — a 4-byte
    /// little-endian integer whose byte sequence is C1 83 2A 9E. The byteswapped
    /// form (0xC1832A9E) is accepted as well.
    /// </summary>
    private static bool IsUnrealAsset(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 4)
                return false;

            using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            uint tag = reader.ReadUInt32();
            return tag == UAssetVersions.PackageFileTag ||
                   tag == UAssetVersions.PackageFileTagSwapped;
        }
        catch
        {
            return false;
        }
    }

    // ===== action: graph =====

    /// <summary>
    /// 离线还原蓝图图表的节点与连线（不经编辑器）：解析 UEdGraphNode 的 Pins
    /// 内联序列化，见 <see cref="BlueprintGraphReader"/>。按引擎版本分派。
    /// </summary>
    private static ToolResult ReadGraph(Dictionary<string, object> parameters, UAssetPackage package)
    {
        string? graphName = null;
        if (parameters.TryGetValue("graph_name", out object? graphNameObj))
        {
            graphName = graphNameObj is JsonElement ge && ge.ValueKind == JsonValueKind.String
                ? ge.GetString() : graphNameObj?.ToString();
        }

        try
        {
            object result = BlueprintGraphReader.Read(package, graphName, "full");
            return ToolResult.Successful("Blueprint graph read offline.", result);
        }
        catch (NotSupportedException ex)
        {
            return ToolResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            return ToolResult.Failed($"Failed to read graph: {ex.Message}");
        }
    }

    // ===== action: read_ir =====

    /// <summary>
    /// 离线读取蓝图 IR，输出与桥端 get_asset_ir 完全一致的 JSON 结构。
    /// 支持 level 参数（summary/graph/full），用于字节级精确比对。
    /// </summary>
    private static ToolResult ReadIR(Dictionary<string, object> parameters, UAssetPackage package)
    {
        string? graphName = null;
        if (parameters.TryGetValue("graph_name", out object? graphNameObj))
        {
            graphName = graphNameObj is JsonElement ge && ge.ValueKind == JsonValueKind.String
                ? ge.GetString() : graphNameObj?.ToString();
        }

        string level = "summary";
        if (parameters.TryGetValue("level", out object? levelObj))
        {
            string? l = levelObj is JsonElement le && le.ValueKind == JsonValueKind.String
                ? le.GetString() : levelObj?.ToString();
            if (l == "summary" || l == "graph" || l == "full")
            {
                level = l!;
            }
        }

        try
        {
            object definition = BlueprintGraphReader.Read(package, graphName, level);
            // 输出与桥端 get_asset_ir 完全一致的结构，使字节比对可行。
            var (major, minor, _, _, _) = package.Summary.SavedByEngineVersion;
            var result = new Dictionary<string, object?>
            {
                ["version"] = $"{major}.{minor}",
                ["assetType"] = "blueprint",
                ["mode"] = "read",
                ["level"] = level,
                ["definition"] = definition,
                ["success"] = true,
            };
            return ToolResult.Successful($"Blueprint IR read offline (level={level}).", result);
        }
        catch (NotSupportedException ex)
        {
            return ToolResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            return ToolResult.Failed($"Failed to read IR: {ex.Message}");
        }
    }

    // ===== action: info =====

    private static ToolResult ReadInfo(string path, UAssetPackage package)
    {
        PackageFileSummary s = package.Summary;
        var (major, minor, patch, changelist, branch) = s.SavedByEngineVersion;
        var (compatMajor, compatMinor, compatPatch, compatChangelist, compatBranch) = s.CompatibleWithEngineVersion;

        var data = new
        {
            file = path,
            fileName = Path.GetFileName(path),
            fileSize = new FileInfo(path).Length,
            versions = new
            {
                ue4Version = s.FileVersionUe4,
                ue5Version = s.FileVersionUe5,
                licenseeVersion = s.FileVersionLicenseeUe4,
                legacyFileVersion = UAssetVersions.LegacyFileVersion
            },
            savedByEngineVersion = new
            {
                major,
                minor,
                patch,
                changelist,
                branch
            },
            compatibleWithEngineVersion = new
            {
                major = compatMajor,
                minor = compatMinor,
                patch = compatPatch,
                changelist = compatChangelist,
                branch = compatBranch
            },
            packageGuid = s.Guid.ToString(),
            persistentGuid = s.PersistentGuid.ToString(),
            counts = new
            {
                names = package.Names.Count,
                imports = package.Imports.Count,
                exports = package.Exports.Count,
                gatherableTexts = package.GatherableTexts.Count,
                softPackageReferences = package.SoftPackageReferences.Count,
                customVersions = s.CustomVersions.Count,
                generations = s.Generations.Count
            },
            packageFlags = s.PackageFlags,
            folderName = s.FolderName,
            localizationId = s.LocalizationId,
            totalHeaderSize = s.TotalHeaderSize
        };

        return ToolResult.Successful(
            $"Asset info for '{Path.GetFileName(path)}' (UE4 v{s.FileVersionUe4}, UE5 v{s.FileVersionUe5}, " +
            $"{package.Exports.Count} export(s), {package.Imports.Count} import(s)).",
            data);
    }

    // ===== action: imports =====

    private static ToolResult ReadImports(UAssetPackage package)
    {
        var imports = new List<object>(package.Imports.Count);
        for (int i = 0; i < package.Imports.Count; i++)
        {
            ObjectImport imp = package.Imports[i];
            imports.Add(new
            {
                index = i,
                classPackage = imp.ClassPackage,
                className = imp.ClassName,
                objectName = imp.ObjectName,
                packageName = imp.PackageName,
                outerIndex = imp.OuterIndex.ToString(),
                isOptional = imp.bImportOptional
            });
        }

        return ToolResult.Successful(
            $"{imports.Count} import(s).",
            new { imports });
    }

    // ===== action: exports =====

    private static ToolResult ReadExports(UAssetPackage package)
    {
        var exports = new List<object>(package.Exports.Count);
        for (int i = 0; i < package.Exports.Count; i++)
        {
            ObjectExport exp = package.Exports[i];
            string className = ResolveClassName(package, exp.ClassIndex);
            string outerName = ResolveObjectName(package, exp.OuterIndex);

            var exportInfo = new
            {
                index = i,
                objectName = exp.ObjectName,
                className,
                outerName,
                serialSize = exp.SerialSize,
                serialOffset = exp.SerialOffset,
                isAsset = exp.bIsAsset,
                flags = exp.ObjectFlags,
                hasParsedProperties = exp.Data?.Properties.Count > 0,
                hasRawData = exp.Data?.RawData != null,
                propertyCount = exp.Data?.Properties.Count ?? 0
            };
            exports.Add(exportInfo);
        }

        return ToolResult.Successful(
            $"{exports.Count} export(s).",
            new { exports });
    }

    // ===== action: properties =====

    private static ToolResult ReadProperties(Dictionary<string, object> parameters, UAssetPackage package)
    {
        int exportIndex = 0;
        if (parameters.TryGetValue("export_index", out object? idxObj))
        {
            if (idxObj is JsonElement e && e.ValueKind == JsonValueKind.Number)
                exportIndex = e.GetInt32();
            else if (!int.TryParse(idxObj?.ToString(), out exportIndex))
                return ToolResult.Failed($"Invalid export_index: {idxObj}");
        }
        else
        {
            return ToolResult.Failed("Missing 'export_index' parameter for 'properties' action");
        }

        if (exportIndex < 0 || exportIndex >= package.Exports.Count)
            return ToolResult.Failed(
                $"export_index {exportIndex} is out of range (0..{package.Exports.Count - 1})");

        ObjectExport export = package.Exports[exportIndex];
        string className = ResolveClassName(package, export.ClassIndex);

        if (export.Data is null)
            return ToolResult.Successful(
                $"Export '{export.ObjectName}' has no parsed data.",
                new { exportIndex, objectName = export.ObjectName, className, properties = Array.Empty<object>() });

        if (export.Data.Properties.Count > 0)
        {
            var props = new List<object>(export.Data.Properties.Count);
            foreach (UPropertyTag tag in export.Data.Properties)
            {
                props.Add(SerializeProperty(tag, package));
            }

            return ToolResult.Successful(
                $"{props.Count} property/properties on export '{export.ObjectName}' ({className}).",
                new { exportIndex, objectName = export.ObjectName, className, properties = props });
        }

        if (export.Data.RawData is not null)
            return ToolResult.Successful(
                $"Export '{export.ObjectName}' contains opaque raw data ({export.Data.RawData.Length} bytes) " +
                "that could not be parsed as tagged properties.",
                new
                {
                    exportIndex,
                    objectName = export.ObjectName,
                    className,
                    rawSize = export.Data.RawData.Length,
                    properties = Array.Empty<object>()
                });

        return ToolResult.Successful(
            $"Export '{export.ObjectName}' has no parsed data.",
            new { exportIndex, objectName = export.ObjectName, className, properties = Array.Empty<object>() });
    }

    // ===== helpers =====

    /// <summary>
    /// 将 <see cref="FPackageIndex"/> 解析为导入表或导出表中的类名字面量。
    /// </summary>
    private static string ResolveClassName(UAssetPackage package, FPackageIndex index)
    {
        if (index.IsNull)
            return "(null)";
        if (index.IsImport)
        {
            int i = index.ToImportIndex();
            return i >= 0 && i < package.Imports.Count
                ? package.Imports[i].ClassName
                : $"import[{i}]";
        }
        if (index.IsExport)
        {
            int i = index.ToExportIndex();
            return i >= 0 && i < package.Exports.Count
                ? package.Exports[i].ObjectName
                : $"export[{i}]";
        }
        return index.ToString();
    }

    /// <summary>
    /// 将 <see cref="FPackageIndex"/> 解析为导入表或导出表中的对象名字面量。
    /// </summary>
    private static string ResolveObjectName(UAssetPackage package, FPackageIndex index)
    {
        if (index.IsNull)
            return "(null)";
        if (index.IsImport)
        {
            int i = index.ToImportIndex();
            return i >= 0 && i < package.Imports.Count
                ? package.Imports[i].ObjectName
                : $"import[{i}]";
        }
        if (index.IsExport)
        {
            int i = index.ToExportIndex();
            return i >= 0 && i < package.Exports.Count
                ? package.Exports[i].ObjectName
                : $"export[{i}]";
        }
        return index.ToString();
    }

    /// <summary>
    /// 将一个标记属性序列化为 JSON 友好的字典结构。
    /// </summary>
    private static object SerializeProperty(UPropertyTag tag, UAssetPackage package)
    {
        var prop = new Dictionary<string, object?>
        {
            ["name"] = tag.Name,
            ["type"] = tag.Type,
            ["size"] = tag.Size,
            ["arrayIndex"] = tag.ArrayIndex
        };

        if (tag.Type == UAssetVersions.TypeStructProperty)
        {
            prop["structName"] = tag.StructName;
        }

        if (tag.Type is UAssetVersions.TypeByteProperty or UAssetVersions.TypeEnumProperty)
        {
            prop["enumName"] = tag.EnumName;
        }

        if (tag.Type == UAssetVersions.TypeArrayProperty)
        {
            prop["innerType"] = tag.InnerType;
        }

        prop["value"] = SerializeValue(tag.Value, package);
        return prop;
    }

    /// <summary>
    /// 将 <see cref="UPropertyValue"/> 递归序列化为 JSON 友好的结构。
    /// </summary>
    private static object? SerializeValue(UPropertyValue? value, UAssetPackage package)
    {
        if (value is null)
            return null;

        return value.Kind switch
        {
            UPropertyValueKind.Int32 => value.Int64,
            UPropertyValueKind.UInt32 => value.UInt64,
            UPropertyValueKind.Int64 => value.Int64,
            UPropertyValueKind.UInt64 => value.UInt64,
            UPropertyValueKind.Float => (double)value.Float,
            UPropertyValueKind.Double => value.Double,
            UPropertyValueKind.Bool => value.Bool,
            UPropertyValueKind.Byte => value.Byte,
            UPropertyValueKind.Enum => new { value = value.Int64, enumName = value.EnumName },
            UPropertyValueKind.Name => package.GetName(value.NameIndex) +
                                       (value.NameNumber != 0 ? $"_{value.NameNumber}" : ""),
            UPropertyValueKind.Str => value.Str,
            UPropertyValueKind.Object => value.ObjectIndex.ToString(),
            UPropertyValueKind.Struct => value.StructFields
                .Select(f => SerializeProperty(f, package))
                .ToList(),
            UPropertyValueKind.Array => value.ArrayItems
                .Select(item => SerializeValue(item, package))
                .ToList(),
            _ => $"(unsupported kind {value.Kind})"
        };
    }
}
