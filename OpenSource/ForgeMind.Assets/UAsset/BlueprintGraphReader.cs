// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.IO;

namespace ForgeMind.Assets;

/// <summary>
/// 离线还原蓝图 IR：直接解析 .uasset 二进制，输出与桥端
/// ForgeMindIR_Blueprint.cpp::Read 完全一致的 JSON 结构，
/// 以实现"字节相等"比对。
///
/// 布局依据（UE 5.0.3 源码）：
/// - EdGraph/EdGraphNode.cpp  UEdGraphNode::Serialize
///   → Super::Serialize(tagged properties)
///     其中 NodeGuid 是 StructProperty(Guid)、NodePosX/NodePosY 是 IntProperty
///   → CustomVer(FBlueprintsObjectVersion::GUID) >= EdGraphPinOptimized 时
///     UEdGraphPin::SerializeAsOwningNode(Ar, Pins)
/// - EdGraph/EdGraphPin.cpp   SerializePinArray / SerializePin
///
/// 保存的 .uasset 文件不含 UBlueprint 导出项，只含
/// BlueprintGeneratedClass + EdGraph + K2Node_* 等。
/// summary 信息从 BlueprintGeneratedClass 的 tagged properties 提取。
/// </summary>
public static class BlueprintGraphReader
{
    /// <summary>
    /// 读取蓝图 IR。输出格式与桥端 ForgeMindIR_Blueprint.cpp::Read 完全一致。
    /// level: "summary" | "graph" | "full"（默认 summary）
    /// </summary>
    public static object Read(UAssetPackage package, string? graphName, string level = "summary")
    {
        (int major, int minor, _, _, _) = package.Summary.SavedByEngineVersion;
        string version = $"{major}.{minor}";

        return version switch
        {
            "5.0" => Read50(package, graphName, level),
            "5.1" => throw new NotSupportedException("UE 5.1 图表解析未实现（空位）"),
            "5.2" => throw new NotSupportedException("UE 5.2 图表解析未实现（空位）"),
            "5.3" => throw new NotSupportedException("UE 5.3 图表解析未实现（空位）"),
            "5.4" => throw new NotSupportedException("UE 5.4 图表解析未实现（空位）"),
            "5.5" => throw new NotSupportedException("UE 5.5 图表解析未实现（空位）"),
            "5.6" => throw new NotSupportedException("UE 5.6 图表解析未实现（空位）"),
            "5.7" => throw new NotSupportedException("UE 5.7 图表解析未实现（空位）"),
            "5.8" => throw new NotSupportedException("UE 5.8 图表解析未实现（空位）"),
            _ => throw new NotSupportedException($"不支持的引擎版本 '{version}'（已实现：5.0）"),
        };
    }

    // ─────────────────────────────────────────────────────────────
    // UE 5.0
    // ─────────────────────────────────────────────────────────────

    private static object Read50(UAssetPackage package, string? graphName, string level)
    {
        var definition = new Dictionary<string, object?>();

        // --- summary 级信息 ---
        // 桥端总是输出 members / components / functions（即使为空数组）
        ExtractSummary(package, definition);

        // --- graph/full 级：图节点与连线 ---
        if (level == "graph" || level == "full")
        {
            bool bIncludeCoords = (level == "full");

            // 事件图（UbergraphPages）：桥端 Blueprint->UbergraphPages
            var eventGraphs = new List<object>();
            for (int i = 0; i < package.Exports.Count; i++)
            {
                ObjectExport export = package.Exports[i];
                if (!IsGraphExport(package, i)) continue;

                string graphObjName = export.ObjectName;
                bool isEventGraph = graphObjName == "EventGraph"
                    || graphObjName == "UserConstructionScript";
                if (!isEventGraph) continue;

                if (!string.IsNullOrWhiteSpace(graphName) && graphObjName != graphName)
                    continue;

                eventGraphs.Add(ReadGraph50(package, i, graphObjName, "eventGraph", bIncludeCoords));
            }
            if (eventGraphs.Count > 0)
            {
                definition["eventGraphs"] = eventGraphs;
            }

            // 函数图：在 summary 的 functions 条目中嵌入 graph 子对象
            if (definition.TryGetValue("functions", out object? funcsObj) && funcsObj is List<object> funcs)
            {
                for (int fi = 0; fi < funcs.Count; fi++)
                {
                    if (funcs[fi] is not Dictionary<string, object?> funcEntry) continue;
                    string funcName = funcEntry.TryGetValue("name", out object? n) ? n?.ToString() ?? "" : "";
                    if (string.IsNullOrEmpty(funcName)) continue;

                    // 找到对应的 EdGraph 导出项
                    for (int i = 0; i < package.Exports.Count; i++)
                    {
                        if (!IsGraphExport(package, i)) continue;
                        if (package.Exports[i].ObjectName != funcName) continue;

                        var graphIR = ReadGraph50(package, i, funcName, "function", bIncludeCoords);
                        funcEntry["graph"] = graphIR;
                        break;
                    }
                }
            }
        }

        return definition;
    }

    // ─────────────────────────────────────────────────────────────
    // Summary 提取
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 从 BlueprintGeneratedClass 导出项提取 extends（ParentClass）。
    /// 从 SCS_Node 提取 components。从 EdGraph 提取 functions 列表。
    /// </summary>
    private static void ExtractSummary(UAssetPackage package,
        Dictionary<string, object?> definition)
    {
        // 桥端总是输出 members（即使为空），对应 Blueprint->NewVariables
        // .uasset 中不直接含 UBlueprint 导出项，变量信息保存在
        // BlueprintGeneratedClass 的 tagged properties 中（UTFrac:NewVariables）
        // 暂时输出空数组，后续可从 BGC tagged properties 提取
        definition["members"] = new List<object>();

        // 找 BlueprintGeneratedClass 导出项
        for (int i = 0; i < package.Exports.Count; i++)
        {
            ObjectExport export = package.Exports[i];
            string classObjName = ResolveImportObjectName(package, export.ClassIndex);

            if (classObjName == "BlueprintGeneratedClass" || classObjName == "Blueprint")
            {
                // 从 tagged properties 提取 ParentClass
                ExtractParentClass(package, export, definition);
                break;
            }
        }

        // 提取 components（从 SCS_Node 导出项）
        // 桥端总是输出 components（即使为空）
        var components = new List<object>();
        for (int i = 0; i < package.Exports.Count; i++)
        {
            string classObjName = ResolveImportObjectName(package, package.Exports[i].ClassIndex);
            if (classObjName == "SCS_Node")
            {
                var comp = ExtractSCSNodeComponent(package, package.Exports[i]);
                if (comp != null)
                {
                    components.Add(comp);
                }
            }
        }
        definition["components"] = components;

        // 提取 functions（从 EdGraph 导出项中找函数图）
        // 桥端总是输出 functions（即使为空）
        var functions = new List<object>();
        for (int i = 0; i < package.Exports.Count; i++)
        {
            if (IsGraphExport(package, i))
            {
                string graphName = package.Exports[i].ObjectName;
                // 函数图：不是 EventGraph 也不是 UserConstructionScript
                if (graphName != "EventGraph" && graphName != "UserConstructionScript")
                {
                    var funcEntry = new Dictionary<string, object?>
                    {
                        ["name"] = graphName,
                        ["type"] = "function",
                        ["inputs"] = new List<object>(),
                        ["outputs"] = new List<object>(),
                    };
                    // 提取函数签名（inputs/outputs）
                    ExtractFunctionSignature50(package, i, funcEntry);
                    functions.Add(funcEntry);
                }
            }
        }
        definition["functions"] = functions;
    }

    /// <summary>从 BlueprintGeneratedClass 的 tagged properties 提取 ParentClass。</summary>
    private static void ExtractParentClass(UAssetPackage package, ObjectExport export,
        Dictionary<string, object?> definition)
    {
        byte[]? raw = export.Data?.RawData;
        if (raw is null || raw.Length == 0)
        {
            // 尝试 Properties
            if (export.Data?.Properties != null)
            {
                foreach (var prop in export.Data.Properties)
                {
                    if (prop.Name == "ParentClass" && prop.Value != null
                        && prop.Value.Kind == UPropertyValueKind.Object)
                    {
                        definition["extends"] = ResolveObjectIndex(package, prop.Value.ObjectIndex);
                    }
                }
            }
            return;
        }

        try
        {
            using var stream = new MemoryStream(raw);
            var reader = new UAssetReader(stream);
            int ue4Version = package.Summary.FileVersionUe4;

            while (true)
            {
                (int nameIndex, _) = reader.ReadFName();
                if (nameIndex == 0) break;

                string propName = package.GetName(nameIndex);
                string type = package.GetName(reader.ReadFName().Item1);
                int size = reader.ReadInt32();
                _ = reader.ReadInt32(); // ArrayIndex
                long dataStart = reader.Position;

                switch (type)
                {
                    case "StructProperty":
                        _ = reader.ReadFName();
                        if (ue4Version >= UAssetVersions.VerUe4StructGuidInPropertyTag)
                        {
                            _ = reader.ReadGuid();
                        }
                        break;
                    case "BoolProperty":
                        _ = reader.ReadByte();
                        break;
                    case "ByteProperty":
                    case "EnumProperty":
                        _ = reader.ReadFName();
                        break;
                    case "ArrayProperty":
                        _ = reader.ReadFName();
                        break;
                    case "MapProperty":
                        _ = reader.ReadFName();
                        _ = reader.ReadFName();
                        break;
                }

                if (ue4Version >= UAssetVersions.VerUe4PropertyGuidInPropertyTag)
                {
                    // HasPropertyGuid is uint8 (1 byte), not bool (4 bytes)
                    if (reader.ReadByte() != 0)
                    {
                        _ = reader.ReadGuid();
                    }
                }

                // 提取 ParentClass（ObjectProperty）
                if (propName == "ParentClass" && type == "ObjectProperty" && size >= 4)
                {
                    int objIndex = reader.ReadInt32();
                    definition["extends"] = ResolveObjectIndex(package, new FPackageIndex(objIndex));
                }

                reader.Seek(dataStart + size);
            }
        }
        catch { }
    }

    /// <summary>从 SCS_Node 的 tagged properties 提取组件名和类型。</summary>
    private static object? ExtractSCSNodeComponent(UAssetPackage package, ObjectExport export)
    {
        if (export.Data?.Properties != null && export.Data.Properties.Count > 0)
        {
            string? componentName = null;
            string? componentType = null;
            foreach (var prop in export.Data.Properties)
            {
                if (prop.Name == "VariableName" && prop.Value != null)
                {
                    if (prop.Value.Kind == UPropertyValueKind.Name)
                    {
                        componentName = package.GetName(prop.Value.NameIndex);
                        if (prop.Value.NameNumber != 0)
                            componentName = $"{componentName}_{prop.Value.NameNumber}";
                    }
                }
                if (prop.Name == "ComponentClass" && prop.Value != null
                    && prop.Value.Kind == UPropertyValueKind.Object)
                {
                    componentType = ResolveObjectIndex(package, prop.Value.ObjectIndex);
                }
            }
            if (!string.IsNullOrEmpty(componentName))
            {
                var entry = new Dictionary<string, object?>
                {
                    ["name"] = componentName,
                };
                if (!string.IsNullOrEmpty(componentType))
                {
                    entry["type"] = componentType;
                }
                return entry;
            }
        }

        // 尝试 RawData
        byte[]? raw = export.Data?.RawData;
        if (raw is null || raw.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(raw);
            var reader = new UAssetReader(stream);
            int ue4Version = package.Summary.FileVersionUe4;

            string? compName = null;
            string? compType = null;

            while (true)
            {
                (int nameIndex, _) = reader.ReadFName();
                if (nameIndex == 0) break;

                string propName = package.GetName(nameIndex);
                string type = package.GetName(reader.ReadFName().Item1);
                int size = reader.ReadInt32();
                _ = reader.ReadInt32();
                long dataStart = reader.Position;

                switch (type)
                {
                    case "StructProperty":
                        _ = reader.ReadFName();
                        if (ue4Version >= UAssetVersions.VerUe4StructGuidInPropertyTag)
                        {
                            _ = reader.ReadGuid();
                        }
                        break;
                    case "BoolProperty":
                        _ = reader.ReadByte();
                        break;
                    case "ByteProperty":
                    case "EnumProperty":
                        _ = reader.ReadFName();
                        break;
                    case "ArrayProperty":
                        _ = reader.ReadFName();
                        break;
                    case "MapProperty":
                        _ = reader.ReadFName();
                        _ = reader.ReadFName();
                        break;
                }

                if (ue4Version >= UAssetVersions.VerUe4PropertyGuidInPropertyTag)
                {
                    // HasPropertyGuid is uint8 (1 byte), not bool (4 bytes)
                    if (reader.ReadByte() != 0)
                    {
                        _ = reader.ReadGuid();
                    }
                }

                if (propName == "VariableName" && type == "NameProperty" && size >= 8)
                {
                    (int idx, int num) = reader.ReadFName();
                    if (idx >= 0 && idx < package.Names.Count)
                    {
                        compName = package.GetName(idx);
                        if (num != 0) compName = $"{compName}_{num}";
                    }
                }
                else if (propName == "ComponentClass" && type == "ObjectProperty" && size >= 4)
                {
                    int objIdx = reader.ReadInt32();
                    compType = ResolveObjectIndex(package, new FPackageIndex(objIdx));
                }

                reader.Seek(dataStart + size);
            }

            if (!string.IsNullOrEmpty(compName))
            {
                var entry = new Dictionary<string, object?>
                {
                    ["name"] = compName,
                };
                if (!string.IsNullOrEmpty(compType)) entry["type"] = compType;
                return entry;
            }
        }
        catch { }

        return null;
    }

    /// <summary>解析 FPackageIndex 为对象名（import 的 ObjectName 或 export 的 ObjectName）。</summary>
    private static string ResolveObjectIndex(UAssetPackage package, FPackageIndex index)
    {
        if (index.IsNull) return "(null)";
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

    // ─────────────────────────────────────────────────────────────
    // Graph 读取（节点 + 连线）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 节点元数据（从 tagged properties 提取）。
    /// </summary>
    private sealed class NodeMetadata
    {
        public UAssetGuid? NodeGuid;
        public int PosX;
        public int PosY;
    }

    private struct PinData50
    {
        public string Name;
        public string Direction;
        public string Type;
        public string DefaultValue;
        public bool bHidden;
        public List<(int NodeExportIndex, UAssetGuid PinId)> LinkedTo;
    }

    /// <summary>
    /// 读取一张图的节点与连线，输出与桥端 GraphToIR 一致的格式。
    /// 两遍扫描：第一遍提取节点元数据和引脚，第二遍解析连线。
    /// </summary>
    private static object ReadGraph50(UAssetPackage package, int graphExportIndex,
        string graphName, string graphKind, bool bIncludeCoords)
    {
        var nodes = new List<object>();
        var connections = new List<object>();
        var warnings = new List<string>();

        // (exportIndex, pinGuid) → (nodeId, pinName)
        var pinIndex = new Dictionary<(int Node, UAssetGuid Pin), (string NodeId, string PinName)>();

        // 第一遍：提取节点和引脚
        // 同时收集引脚的 LinkedTo 信息以便第二遍解析连线
        var nodeLinkedTo = new List<(string FromNodeId, string FromPinName, string FromPinDir,
            List<(int TargetExport, UAssetGuid TargetPinId)> Links)>();

        for (int exportIndex = 0; exportIndex < package.Exports.Count; exportIndex++)
        {
            ObjectExport export = package.Exports[exportIndex];
            if (export.OuterIndex.IsExport && export.OuterIndex.ToExportIndex() != graphExportIndex)
            {
                continue;
            }

            if (!IsNodeExport(package, exportIndex))
            {
                continue;
            }

            byte[]? raw = export.Data?.RawData;
            if (raw is null || raw.Length == 0)
            {
                continue;
            }

            string nodeId = $"export[{exportIndex}]";
            string nodeClass = ResolveImportObjectName(package, export.ClassIndex);
            int posX = 0, posY = 0;

            var pins = new List<object>();
            var trace = new List<string>();

            try
            {
                using var stream = new MemoryStream(raw);
                var reader = new UAssetReader(stream);

                // 1) 解析 tagged properties，提取 NodeGuid/NodePosX/NodePosY
                var meta = ExtractNodeMetadata(reader, package, trace);
                if (meta.NodeGuid.HasValue)
                {
                    nodeId = meta.NodeGuid.Value.ToString();
                }
                posX = meta.PosX;
                posY = meta.PosY;

                // 2) SerializePinArray(Pins, OwningNode)
                long pinsOffset = reader.Position;
                int pinCount = reader.ReadInt32();
                trace.Add($"pinsOffset={pinsOffset}/{raw.Length} pinCount={pinCount}");
                for (int p = 0; p < pinCount; p++)
                {
                    // bNullPtr is serialized via Ar << bNullPtr, which is FArchive::operator<<(bool&) = uint32 (4 bytes)
                    bool nullPin = reader.ReadBool();
                    if (nullPin)
                    {
                        continue;
                    }

                    _ = new FPackageIndex(reader.ReadInt32());
                    UAssetGuid pinId = reader.ReadGuid();

                    var pin = ReadPin50(reader, package);

                    // 桥端跳过 bHidden 的引脚
                    if (pin.bHidden)
                    {
                        continue;
                    }

                    // 输出与桥端一致的 pin 格式
                    var pinEntry = new Dictionary<string, object?>
                    {
                        ["name"] = pin.Name,
                        ["direction"] = pin.Direction,
                        ["type"] = pin.Type,
                    };
                    if (bIncludeCoords && !string.IsNullOrEmpty(pin.DefaultValue))
                    {
                        pinEntry["defaultValue"] = pin.DefaultValue;
                    }
                    pins.Add(pinEntry);

                    pinIndex[(exportIndex, pinId)] = (nodeId, pin.Name);

                    // 保存连线信息
                    if (pin.Direction == "output" && pin.LinkedTo.Count > 0)
                    {
                        nodeLinkedTo.Add((nodeId, pin.Name, pin.Direction, pin.LinkedTo));
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"export[{exportIndex}] '{export.ObjectName}': {ex.GetType().Name}: {ex.Message} | {string.Join(", ", trace)}");
            }

            // TODO: 临时输出 trace 用于调试
            if (trace.Count > 0)
            {
                warnings.Add($"export[{exportIndex}] trace: {string.Join(", ", trace)}");
            }

            // 输出与桥端一致的 node 格式
            // 桥端: id = NodeGuid.ToString(), type = GetClass()->GetName(), title = GetNodeTitle(FullTitle)
            // 对于 K2Node，GetClass()->GetName() 就是导入表中 ClassIndex 指向的 ObjectName
            // GetNodeTitle(FullTitle) 对于大多数 K2Node 返回 export.ObjectName
            var nodeEntry = new Dictionary<string, object?>
            {
                ["id"] = nodeId,
                ["type"] = nodeClass,
                ["title"] = export.ObjectName,
            };
            if (bIncludeCoords)
            {
                nodeEntry["posX"] = posX;
                nodeEntry["posY"] = posY;
            }
            nodeEntry["pins"] = pins;
            nodes.Add(nodeEntry);
        }

        // 第二遍：解析连线
        // 桥端: 只处理 output 引脚的 LinkedTo
        foreach (var (fromNodeId, fromPinName, _, links) in nodeLinkedTo)
        {
            foreach (var (targetExport, targetPinId) in links)
            {
                string toNodeId = $"export[{targetExport}]";
                string toPinName = targetPinId.ToString();

                if (pinIndex.TryGetValue((targetExport, targetPinId), out var resolved))
                {
                    toNodeId = resolved.NodeId;
                    toPinName = resolved.PinName;
                }

                connections.Add(new
                {
                    fromNode = fromNodeId,
                    fromPin = fromPinName,
                    toNode = toNodeId,
                    toPin = toPinName,
                });
            }
        }

        // 桥端 GraphToIR 输出: name, kind, nodes, connections
        return new
        {
            name = graphName,
            kind = graphKind,
            nodes,
            connections,
            // TODO: 临时输出 warnings 用于调试，确认正确后移除
            warnings,
        };
    }

    /// <summary>
    /// 解析 UEdGraphNode 的 tagged properties，提取 NodeGuid/NodePosX/NodePosY。
    /// 使用 UPropertyTag.ReadFrom 进行完整解析（header + value），
    /// 确保与 UAssetSerializer 的 tag 解析完全一致，
    /// 避免手写 header 解析因边缘情况导致偏移错误。
    ///
    /// 读完所有 properties 后，reader 位置位于 None 终止符之后，
    /// 即 pins 数据的开始处。
    /// </summary>
    private static NodeMetadata ExtractNodeMetadata(UAssetReader reader,
        UAssetPackage package, List<string>? trace)
    {
        var meta = new NodeMetadata();
        int ue4Version = package.Summary.FileVersionUe4;

        while (true)
        {
            long beforeTag = reader.Position;
            UPropertyTag tag;
            try
            {
                tag = UPropertyTag.ReadFrom(reader, package, ue4Version);
            }
            catch (Exception ex)
            {
                trace?.Add($"ERROR@{beforeTag}: {ex.Message}");
                break;
            }
            if (tag.IsNone)
            {
                break;
            }

            trace?.Add($"{tag.Name}:{tag.Type}[{tag.Size}]");

            // 提取 NodeGuid（StructProperty, StructName="Guid"）
            if (tag.Name == "NodeGuid" && tag.Type == "StructProperty"
                && tag.Value?.RawData is { Length: >= 16 } rawData)
            {
                // Guid 的 raw bytes 是 4 个 uint32 (A, B, C, D)，小端序
                uint a = BitConverter.ToUInt32(rawData, 0);
                uint b = BitConverter.ToUInt32(rawData, 4);
                uint c = BitConverter.ToUInt32(rawData, 8);
                uint d = BitConverter.ToUInt32(rawData, 12);
                meta.NodeGuid = new UAssetGuid(a, b, c, d);
            }
            else if (tag.Name == "NodePosX" && tag.Type == "IntProperty"
                     && tag.Value != null)
            {
                meta.PosX = (int)tag.Value.Int64;
            }
            else if (tag.Name == "NodePosY" && tag.Type == "IntProperty"
                     && tag.Value != null)
            {
                meta.PosY = (int)tag.Value.Int64;
            }
        }

        return meta;
    }

    /// <summary>
    /// UEdGraphPin::Serialize（EdGraphPin.cpp:1659）的字段顺序。
    /// </summary>
    private static PinData50 ReadPin50(UAssetReader reader, UAssetPackage package)
    {
        // UEdGraphPin::Serialize (EdGraphPin.cpp:1659) field order:
        // 1. OwningNode (UObject*, FPackageIndex 4 bytes)
        // 2. PinId (FGuid 16 bytes)
        // 3. PinName (FName 8 bytes, since PinsStoreFName)
        // 4. PinFriendlyName (FText, variable)
        // 5. SourceIndex (int32 4 bytes, since FUE5MainStreamObjectVersion::EdGraphPinSourceIndex)
        // 6. PinToolTip (FString)
        // 7. Direction (EEdGraphPinDirection, 1 byte enum)
        // 8. PinType.Serialize (complex)
        // 9. DefaultValue (FString)
        // 10. AutogeneratedDefaultValue (FString)
        // 11. DefaultObject (UObject*, FPackageIndex 4 bytes)
        // 12. DefaultTextValue (FText)
        // 13. LinkedTo (SerializePinArray)
        // 14. SubPins (SerializePinArray)
        // 15. ParentPin (SerializePin)
        // 16. ReferencePassThroughConnection (SerializePin)
        // 17. PersistentGuid (FGuid 16 bytes)
        // 18. BitField (uint32, contains bHidden etc.)

        _ = new FPackageIndex(reader.ReadInt32());  // OwningNode
        _ = reader.ReadGuid();                        // PinId

        string pinName = ReadName(reader, package);   // PinName

        _ = ReadFText(reader);                        // PinFriendlyName

        // SourceIndex: present when FUE5MainStreamObjectVersion >= EdGraphPinSourceIndex
        // For UE 5.0+ assets (FileVersionUe5 >= 1000), EdGraphPinSourceIndex is always active.
        // The custom version was added before UE 5.0 release.
        if (package.Summary.FileVersionUe5 > 0)
        {
            _ = reader.ReadInt32();                    // SourceIndex
        }

        _ = reader.ReadFString();                     // PinToolTip
        byte direction = reader.ReadByte();           // Direction

        string pinType = ReadPinType50(reader, package);

        string defaultValue = reader.ReadFString();   // DefaultValue
        _ = reader.ReadFString();                     // AutogeneratedDefaultValue
        _ = new FPackageIndex(reader.ReadInt32());     // DefaultObject
        _ = ReadFText(reader);                        // DefaultTextValue

        var linkedTo = new List<(int, UAssetGuid)>();
        int linkCount = reader.ReadInt32();
        for (int i = 0; i < linkCount; i++)
        {
            // bNullPtr via Ar << bNullPtr = uint32 (4 bytes)
            bool nullLink = reader.ReadBool();
            if (nullLink)
            {
                continue;
            }

            var ownerRef = new FPackageIndex(reader.ReadInt32());
            UAssetGuid linkPinId = reader.ReadGuid();

            if (!ownerRef.IsExport)
            {
                continue;
            }

            linkedTo.Add((ownerRef.ToExportIndex(), linkPinId));
        }

        int subPinCount = reader.ReadInt32();
        for (int i = 0; i < subPinCount; i++)
        {
            SkipPinReference(reader);
        }

        SkipPinReference(reader);  // ParentPin
        SkipPinReference(reader);  // ReferencePassThroughConnection
        _ = reader.ReadGuid();     // PersistentGuid
        uint bitField = reader.ReadUInt32();  // BitField (bHidden, bNotConnectable, etc.)

        // Bit 0 = bHidden
        bool bHidden = (bitField & (1u << 0)) != 0;

        string directionText = direction == 0 ? "input" : "output";

        return new PinData50
        {
            Name = pinName,
            Direction = directionText,
            Type = pinType,
            DefaultValue = defaultValue,
            bHidden = bHidden,
            LinkedTo = linkedTo,
        };
    }

    /// <summary>
    /// FEdGraphPinType::Serialize (EdGraphPin.cpp:155) 的字段顺序。
    /// 使用自定义版本 FFrameworkObjectVersion::GUID 和 FReleaseObjectVersion::GUID。
    /// 注意：bool 类型字段 (bIsReference, bIsWeakPointer, bIsConst, bIsUObjectWrapper)
    /// 通过 Ar << boolValue 序列化为 4 字节 uint32 (FArchive::operator<<(bool&))。
    /// FSimpleMemberReference 有自定义 operator<<，直接序列化 MemberParent(4B) + MemberName(8B) + MemberGuid(16B)。
    /// FEdGraphTerminalType 没有 operator<<，用 tagged properties 序列化。
    /// </summary>
    private static string ReadPinType50(UAssetReader reader, UAssetPackage package)
    {
        int ue4Version = package.Summary.FileVersionUe4;

        // PinsStoreFName: PinCategory, PinSubCategory as FName
        string category = ReadName(reader, package);
        string subCategory = ReadName(reader, package);

        // PinSubCategoryObject (UObject*, FPackageIndex 4 bytes)
        FPackageIndex subCategoryObject = new(reader.ReadInt32());

        // ContainerType (EPinContainerType, uint8 1 byte)
        // FFrameworkObjectVersion::EdGraphPinContainerType (always active in UE 5.0)
        byte containerType = reader.ReadByte();
        if (containerType == 3) // EPinContainerType::Map
        {
            // PinValueType: FEdGraphTerminalType via operator<< (NOT tagged properties!)
            // FEdGraphTerminalType::operator<< serializes:
            //   TerminalCategory (FName 8B), TerminalSubCategory (FName 8B),
            //   TerminalSubCategoryObject (UObject* 4B),
            //   bTerminalIsConst (bool 4B), bTerminalIsWeakPointer (bool 4B),
            //   bTerminalIsUObjectWrapper (bool 4B, since PinTypeIncludesUObjectWrapperFlag)
            SkipTerminalType(reader);
        }

        // bIsReferenceBool (bool, 4 bytes via Ar << bool)
        _ = reader.ReadBool();
        // bIsWeakPointerBool (bool, 4 bytes)
        _ = reader.ReadBool();

        // PinSubCategoryMemberReference (FSimpleMemberReference, custom operator<<)
        // MemberParent (UObject* → FPackageIndex 4 bytes)
        _ = new FPackageIndex(reader.ReadInt32());
        // MemberName (FName 8 bytes)
        _ = ReadName(reader, package);
        // MemberGuid (FGuid 16 bytes)
        _ = reader.ReadGuid();

        // bIsConstBool (bool, 4 bytes, VER_UE4_SERIALIZE_PINTYPE_CONST)
        _ = reader.ReadBool();

        // bIsUObjectWrapperBool (bool, 4 bytes, PinTypeIncludesUObjectWrapperFlag)
        _ = reader.ReadBool();

        // 将 PinType 转换为桥端 PinTypeToText 格式
        return PinTypeToText(category, subCategory, subCategoryObject, containerType, package);
    }

    /// <summary>
    /// 将离线读取的 FEdGraphPinType 字段转换为桥端
    /// ForgeMindBlueprintUtils::PinTypeToText 的输出格式。
    ///
    /// 桥端逻辑 (ForgeMindBlueprintUtils.cpp:862):
    /// - PC_Boolean → "bool"
    /// - PC_Byte → "byte" 或 "byte&lt;EnumName&gt;"
    /// - PC_Int → "int"
    /// - PC_Int64 → "int64"
    /// - PC_Real → 根据子类别 "float" 或 "double"
    /// - PC_Name → "name"
    /// - PC_String → "string"
    /// - PC_Text → "text"
    /// - PC_Struct → "vector"/"rotator"/"transform"/"linearcolor"/"struct&lt;Name&gt;"
    /// - PC_Object → 对象类名或 "object"
    /// - 其他 → category.ToString()
    ///
    /// 容器类型后缀:
    /// - Array → "Base[]"
    /// - Set → "set&lt;Base&gt;"
    /// - Map → "map&lt;Key,Value&gt;"
    /// </summary>
    private static string PinTypeToText(string category, string subCategory,
        FPackageIndex subCategoryObject, byte containerType, UAssetPackage package)
    {
        string base_ = category switch
        {
            "bool" => "bool",
            "byte" => "byte",  // TODO: enum 子类别对象解析
            "int" => "int",
            "int64" => "int64",
            "real" => subCategory == "float" ? "float" : "double",
            "name" => "name",
            "string" => "string",
            "text" => "text",
            "exec" => "exec",
            "struct" => "struct",  // TODO: 结构体子类别对象解析
            "object" => ResolveImportObjectName(package, subCategoryObject)
                is { Length: > 0 } objName ? objName : "object",
            "class" => "class",
            "softobject" => "softobject",
            "softclass" => "softclass",
            "wildcard" => "wildcard",
            "delegate" => "delegate",
            "mcdelegate" => "mcdelegate",
            "interface" => "interface",
            "enum" => "enum",
            "fieldpath" => "fieldpath",
            _ => category,
        };

        return containerType switch
        {
            1 => base_ + "[]",          // EPinContainerType::Array
            2 => $"set<{base_}>",       // EPinContainerType::Set
            3 => $"map<{base_},?>",     // EPinContainerType::Map (value type not fully resolved)
            _ => base_,                  // EPinContainerType::None
        };
    }

    /// <summary>
    /// Skips an FEdGraphTerminalType serialized via its custom operator<<
    /// (EdGraphNode.cpp). This is NOT tagged properties — the struct's
    /// operator<< directly serializes its fields.
    ///
    /// Layout (UE 5.0.3, all custom versions active):
    ///   TerminalCategory (FName 8B)
    ///   TerminalSubCategory (FName 8B)
    ///   TerminalSubCategoryObject (UObject* → FPackageIndex 4B)
    ///   bTerminalIsConst (bool 4B via Ar << bool)
    ///   bTerminalIsWeakPointer (bool 4B)
    ///   bTerminalIsUObjectWrapper (bool 4B, since PinTypeIncludesUObjectWrapperFlag)
    /// </summary>
    private static void SkipTerminalType(UAssetReader reader)
    {
        _ = reader.ReadFName();  // TerminalCategory
        _ = reader.ReadFName();  // TerminalSubCategory
        _ = reader.ReadInt32();  // TerminalSubCategoryObject (FPackageIndex)
        _ = reader.ReadBool();  // bTerminalIsConst
        _ = reader.ReadBool();  // bTerminalIsWeakPointer
        _ = reader.ReadBool();  // bTerminalIsUObjectWrapper
    }

    private static void SkipPinReference(UAssetReader reader)
    {
        // bNullPtr via Ar << bNullPtr = uint32 (4 bytes)
        bool nullRef = reader.ReadBool();
        if (nullRef)
        {
            return;
        }

        _ = reader.ReadInt32();
        _ = reader.ReadGuid();
    }

    private static string ReadFText(UAssetReader reader)
    {
        // FText::SerializeText in binary archive:
        // 1. Flags (int32, 4 bytes)
        // 2. HistoryType (int8, 1 byte)
        //    - -1 (None): empty or culture-invariant text
        //    -  0 (Base): localized text with Namespace, Key, SourceString
        //    -  1+ : other history types (NamedFormat, etc.)
        _ = reader.ReadUInt32();  // Flags
        sbyte historyType = unchecked((sbyte)reader.ReadByte());

        if (historyType == 0) // ETextHistoryType::Base
        {
            // FTextHistory_Base::Serialize:
            // Namespace (FString via FTextKey::SerializeAsString)
            // Key (FString via FTextKey::SerializeAsString)
            // SourceString (FString)
            _ = reader.ReadFString();  // Namespace
            _ = reader.ReadFString();  // Key
            return reader.ReadFString();  // SourceString
        }

        if (historyType == -1) // ETextHistoryType::None
        {
            // None type: bHasCultureInvariantString (bool, 4 bytes)
            // If true: CultureInvariantString (FString)
            bool bHasCultureInvariantString = reader.ReadBool();
            if (bHasCultureInvariantString)
            {
                return reader.ReadFString();
            }
        }

        // For other history types (1+), we don't parse the body.
        // This is a best-effort skip; if we encounter these, the reader
        // position will be wrong and subsequent parsing will fail.
        // In practice, pin FText fields in saved blueprints are either
        // empty (None) or Base type.
        return string.Empty;
    }

    private static string ReadName(UAssetReader reader, UAssetPackage package)
    {
        (int index, int number) = reader.ReadFName();

        if (index < 0 || index >= package.Names.Count)
        {
            throw new InvalidDataException($"Name table index {index} out of range (names: {package.Names.Count}).");
        }

        string name = package.GetName(index);
        return number != 0 ? $"{name}_{number}" : name;
    }

    // ─────────────────────────────────────────────────────────────
    // 自定义版本常量与辅助
    // ─────────────────────────────────────────────────────────────

    // FFrameworkObjectVersion::GUID (DevObjectVersion.cpp)
    private static readonly UAssetGuid FFrameworkObjectVersionGuid =
        new(0xCFFC743F, 0x43B04480, 0x939114DF, 0x171D2073);

    // FUE5MainStreamObjectVersion::GUID
    private static readonly UAssetGuid UE5MainStreamObjectVersionGuid =
        new(0x697DD581, 0xE64f41AB, 0xAA4A51EC, 0xBEB7B628);

    // FReleaseObjectVersion::GUID
    private static readonly UAssetGuid FReleaseObjectVersionGuid =
        new(0x9C54D522, 0xA8264FBE, 0x94210746, 0x61B482D0);

    // FBlueprintsObjectVersion::GUID
    private static readonly UAssetGuid FBlueprintsObjectVersionGuid =
        new(0xB0D832E4, 0x1F894F0D, 0xACCF7EB7, 0x36FD4AA2);

    // FUE5MainStreamObjectVersion::EdGraphPinSourceIndex (enum index = 50)
    // From UE5MainStreamObjectVersion.h, counting from BeforeCustomVersionWasAdded=0
    private const int EdGraphPinSourceIndexVersion = 50;

    /// <summary>
    /// 检查包的自定义版本是否 >= 指定版本号。
    /// </summary>
    private static bool HasCustomVersion(UAssetPackage package, UAssetGuid guid, int minVersion)
    {
        foreach (var cv in package.Summary.CustomVersions)
        {
            if (cv.Key.HasValue && cv.Key.Value.Equals(guid))
            {
                return cv.Version >= minVersion;
            }
        }
        // 如果找不到自定义版本条目，默认为 0（BeforeCustomVersionWasAdded）
        // 对于 UE 5.0 的包，所有自定义版本都应该存在
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    // 函数签名提取
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 从函数图中提取函数签名（inputs/outputs）。
    /// 桥端 ExtractFunctionSignature 逻辑：
    /// - 找 K2Node_FunctionEntry 节点，其 output 非 exec 引脚 = inputs
    /// - 找 K2Node_FunctionResult 节点，其 input 非 exec 引脚 = outputs
    /// </summary>
    private static void ExtractFunctionSignature50(UAssetPackage package, int graphExportIndex,
        Dictionary<string, object?> funcEntry)
    {
        var inputs = new List<object>();
        var outputs = new List<object>();

        for (int exportIndex = 0; exportIndex < package.Exports.Count; exportIndex++)
        {
            ObjectExport export = package.Exports[exportIndex];
            if (export.OuterIndex.IsExport && export.OuterIndex.ToExportIndex() != graphExportIndex)
            {
                continue;
            }

            string className = ResolveImportObjectName(package, export.ClassIndex);
            bool isFunctionEntry = className == "K2Node_FunctionEntry";
            bool isFunctionResult = className == "K2Node_FunctionResult";
            if (!isFunctionEntry && !isFunctionResult) continue;

            byte[]? raw = export.Data?.RawData;
            if (raw is null || raw.Length == 0) continue;

            try
            {
                using var stream = new MemoryStream(raw);
                var reader = new UAssetReader(stream);
                var trace = new List<string>();

                // 解析 tagged properties
                var meta = ExtractNodeMetadata(reader, package, trace);

                // 读取 pins
                int pinCount = reader.ReadInt32();
                for (int p = 0; p < pinCount; p++)
                {
                    bool nullPin = reader.ReadBool();
                    if (nullPin) continue;

                    _ = new FPackageIndex(reader.ReadInt32());
                    UAssetGuid _pinId = reader.ReadGuid();

                    var pin = ReadPin50(reader, package);
                    if (pin.bHidden) continue;

                    // exec 类型引脚跳过
                    if (pin.Type == "exec") continue;

                    var pinEntry = new Dictionary<string, object?>
                    {
                        ["name"] = pin.Name,
                        ["type"] = pin.Type,
                        ["direction"] = pin.Direction,
                    };

                    // FunctionEntry: output 非 exec 引脚 = inputs
                    // FunctionResult: input 非 exec 引脚 = outputs
                    if (isFunctionEntry && pin.Direction == "output")
                    {
                        inputs.Add(pinEntry);
                    }
                    else if (isFunctionResult && pin.Direction == "input")
                    {
                        outputs.Add(pinEntry);
                    }
                }
            }
            catch { }
        }

        funcEntry["inputs"] = inputs;
        funcEntry["outputs"] = outputs;
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助方法
    // ─────────────────────────────────────────────────────────────

    private static bool IsGraphExport(UAssetPackage package, int exportIndex)
    {
        ObjectExport export = package.Exports[exportIndex];
        return ResolveImportObjectName(package, export.ClassIndex) == "EdGraph";
    }

    private static bool IsNodeExport(UAssetPackage package, int exportIndex)
    {
        ObjectExport export = package.Exports[exportIndex];
        string className = ResolveImportObjectName(package, export.ClassIndex);
        return className.StartsWith("K2Node_", System.StringComparison.Ordinal);
    }

    /// <summary>解析 import 引用的对象名（ObjectName）。</summary>
    private static string ResolveImportObjectName(UAssetPackage package, FPackageIndex index)
    {
        if (!index.IsImport)
        {
            return string.Empty;
        }

        int importIndex = index.ToImportIndex();
        if (importIndex < 0 || importIndex >= package.Imports.Count)
        {
            return string.Empty;
        }

        return package.Imports[importIndex].ObjectName;
    }
}
