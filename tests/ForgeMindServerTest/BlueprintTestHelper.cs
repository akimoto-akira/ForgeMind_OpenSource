// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Test;

/// <summary>
/// Blueprint test public helper: MCP step commands, DSL distribution, IR validation.
/// All ITestCase implementations share this static class to avoid duplicating communication logic.
/// </summary>
public static class BlueprintTestHelper
{
    // ---- Constants ----

    public const string ProjectPath =
        @"d:\ForgeMind\UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject";

    public const string ContentRoot =
        @"d:\ForgeMind\UEProject\ForgeMind_5_0\ForgeMind_5_0\Content";

    // ---- Data Records ----

    public record FuncInfo(string entryId, string resultId);

    // =====================================================================
    // MCP step: wrap bridge commands from editor tool
    // =====================================================================

    public record GraphInfo(string name, string kind, NodeInfo[] nodes);
    public record NodeInfo(string nodeId, string nodeClass, string title, int posX, int posY, PinInfo[] pins);
    public record PinInfo(string name, string direction, string type, string? defaultValue);

    public static void CreateBlueprintActor(TestContext ctx, string assetPath)
        => CreateBlueprint(ctx, assetPath, "Actor");

    /// <summary>
    /// Create (or idempotently load) a level and set it as the current editor level (create_level).
    /// Level blueprints are embedded assets (not standalone .uasset); subsequent commands like add_blueprint_node /
    /// compile_blueprint / save_asset address levels by path. Bridge-side
    /// ResolveBlueprint uses the level blueprint fallback (PersistentLevel.LevelScriptActor's
    /// ClassGeneratedBy). save_asset writes map packages with .umap extension.
    /// </summary>
    public static void CreateLevel(TestContext ctx, string levelPath)
    {
        var r = EditorCall(ctx, "create_level", new { path = levelPath });
        var created = GetBool(r, "created");
        var loaded = GetBool(r, "loaded");
        ctx.AssertTrue(created || loaded,
            $"create_level {levelPath}: {GetStr(r, "reason")}");
    }

    /// <summary>
    /// Place a named Actor in the current editor level (spawn_actor). The name is used by level blueprint
    /// levelLiteral etc. for name-based addressing; if the level already has an Actor with the same name,
    /// the bridge reuses it idempotently without duplication.
    /// </summary>
    public static void SpawnLevelActor(TestContext ctx, string type, string name)
    {
        var r = EditorCall(ctx, "spawn_actor", new { type, name });
        ctx.AssertTrue(GetBool(r, "spawned"), $"spawn_actor {type} '{name}': {GetStr(r, "reason")}");
    }

    /// <summary>
    /// Create a blueprint with the specified parent class. Pass reflection class name for parentClass (Actor / Character / ...);
    /// For blueprint parents, pass the generated class name - after compilation, BP_Base asset's generated class is "BP_Base_C".
    /// create_blueprint resolves class names via FindObject(ANY_PACKAGE) (already loaded objects).
    /// </summary>
    public static void CreateBlueprint(TestContext ctx, string assetPath, string parentClass, bool replicates = false)
    {
        var payload = new Dictionary<string, object>
        {
            ["path"] = assetPath,
            ["parentClass"] = parentClass,
        };
        if (replicates)
            payload["replicates"] = true;
        var r = EditorCall(ctx, "create_blueprint", payload);
        ctx.AssertTrue(GetBool(r, "created"), $"create_blueprint: {assetPath} (parent {parentClass})");
    }

    public static void AddVariable(TestContext ctx, string assetPath,
        string name, string type, object? defaultValue = null, bool instanceEditable = false,
        bool bitmask = false, string? bitmaskEnum = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["path"] = assetPath,
            ["name"] = name,
            ["type"] = type,
        };
        // Bridge-side add_blueprint_variable / set_blueprint_variable_default reads the default value
        // field as 'default' ('defaultValue' is the IR field name, not recognized at command layer):
        // Passing defaultValue is silently ignored by bridge -> variable default not assigned (Var_Edit_A visual feedback).
        if (defaultValue != null)
            payload["default"] = defaultValue;
        // instanceEditable clears CPF_DisableEditOnInstance (AddMemberVariable's default).
        // Data-asset class variables need this to show up in the Data Asset editor at all
        // (user constraint 2026-10-14: only editable variables are discovered there).
        if (instanceEditable)
            payload["instanceEditable"] = true;
        // Bitmask metadata: MD_Bitmask (property is a bitmask) + MD_BitmaskEnum (associated flag enum).
        if (bitmask)
            payload["bitmask"] = true;
        if (bitmaskEnum != null)
            payload["bitmaskEnum"] = bitmaskEnum;

        var r = EditorCallRaw(ctx, "add_blueprint_variable", payload);
        ctx.AssertTrue(GetBool(r, "added"),
            $"add_blueprint_variable {name} ({type}): {GetStr(r, "reason")}");
    }

    /// <summary>
    /// Read back a variable's bitmaskEnum metadata via list_blueprint_variables. This is
    /// the exact short-name value the editor's "Bitmask Enum" combo matches against, so
    /// it lets a test assert the association landed (A/B equivalence) without eyeballing
    /// the details panel. Returns "" when the variable has no bitmaskEnum.
    /// </summary>
    public static string GetVariableBitmaskEnum(TestContext ctx, string assetPath, string varName)
        => GetVariableField(ctx, assetPath, varName, "bitmaskEnum");

    /// <summary>
    /// Read back one reported field of a named variable via list_blueprint_variables
    /// (e.g. "bitmaskEnum", "defaultValue"). The defaultValue is read off the compiled
    /// CDO, so it reflects the value the bitmask/enum resolution actually wrote. Returns
    /// "" when the variable or field is absent.
    /// </summary>
    public static string GetVariableField(TestContext ctx, string assetPath, string varName, string field)
    {
        var r = EditorCall(ctx, "list_blueprint_variables", new { path = assetPath });
        var vars = r?["response"]?["variables"]?.AsArray();
        if (vars != null)
        {
            foreach (var v in vars)
            {
                if (v?["name"]?.GetValue<string>() == varName)
                {
                    return v?[field]?.GetValue<string>() ?? "";
                }
            }
        }
        return "";
    }

    public static FuncInfo CreateFunction(TestContext ctx, string assetPath, string funcName,
        object[]? locals = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["path"] = assetPath,
            ["name"] = funcName,
        };
        if (locals != null)
        {
            payload["locals"] = locals;
        }
        var r = EditorCallRaw(ctx, "create_function_graph", payload);
        ctx.AssertTrue(GetBool(r, "created"), $"create_function_graph {funcName}: {GetStr(r, "reason")}");
        return FindFunctionTerminators(ctx, assetPath, funcName);
    }

    public static FuncInfo CreateFunctionWithInputs(TestContext ctx,
        string assetPath, string funcName, object[] inputs, object[]? outputs = null,
        bool pure = false)
    {
        // companion's create_function_graph signature field is 'params' (not 'inputs');
        // 'outputs' is the function return value (input pin on result node), bridge already supports
        var payload = new Dictionary<string, object>
        {
            ["path"] = assetPath,
            ["name"] = funcName,
            ["params"] = inputs,
        };
        if (outputs != null)
        {
            payload["outputs"] = outputs;
        }
        if (pure)
        {
            // Pure function: no exec pin, compiled as FUNC_BlueprintPure. Bridge
            // create_function_graph supports this field (synonymous with DSL [Function(Pure)]).
            payload["pure"] = true;
        }
        var r = EditorCall(ctx, "create_function_graph", payload);
        ctx.AssertTrue(GetBool(r, "created"), $"create_function_graph {funcName}: {GetStr(r, "reason")}");
        return FindFunctionTerminators(ctx, assetPath, funcName);
    }

    /// <summary>
    /// Common entry point for list_blueprint_graph: returns a flattened array of all nodes in specified graph.
    /// Shared by FindFunctionTerminators / FindMacroTerminators / FindGraphEntryNode,
    /// avoiding duplicate list calls + found assertion + graph node iteration (per spec point 8).
    /// </summary>
    private static JsonArray ListGraphNodes(TestContext ctx, string assetPath, string graphName)
    {
        var g = EditorCall(ctx, "list_blueprint_graph", new { path = assetPath, graphName });
        ctx.AssertTrue(GetBool(g, "found"), $"list_blueprint_graph {graphName}: {GetStr(g, "reason")}");
        var nodes = new JsonArray();
        foreach (var graph in g["response"]?["graphs"]?.AsArray() ?? new JsonArray())
        {
            foreach (var node in graph?["nodes"]?.AsArray() ?? new JsonArray())
            {
                nodes.Add(node?.DeepClone());
            }
        }
        return nodes;
    }

    /// <summary>
    /// create_function_graph does not return node IDs - locate the entry/result nodes of this function graph
    /// via list_blueprint_graph by node class (connect requires NodeGuid strings).
    /// </summary>
    private static FuncInfo FindFunctionTerminators(TestContext ctx, string assetPath, string funcName)
    {
        var nodes = ListGraphNodes(ctx, assetPath, funcName);
        string entryId = "", resultId = "";
        foreach (var node in nodes)
        {
            string cls = node?["class"]?.GetValue<string>() ?? "";
            string id = node?["id"]?.GetValue<string>() ?? "";
            if (cls == "K2Node_FunctionEntry") entryId = id;
            else if (cls == "K2Node_FunctionResult") resultId = id;
        }
        ctx.AssertTrue(entryId.Length > 0 && resultId.Length > 0,
            $"function graph '{funcName}' is missing its entry/result node");
        return new FuncInfo(entryId, resultId);
    }

    /// <summary>
    /// Create macro graph (create_macro_graph). Bridge uses FBlueprintEditorUtils::AddMacroGraph
    /// to create the graph + entry/exit tunnel (K2Node_Tunnel) with default exec pins (then/execute).
    /// params become output pins on entry tunnel (data flows from entry into macro body), outputs become
    /// input pins on exit tunnel. Unlike CreateFunction: macro graphs allow exec pins, no pure,
    /// no locals. Returns entry/exit tunnel node IDs (needed for connect).
    /// </summary>
    public static FuncInfo CreateMacro(TestContext ctx, string assetPath, string macroName,
        object[]? inputs = null, object[]? outputs = null, bool exec = true)
    {
        var payload = new Dictionary<string, object>
        {
            ["path"] = assetPath,
            ["name"] = macroName,
        };
        if (inputs != null) payload["params"] = inputs;
        if (outputs != null) payload["outputs"] = outputs;
        // When macro body has only data flow, don't build the execute/then pair (strategy preference). Default true matches bridge behavior.
        if (!exec) payload["exec"] = false;
        var r = EditorCallRaw(ctx, "create_macro_graph", payload);
        ctx.AssertTrue(GetBool(r, "created"), $"create_macro_graph {macroName}: {GetStr(r, "reason")}");
        return FindMacroTerminators(ctx, assetPath, macroName);
    }
    /// <summary>
    /// Locate entry/exit tunnel node IDs of a macro graph. Macro graphs have two K2Node_Tunnel:
    /// entry tunnel (canHaveOutputs) host macro's input pins, exit tunnel (canHaveInputs)
    /// hosts output pins. Prefer canHaveOutputs field exposed by list_blueprint_graph to distinguish;
    /// fallback to exec pin direction when field is missing (compatibility path for older bridge).
    /// Macros without exec pins have no exec pins on either side - only canHaveOutputs can distinguish.
    /// </summary>
    private static FuncInfo FindMacroTerminators(TestContext ctx, string assetPath, string macroName)
    {
        var nodes = ListGraphNodes(ctx, assetPath, macroName);
        string entryId = "", exitId = "";
        foreach (var node in nodes)
        {
            if ((node?["class"]?.GetValue<string>() ?? "") != "K2Node_Tunnel") continue;
            string id = node?["id"]?.GetValue<string>() ?? "";
            var roleNode = node?["canHaveOutputs"];
            if (roleNode is not null)
            {
                if (roleNode.GetValue<bool>()) entryId = id; else exitId = id;
                continue;
            }
            bool hasOutputExec = false, hasInputExec = false;
            foreach (var pin in node?["pins"]?.AsArray() ?? new JsonArray())
            {
                string dir = pin?["direction"]?.GetValue<string>() ?? "";
                string type = pin?["type"]?.GetValue<string>() ?? "";
                if (type == "exec" && dir == "output") hasOutputExec = true;
                if (type == "exec" && dir == "input") hasInputExec = true;
            }
            if (hasOutputExec) entryId = id;
            else if (hasInputExec) exitId = id;
        }
        ctx.AssertTrue(entryId.Length > 0 && exitId.Length > 0,
            $"macro graph '{macroName}' is missing its entry/exit tunnel");
        return new FuncInfo(entryId, exitId);
    }

    /// <summary>
    /// Get entry node ID of any graph (K2Node_FunctionEntry). Construction script graph
    /// (UserConstructionScript) entry is engine-provided - DSL/IR side does not create entry node
    /// (translator addEntry:false). A-side step must explicitly connect entry "then" to the first node,
    /// otherwise visual inspection shows "function header not connected to subsequent nodes" (2026-10-03 user visual feedback).
    /// </summary>
    public static string FindGraphEntryNode(TestContext ctx, string assetPath, string graphName)
    {
        var nodes = ListGraphNodes(ctx, assetPath, graphName);
        string entryId = "";
        foreach (var node in nodes)
        {
            if (node?["class"]?.GetValue<string>() == "K2Node_FunctionEntry")
                entryId = node?["id"]?.GetValue<string>() ?? "";
        }
        ctx.AssertTrue(entryId.Length > 0, $"graph '{graphName}' has no K2Node_FunctionEntry node");
        return entryId;
    }

    public static NodeInfo AddNode(TestContext ctx, string assetPath,
        string graphName, string nodeType, int x, int y,
        string? ownerClass = null, string? kind = null,
        int[]? intCases = null, string[]? stringCases = null,
        string? enumName = null, object[]? eventParams = null,
        string? function = null, string? component = null,
        string? targetClass = null, int? outputs = null,
        object[]? tracks = null, string? rpc = null, bool? reliable = null)
    {
        // companion's add_blueprint_node fields are name / x / y (not nodeType / nodePos)
        var payload = new Dictionary<string, object>
        {
            ["path"] = assetPath,
            ["graphName"] = graphName,
            ["x"] = x,
            ["y"] = y,
        };
        // "Cast<ACharacter>" -> native flow node bare name 'Cast' + 'targetClass' field.
        // Engine C++ classes (A/U prefix) must be dispatched with full path: companion's
        // ForgeMindFindFirstObject only searches loaded objects; ACharacter and other engine classes
        // may not be in memory at editor startup, and A-prefix names skip the prefix retry branch;
        // full path uses LoadObject for on-demand loading (same as DSL translator approach).
        if (nodeType.StartsWith("Cast<") && nodeType.EndsWith(">"))
        {
            payload["name"] = "Cast";
            string target = nodeType[5..^1];
            if (target.Length > 1 && (target[0] == 'A' || target[0] == 'U')
                && char.IsUpper(target[1]) && !target.Contains('/'))
            {
                payload["targetClass"] = "/Script/Engine." + target[1..];
            }
            else
            {
                payload["targetClass"] = target;
            }
        }
        else
        {
            payload["name"] = nodeType;
        }
        if (ownerClass != null)
        {
            // Function library members (e.g. KismetSystemLibrary::PrintString) are not on the blueprint parent chain,
            // must be located via 'class'; default resolves as self member and will fail at compile time with member not found.
            payload["class"] = ownerClass;
        }
        if (kind != null)
        {
            // getter/setter/macro/customevent/callDelegate/... etc. non-default node families
            payload["kind"] = kind;
        }
        if (intCases != null)
        {
            payload["cases"] = intCases;
        }
        if (outputs != null)
        {
            // MultiGate / Sequence: number of execution output pins. The bridge allocates two by
            // default and adds the rest via UK2Node_ExecutionSequence::AddInputPin -- without it
            // a third output ("Out 2" / "then_2") simply does not exist on the node.
            payload["outputs"] = outputs.Value;
        }
        if (stringCases != null)
        {
            payload["cases"] = stringCases;
        }
        if (enumName != null)
        {
            payload["enum"] = enumName;
        }
        if (eventParams != null)
        {
            payload["params"] = eventParams;
        }
        if (rpc != null)
        {
            // Replication on a custom event (blueprint RPC): bridge sets
            // UK2Node_CustomEvent::FunctionFlags (FUNC_Net | direction | Reliable).
            payload["rpc"] = rpc;
            if (reliable is true)
                payload["reliable"] = true;
        }
        if (function != null)
        {
            // CreateDelegate: custom event function name to bind (bridge uses this for SetFunction)
            payload["function"] = function;
        }
        if (component != null)
        {
            // componentBoundEvent: SCS component variable name in this blueprint for binding target
            payload["component"] = component;
        }
        if (targetClass != null)
        {
            // AddComponent and other "create node by class" class parameters: same form as DSL translator - use node
            // targetClass field (LowerAddComponent), **not** pin default value (verified
            // 'AddActorComponent' node has no Class pin, see Ctor_Loop_A).
            payload["targetClass"] = targetClass;
        }
        if (tracks != null)
        {
            // Timeline tracks (kind=timeline only): [{name, type, keys:[{t,v}] | times:[...] | curveAsset}].
            // The bridge never invents keyframes - curve data must come from the caller, on BOTH
            // the MCP A side and the DSL B side, so the two produce identical keys (count / time / value).
            payload["tracks"] = tracks;
        }

        var r = EditorCallRaw(ctx, "add_blueprint_node", payload);
        ctx.AssertTrue(GetBool(r, "added"), $"add_blueprint_node {nodeType} @ {graphName}: {GetStr(r, "reason")}");
        // Verify the created node class matches the expected kind -
        // catch silent mis-identification (e.g. callDelegate falling back
        // to K2Node_Event because the kind string didn't match in C++).
        var actualClass = GetStr(r, "nodeClass");
        if (!string.IsNullOrEmpty(actualClass) && !string.IsNullOrEmpty(kind))
        {
            var expected = kind.ToLowerInvariant() switch
            {
                "calldelegate" => "K2Node_CallDelegate",
                "adddelegate" => "K2Node_AddDelegate",
                "removedelegate" => "K2Node_RemoveDelegate",
                _ => null
            };
            if (expected != null && actualClass != expected)
            {
                throw new TestFailureException(
                    $"add_blueprint_node {nodeType} (kind={kind}) created wrong node type: " +
                    $"expected {expected}, got {actualClass}");
            }
        }
        // Try to extract node info from response if available (from new bridge with pins)
        string nodeId = GetStr(r, "nodeId");
        string nodeClass = GetStr(r, "nodeClass");
        string title = GetStr(r, "title");
        int posX = 0; int posY = 0;
        TryGetInt(r, "posX", out posX);
        TryGetInt(r, "posY", out posY);
        return new NodeInfo(nodeId, nodeClass, title, posX, posY, Array.Empty<PinInfo>());
    }

    /// <summary>
    /// list_blueprint_graph: get blueprint graph structure and node pin information.
    /// </summary>
    public static GraphInfo ListGraph(TestContext ctx, string assetPath, string? graphName = null)
    {
        var payload = new Dictionary<string, object> { ["path"] = assetPath };
        if (!string.IsNullOrEmpty(graphName))
        {
            payload["graphName"] = graphName;
        }
        var r = EditorCall(ctx, "list_blueprint_graph", payload);
        ctx.AssertTrue(GetBool(r, "found"), $"list_blueprint_graph: {GetStr(r, "reason")}");

        JsonArray? graphs = r["response"]?["graphs"]?.AsArray();
        ctx.AssertTrue(graphs != null && graphs.Count > 0, "list_blueprint_graph: no graphs found");

        var firstGraph = graphs[0];
        string name = firstGraph["name"]?.GetValue<string>() ?? "";
        string kind = firstGraph["kind"]?.GetValue<string>() ?? "";

        var nodes = new List<NodeInfo>();
        JsonArray? nodeArray = firstGraph["nodes"]?.AsArray();
        if (nodeArray != null)
        {
            foreach (var node in nodeArray)
            {
                string nodeId = node["id"]?.GetValue<string>() ?? "";
                string nodeClass = node["class"]?.GetValue<string>() ?? "";
                string title = node["title"]?.GetValue<string>() ?? "";
                int posX = node["posX"]?.GetValue<int>() ?? 0;
                int posY = node["posY"]?.GetValue<int>() ?? 0;

                var pins = new List<PinInfo>();
                JsonArray? pinArray = node["pins"]?.AsArray();
                if (pinArray != null)
                {
                    foreach (var pin in pinArray)
                    {
                        string pinName = pin["name"]?.GetValue<string>() ?? "";
                        string direction = pin["direction"]?.GetValue<string>() ?? "";
                        string type = pin["type"]?.GetValue<string>() ?? "";
                        string? defaultValue = pin["defaultValue"]?.GetValue<string>();
                        pins.Add(new PinInfo(pinName, direction, type, defaultValue));
                    }
                }

                nodes.Add(new NodeInfo(nodeId, nodeClass, title, posX, posY, pins.ToArray()));
            }
        }

        return new GraphInfo(name, kind, nodes.ToArray());
    }

    public static void AddEventDispatcher(TestContext ctx, string assetPath,
        string name, object[] eventParams)
    {
        // companion's add_event_dispatcher signature field is 'params' (not 'inputs')
        var r = EditorCall(ctx, "add_event_dispatcher", new
        {
            path = assetPath,
            name,
            @params = eventParams,
        });
        ctx.AssertTrue(GetBool(r, "added"), $"add_event_dispatcher {name}: {GetStr(r, "reason")}");
    }

    public static void AddComponent(TestContext ctx, string assetPath,
        string name, string type)
    {
        // companion's add_blueprint_component class name field is 'class' (not 'type')
        var r = EditorCall(ctx, "add_blueprint_component", new
        {
            path = assetPath,
            @class = type,
            name,
        });
        ctx.AssertTrue(GetBool(r, "added"), $"add_blueprint_component {name}: {GetStr(r, "reason")}");
    }

    public static void Connect(TestContext ctx, string assetPath,
        string fromNode, string fromPin, string toNode, string toPin)
    {
        var r = EditorCall(ctx, "connect_blueprint_pins", new
        {
            path = assetPath,
            fromNode,
            fromPin,
            toNode,
            toPin,
        });
        ctx.AssertTrue(GetBool(r, "connected"),
            $"connect {Short(fromNode)}.{fromPin} -> {Short(toNode)}.{toPin}: {GetStr(r, "reason")}");
    }

    public static void SetPinDefault(TestContext ctx, string assetPath,
        string nodeName, string pinName, string defaultValue)
    {
        // companion's set_pin_default_value fields are node / pin / value
        var r = EditorCall(ctx, "set_pin_default_value", new
        {
            path = assetPath,
            node = nodeName,
            pin = pinName,
            value = defaultValue,
        });
        ctx.AssertTrue(GetBool(r, "set"),
            $"set_pin_default {Short(nodeName)}.{pinName}={defaultValue}: {GetStr(r, "reason")}");
    }

    private static string Short(string guid) => guid.Length <= 8 ? guid : guid[..8];

    public static void CompileAndSave(TestContext ctx, string assetPath)
    {
        var compileResult = EditorCall(ctx, "compile_blueprint", new { path = assetPath });
        if (!GetBool(compileResult, "compiled"))
        {
            var reason = GetStr(compileResult, "reason");
            var errors = compileResult?["response"]?["errors"]?.GetValue<int>() ?? 0;
            // Collect compile messages for detailed diagnostics
            var msgs = compileResult?["response"]?["compileMessages"]?.AsArray();
            var msgDetail = new System.Text.StringBuilder();
            if (msgs != null)
            {
                foreach (var m in msgs)
                {
                    var severity = m?["severity"]?.GetValue<int>() ?? 0;
                    var message = m?["message"]?.GetValue<string>() ?? "";
                    if (severity == 0) // error
                        msgDetail.AppendLine($"  ERROR: {message}");
                    else
                        msgDetail.AppendLine($"  WARN:  {message}");
                }
            }
            throw new TestFailureException(
                $"compile_blueprint failed for '{assetPath}': {reason} (errors={errors})\n{msgDetail}");
        }

        var saveResult = EditorCall(ctx, "save_asset", new { path = assetPath });
        if (!GetBool(saveResult, "saved"))
        {
            var reason = GetStr(saveResult, "reason");
            throw new TestFailureException(
                $"save_asset failed for '{assetPath}': {reason}");
        }
    }

    // =====================================================================
    // Input Settings registration (action / axis key mappings)
    // =====================================================================

    public static void AddActionMapping(TestContext ctx, string action, string key,
        bool shift = false, bool ctrl = false, bool alt = false, bool cmd = false)
    {
        var r = EditorCall(ctx, "add_action_mapping", new
        {
            action,
            key,
            shift,
            ctrl,
            alt,
            cmd,
        });
        ctx.AssertTrue(GetBool(r, "added"),
            $"add_action_mapping {action}/{key}: {GetStr(r, "reason")}");
    }

    public static void AddAxisMapping(TestContext ctx, string axis, string key,
        double scale = 1.0)
    {
        var r = EditorCall(ctx, "add_axis_mapping", new
        {
            axis,
            key,
            scale,
        });
        ctx.AssertTrue(GetBool(r, "added"),
            $"add_axis_mapping {axis}/{key}/{scale}: {GetStr(r, "reason")}");
    }

    // =====================================================================
    // DSL distribution
    // =====================================================================

    public static void ApplyDsl(TestContext ctx, string dsl)
    {
        var result = ctx.CallTool("dsl", new()
        {
            ["dsl"] = dsl,
            ["path"] = ProjectPath,
        });

        // Safety net: ToolResult.Success=false means the DSL tool execution itself failed
        // (e.g., DSL syntax error, translator exception), not node creation failure.
        // Previously only checked warnings in assetResults; ToolResult-layer failures
        // were silently swallowed.
        if (!result.Success)
        {
            throw new TestFailureException(
                $"dsl tool execution failed: {result.Message}");
        }

        // DSL execution itself may succeed, but if generated blueprint nodes/connections have errors
        // (e.g., macro type identification failure leads to BP_PIN_TYPE_MISMATCH), compile_blueprint
        // will expose compilation errors. Must compile + save after each DSL distribution;
        // throw on compile errors - otherwise defects will be masked by ApplyDsl's success=true.
        var root = JsonNode.Parse(SerializeResult(result))!;
        var assetResults = root?["response"]?["assetResults"]?.AsArray();
        if (assetResults != null)
        {
            foreach (var asset in assetResults)
            {
                // Blueprint-backed asset types: anything not listed here exists only in memory
                // after apply (a macro library's DSL side used to be skipped here, so its B side
                // never reached disk while its A side -- CompileAndSave -- did).
                // Interfaces ("blueprint_interface"), structs ("user_defined_struct") and enums
                // ("user_defined_enum") are deliberately absent: they have no compile concept, so
                // compile_blueprint must not be sent. The bridge does not save them on apply either
                // -- the owning test case saves them explicitly (local SaveAsset helper in
                // Blueprint_Interface_Tests / Blueprint_Struct_Tests / Blueprint_Switch_Tests).
                if (asset?["assetType"]?.GetValue<string>() is not ("blueprint" or "blueprint_function_library"
                    or "level_blueprint" or "blueprint_macro_library"))
                    continue;

                // Check bridge warnings: BP_NODE_NOT_FOUND etc. means node creation failed;
                // compilation may still pass (blueprint just missing a call), but visual defects appear.
                // Previously warnings weren't checked, so node creation failures were silently ignored -
                // MatInst_Make_B user feedback "SetMaterial not created" was this gap.
                var warnings = asset?["warnings"]?.AsArray();
                if (warnings != null && warnings.Count > 0)
                {
                    var msgDetail = new System.Text.StringBuilder();
                    foreach (var w in warnings)
                    {
                        var code = w?["code"]?.GetValue<string>() ?? "";
                        var message = w?["message"]?.GetValue<string>() ?? "";
                        msgDetail.AppendLine($"  {code}: {message}");
                    }
                    throw new TestFailureException(
                        $"DSL applied but bridge reported warnings for '{asset?["path"]?.GetValue<string>()}':\n{msgDetail}");
                }

                var assetPath = asset["path"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(assetPath))
                    CompileAndSave(ctx, assetPath);
            }
        }
    }

    // =====================================================================
    // IR snapshot comparison (mis-touch detection)
    // =====================================================================

    /// <summary>
    /// Root directory for IR snapshot baselines: TestRuns/{engine version}/{test asset name}.json.
    /// Different from per-run timestamp directories - baselines persist across runs.
    /// </summary>
    private static readonly string SnapshotRoot = @"d:\ForgeMind\TestRuns";

    /// <summary>
    /// Mis-touch detection: compare asset IR with baseline snapshot to catch unintended modifications.
    ///
    /// Principle: read IR from bridge get_asset_ir (level=graph, no coordinates); node
    /// NodeGuid regenerates on each rebuild, so direct comparison would always false-positive -
    /// therefore first **re-key** each node's guid to a deterministic stable key
    /// (node class|full title#same-class occurrence sequence), then rewrite connection
    /// fromNode/toNode via the same mapping, then perform **full structural comparison** with baseline
    /// (key sets, array elements per-position, values - any field difference, count difference,
    /// or rewired connection will error and list each diff path).
    ///
    /// User-defined struct members are normalised as well: their IR "name" is the engine-internal
    /// VarName whose trailing GUID is FGuid::NewGuid() per struct creation (see
    /// <see cref="ScrubVolatileGuidTails"/>), and Content/Tests is wiped every run - so that tail
    /// is stripped before comparison, otherwise FHealth_A / FHealth_B / Struct_Custom_A /
    /// Struct_Custom_B could never match their baseline.
    ///
    /// When baseline doesn't exist, save current normalized IR (first run setup, no failure).
    /// </summary>
    public static void VerifyIrSnapshot(TestContext ctx, string assetPath)
    {
        var r = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(r, "success"), $"get_asset_ir {assetPath}: {GetStr(r, "error")}");
        var ir = r["response"];
        ctx.AssertTrue(ir?["definition"] != null, $"get_asset_ir {assetPath}: response missing definition");

        var normalized = NormalizeIrForSnapshot(ir!);
        string version = ir["version"]?.GetValue<string>() ?? "unknown";
        string assetName = assetPath[(assetPath.LastIndexOf('/') + 1)..];
        string baselinePath = Path.Combine(SnapshotRoot, version, assetName + ".json");

        if (!File.Exists(baselinePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, normalized.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
            ctx.Logger.Info(null, "[Test]     IR snapshot baseline saved: {0}", baselinePath);
            return;
        }

        var baseline = JsonNode.Parse(File.ReadAllText(baselinePath))!;
        var diffs = new List<string>();
        DiffJson(baseline, normalized, "$", diffs);
        if (diffs.Count > 0)
        {
            throw new TestFailureException(
                $"IR snapshot mismatch for {assetPath} (baseline: {baselinePath}) - {diffs.Count} difference(s):\n" +
                string.Join("\n", diffs));
        }
        ctx.Logger.Info(null, "[Test]     IR snapshot match: {0}", assetName);
    }

    /// <summary>
    /// Deep copy IR entry and re-key node guids to stable keys in all graphs
    /// (functions[].graph and eventGraphs[]), with connection endpoints updated accordingly.
    /// User-defined struct member names additionally get their volatile GUID tail stripped
    /// (see <see cref="ScrubVolatileGuidTails"/>).
    /// </summary>
        private static JsonNode NormalizeIrForSnapshot(JsonNode ir)
        {
            var clone = JsonNode.Parse(ir.ToJsonString())!;
            ScrubVolatileGuidTails(clone);
            var def = clone["definition"];
            if (def == null) return clone;

            foreach (var fn in def["functions"]?.AsArray() ?? new JsonArray())
                NormalizeIrGraph(fn?["graph"]);
            foreach (var g in def["eventGraphs"]?.AsArray() ?? new JsonArray())
                NormalizeIrGraph(g);
            // Macro graphs (bridge get_asset_ir macros[], added 2026-10-04): macro body internals also need
            // re-keying, otherwise macro body changes won't be visible to mis-touch detection.
            foreach (var m in def["macros"]?.AsArray() ?? new JsonArray())
                NormalizeIrGraph(m?["graph"]);
            return clone;
        }

        /// <summary>
    /// Trailing random GUID of a user-defined struct member's internal name.
    ///
    /// UE builds the member VarName as "&lt;FriendlyName&gt;_&lt;seq&gt;_&lt;32 hex digits&gt;"
    /// (FMemberVariableNameHelper::Generate, StructureEditorUtils.cpp:259/264), where the GUID
    /// comes from FGuid::NewGuid() at AddVariable (StructureEditorUtils.cpp:304) — a fresh random
    /// value for every variable of every freshly created struct. Bridge get_asset_ir emits that
    /// internal VarName (ForgeMindIR_Blueprint.cpp:1807) rather than FriendlyName, and pins of
    /// K2Node_MakeStruct are named after the same VarName — so FHealth_A / FHealth_B member names
    /// and Struct_Custom_A / Struct_Custom_B pin names all carry it.
    ///
    /// GroupLifecycle deletes Content/Tests before every run, so struct assets are recreated from
    /// scratch each run and this tail differs every run; comparing it verbatim makes those four
    /// baselines unmatchable by construction (observed TestRuns/20261008_1510: baseline
    /// Cur_2_2278FA58… vs actual Cur_2_E5D7D3C0…). Only the 32-hex tail is removed — the
    /// "&lt;FriendlyName&gt;_&lt;seq&gt;" prefix is deterministic (Cur_2 / Max_4 in every run) and
    /// member renames still show up as diffs.
    /// </summary>
    private static readonly Regex StructVarGuidTail = new(@"_[0-9A-F]{32}$", RegexOptions.Compiled);

    /// <summary>
    /// Strip that GUID tail from every string "name" / "fromPin" / "toPin" field of the IR
    /// (struct definition.members[], graph nodes[].pins[].name, and graph connections[]
    /// endpoints). Connections' fromPin/toPin carry the same VarName when the pin belongs
    /// to a K2Node_MakeStruct/K2Node_BreakStruct (pin named after the struct member), and
    /// NormalizeIrGraph only re-keys fromNode/toNode — without scrubbing the pin endpoints
    /// the connection diff still fails (observed TestRuns/20261014_0220: baseline
    /// Value_2_7C1517AE… vs actual Value_2_A23C13B9…). Only the 32-hex tail is removed:
    /// a real member rename / pin rename must still surface as a diff, and values such as
    /// defaultValue are left alone.
    /// </summary>
    private static void ScrubVolatileGuidTails(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot the keys: the loop body may replace a value (same key, no add/remove).
                foreach (string key in obj.Select(kvp => kvp.Key).ToList())
                {
                    var value = obj[key];
                    if ((key == "name" || key == "fromPin" || key == "toPin") && value is JsonValue jv
                        && jv.TryGetValue<string>(out var text) && text != null)
                    {
                        string scrubbed = StructVarGuidTail.Replace(text, "");
                        if (scrubbed != text) obj[key] = scrubbed;
                    }
                    ScrubVolatileGuidTails(value);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    ScrubVolatileGuidTails(item);
                break;
        }
    }

    /// <summary>
    /// Single graph re-key: key = node class|full title#same-class occurrence sequence
    /// (array order = creation order, test code deterministically creates, stable across runs).
    /// After connection endpoints are rewritten via guid->key mapping,
    /// connection differences become wiring differences.
    /// </summary>
    private static void NormalizeIrGraph(JsonNode? graph)
    {
        if (graph == null) return;
        var nodes = graph["nodes"]?.AsArray();
        if (nodes == null) return;

        var guidToKey = new Dictionary<string, string>();
        var seenCount = new Dictionary<string, int>();
        foreach (var n in nodes)
        {
            string guid = n?["id"]?.GetValue<string>() ?? "";
            string type = n?["type"]?.GetValue<string>() ?? "";
            string title = n?["title"]?.GetValue<string>() ?? "";
            string baseKey = $"{type}|{title}";
            int occ = seenCount.TryGetValue(baseKey, out int c) ? c : 0;
            seenCount[baseKey] = occ + 1;
            string key = $"{baseKey}#{occ}";
            guidToKey[guid] = key;
            n["id"] = key;
        }
        foreach (var w in graph["connections"]?.AsArray() ?? new JsonArray())
        {
            string from = w?["fromNode"]?.GetValue<string>() ?? "";
            string to = w?["toNode"]?.GetValue<string>() ?? "";
            if (guidToKey.TryGetValue(from, out var fk)) w["fromNode"] = fk;
            if (guidToKey.TryGetValue(to, out var tk)) w["toNode"] = tk;
        }

        // Recurse into nested subgraphs: a UK2Node_MathExpression's bound graph is exported
        // as node.subgraph (added 2026-10-15). Its nodes carry volatile NodeGuids too, so
        // without re-keying them the mis-touch snapshot compares raw guids and fails on every
        // re-run (observed TestRuns/20261015_1422: MExpr_Basic_MathNodes 12 diffs, all in
        // subgraph node ids / connection endpoints). Each subgraph re-keys independently.
        foreach (var n in nodes)
            NormalizeIrGraph(n?["subgraph"]);
    }

    /// <summary>Full structural comparison: objects compare key sets and values, arrays compare counts and per-position elements,
    /// values compare serialized text; differences recorded into <paramref name="diffs"/> (diff paths).</summary>
    private static void DiffJson(JsonNode? expected, JsonNode? actual, string path, List<string> diffs)
    {
        if (expected == null && actual == null) return;
        if (expected == null)
        {
            diffs.Add($"{path}: baseline missing, actual extra = {actual!.ToJsonString()}");
            return;
        }
        if (actual == null)
        {
            diffs.Add($"{path}: actual missing, baseline = {expected.ToJsonString()}");
            return;
        }
        if (expected is JsonObject eo && actual is JsonObject ao)
        {
            foreach (var kvp in eo)
            {
                if (!ao.TryGetPropertyValue(kvp.Key, out var av))
                    diffs.Add($"{path}.{kvp.Key}: actual missing, baseline = {kvp.Value?.ToJsonString()}");
                else
                    DiffJson(kvp.Value, av, $"{path}.{kvp.Key}", diffs);
            }
            foreach (var kvp in ao)
            {
                if (!eo.TryGetPropertyValue(kvp.Key, out _))
                    diffs.Add($"{path}.{kvp.Key}: baseline missing, actual = {kvp.Value?.ToJsonString()}");
            }
            return;
        }
        if (expected is JsonArray ea && actual is JsonArray aa)
        {
            if (ea.Count != aa.Count)
                diffs.Add($"{path}: count mismatch baseline={ea.Count} actual={aa.Count}");
            int n = Math.Min(ea.Count, aa.Count);
            for (int i = 0; i < n; i++)
                DiffJson(ea[i], aa[i], $"{path}[{i}]", diffs);
            return;
        }
        string e = expected.ToJsonString();
        string a = actual.ToJsonString();
        if (e != a)
            diffs.Add($"{path}: baseline={e} actual={a}");
    }

    // =====================================================================
    // editor tool call + response parsing
    // =====================================================================

    public static JsonNode EditorCall(TestContext ctx, string action, object payload)
        => EditorCallRaw(ctx, action, PayloadToDict(payload));

    private static JsonNode EditorCallRaw(TestContext ctx, string action,
        Dictionary<string, object> payload)
    {
        var dict = new Dictionary<string, object>
        {
            ["action"] = action,
            ["path"] = ProjectPath,
            ["payload"] = payload,
        };

        var result = ctx.CallTool("editor", dict);

        // Safety net: ToolResult.Success=false means the command itself failed (transport layer /
        // bridge exception), not a business-level false (e.g., node creation failed but command returned successfully).
        // Previously each caller only checked business fields like response.added / response.connected;
        // if some function (like AddVariable) forgot the business check, bridge errors were silently swallowed.
        // Here we intercept top-level failures uniformly, with action and message attached for easy diagnosis.
        if (!result.Success)
        {
            throw new TestFailureException(
                $"editor action '{action}' failed: {result.Message}");
        }

        return JsonNode.Parse(SerializeResult(result))!;
    }

    /// <summary>Convert anonymous object to Dictionary (camelCase serialize then deserialize).</summary>
    private static Dictionary<string, object> PayloadToDict(object payload)
    {
        // Serialize to JSON then deserialize to Dictionary<string, JsonElement>,
        // storing final values as JsonElement - CallTool's parameters will eventually be
        // serialized by JsonSerializer.Serialize, and JsonElement can round-trip correctly.
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var dict = new Dictionary<string, object>();
        using (var doc = JsonDocument.Parse(json))
        {
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.Clone();
            }
        }
        return dict;
    }

    // SerializeResult serializes ToolResult.Data (anonymous object { projectFile, action, response }),
    // so the root node is that object directly; response is under root, not under data.
    private static bool GetBool(JsonNode root, string field)
        => root?["response"]?[field]?.GetValue<bool>() ?? false;

    private static string GetStr(JsonNode root, string field)
        => root?["response"]?[field]?.GetValue<string>() ?? "";

    private static string SerializeResult(ToolResult r)
    {
        return JsonSerializer.Serialize(r.Data, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    // =====================================================================
    // Helper methods
    // =====================================================================

    private static bool TryGetInt(JsonNode root, string field, out int value)
    {
        var val = root?["response"]?[field];
        if (val != null)
        {
            value = val.GetValue<int>();
            return true;
        }
        value = 0;
        return false;
    }
}
