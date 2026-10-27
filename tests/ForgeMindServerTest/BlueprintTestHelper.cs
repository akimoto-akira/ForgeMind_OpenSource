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
    /// Creates a UInputAction asset (Enhanced Input plugin) and saves it — the asset the
    /// UK2Node_EnhancedInputAction event node binds (add_blueprint_node kind
    /// "enhancedInputAction" takes it through payload targetClass). A typed variable of
    /// class InputAction does not satisfy the node: its InputAction property is an asset
    /// pointer (requirement doc Tasks/UE_5.0/Blueprint_EnhancedInput.md Question 1 Step 1 asks for the asset itself).
    /// Default valueType Axis2D with the "Down" trigger: a trigger-less action reports
    /// GetSupportedTriggerEvents() == None and every exec pin of the event node then shows
    /// as "(Unsupported)", so the trigger is created by default.
    /// </summary>
    public static void CreateInputAction(TestContext ctx, string assetPath,
        string valueType = "Axis2D", string trigger = "InputTriggerDown")
    {
        var r = EditorCall(ctx, "create_input_action", new { path = assetPath, valueType, trigger });
        ctx.AssertTrue(GetBool(r, "created"), $"create_input_action {assetPath}: {GetStr(r, "reason")}");
        var s = EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(GetBool(s, "saved"), $"save_asset {assetPath}: {GetStr(s, "reason")}");
    }

    /// <summary>
    /// Creates a UInputMappingContext asset (Enhanced Input plugin) and saves it — the asset
    /// the "Add Mapping Context" call feeds the enhanced-input LocalPlayer subsystem
    /// (requirement doc Tasks/UE_5.0/Blueprint_EnhancedInput.md Question 3 Step 1). Created without key mappings: nothing
    /// in the current requirement set binds a key to it.
    /// </summary>
    public static void CreateInputMappingContext(TestContext ctx, string assetPath)
    {
        var r = EditorCall(ctx, "create_input_mapping_context", new { path = assetPath });
        ctx.AssertTrue(GetBool(r, "created"), $"create_input_mapping_context {assetPath}: {GetStr(r, "reason")}");
        var s = EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(GetBool(s, "saved"), $"save_asset {assetPath}: {GetStr(s, "reason")}");
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
        object[]? tracks = null, string? rpc = null, bool? reliable = null,
        int? inputs = null)
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
        if (inputs != null)
        {
            // MakeArray / MakeSet / MakeMap (UK2Node_MakeContainer): element count driving the
            // wildcard input pin count ("[0]".."[n-1]", output "Array"). Without it the node
            // defaults to a single input.
            payload["inputs"] = inputs.Value;
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
    ///
    /// Optional <paramref name="crossMethodPath"/>: a task doc whose "expected verification" section asks that the
    /// asset built step by step (A side) and the asset built from the DSL (B side) be content-equivalent passes
    /// the counterpart here. It is the same read, the same normalization and the same comparison - one function
    /// verifies any asset, with or without a counterpart.
    /// </summary>
    public static void VerifyIrSnapshot(TestContext ctx, string assetPath, string? crossMethodPath = null)
    {
        var ir = ReadIrForSnapshot(ctx, assetPath);
        var normalized = NormalizeIrForSnapshot(ir);
        string version = ir["version"]?.GetValue<string>() ?? "unknown";
        string assetName = assetPath[(assetPath.LastIndexOf('/') + 1)..];
        string baselinePath = Path.Combine(SnapshotRoot, version, assetName + ".json");

        if (!File.Exists(baselinePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, normalized.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
            ctx.Logger.Info(null, "[Test]     IR snapshot baseline saved: {0}", baselinePath);
        }
        else
        {
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

        // The counterpart comparison runs even on a seeding pass: "both channels agree" must not depend on
        // whether this asset happened to have a baseline yet.
        if (crossMethodPath != null)
        {
            var counterpart = NormalizeIrForSnapshot(ReadIrForSnapshot(ctx, crossMethodPath));
            AssertSameContentAcrossChannels(ctx, assetPath, normalized, crossMethodPath, counterpart);
        }
    }

    /// <summary>
    /// Read one asset's IR the way the snapshot compares it (level=graph: structure and wiring, no coordinates)
    /// and assert that the bridge answered with a definition.
    /// </summary>
    private static JsonNode ReadIrForSnapshot(TestContext ctx, string assetPath)
    {
        var r = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(r, "success"), $"get_asset_ir {assetPath}: {GetStr(r, "error")}");
        var ir = r["response"];
        ctx.AssertTrue(ir?["definition"] != null, $"get_asset_ir {assetPath}: response missing definition");

        // Domain-neutral content rule: an expression node that carries a texture key must actually hold a
        // resolved reference. A step command reports success even when the asset path it was given resolves to
        // nothing, and that null is what later surfaces as a compile error on the nodes wired to the sample.
        AssertTextureReferencesResolve(ctx, assetPath, ir!["definition"]!);
        return ir;
    }

    /// <summary>
    /// Deep copy IR entry and re-key node guids to stable keys in all graphs
    /// (functions[].graph and eventGraphs[]), with connection endpoints updated accordingly.
    /// Expression-domain definitions (material / material function) are re-keyed as well -
    /// see <see cref="NormalizeExpressionGraphIds"/> - so a material can be snapshotted with the
    /// same protection a blueprint has instead of freezing engine-assigned object names.
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
            NormalizeExpressionGraphIds(def);
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

    /// <summary>
    /// Re-key an expression-domain definition (material / material function). The bridge identifies an
    /// expression by its engine object name (add_material_expression returns that name; the ids inside an
    /// IR definition stay internal to the definition), and those names follow per-asset creation order -
    /// so two channels that build the same graph can never spell the same ids. Node ids become
    /// "&lt;class&gt;#&lt;occurrence within class&gt;", every reference (node input wires, material outputs) is
    /// rewritten through that mapping, and the node list plus each node's pin list are put in a
    /// deterministic order. This is the same approach NormalizeIrGraph uses for blueprint NodeGuids, which
    /// is why both domains share one normalization entry point (NormalizeIrForSnapshot).
    /// </summary>
    private static void NormalizeExpressionGraphIds(JsonNode? definition)
    {
        var nodes = definition?["nodes"]?.AsArray();
        if (nodes == null) return;

        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = new List<(string Key, JsonNode Node)>();

        foreach (var entry in nodes)
        {
            if (entry is not JsonObject node)
                continue;

            string type = node["type"]?.GetValue<string>() ?? "";
            int occurrence = counters.TryGetValue(type, out int seen) ? seen : 0;
            counters[type] = occurrence + 1;
            string key = $"{type}#{occurrence}";

            string id = node["id"]?.GetValue<string>() ?? "";
            if (id.Length > 0)
                remap[id] = key;
            node["id"] = key;

            if (node["inputs"] is JsonArray inputs)
            {
                foreach (var input in inputs)
                    RemapIrReference(input as JsonObject, "expression", remap);
                // Pin order comes from engine reflection here. Ordering it explicitly keeps the
                // comparison positional and independent of property-iteration changes.
                node["inputs"] = new JsonArray(inputs.OfType<JsonObject>()
                    .OrderBy(i => i["name"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
                    .Select(i => (JsonNode)i.DeepClone())
                    .ToArray());
            }
            keyed.Add((key, node));
        }

        definition!["nodes"] = new JsonArray(keyed
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Node.DeepClone())
            .ToArray());

        if (definition["materialOutputs"] is JsonObject outputs)
            foreach (var pair in outputs)
                RemapIrReference(pair.Value as JsonObject, "sourceNodeId", remap);
    }

    /// <summary>Rewrite one string field of an IR object through the id remap; unknown values stay as they are.</summary>
    private static void RemapIrReference(JsonObject? target, string field, Dictionary<string, string> remap)
    {
        if (target is null || target[field]?.GetValueKind() != JsonValueKind.String)
            return;
        string old = target[field]!.GetValue<string>();
        if (remap.TryGetValue(old, out string? key))
            target[field] = key;
    }

    // =====================================================================
    // Cross-method content equality - the optional counterpart leg of VerifyIrSnapshot
    // =====================================================================

    /// <summary>
    /// Compare one task doc's A/B pair (asset built step by step vs asset built from the DSL) after both sides
    /// went through the same normalization used for baselines, and after dropping the fields that legitimately
    /// differ between two assets - where each of them lives.
    ///
    /// Both sides must also actually carry a graph: an empty definition would otherwise compare equal to an
    /// empty definition and report the pair as verified.
    /// </summary>
    private static void AssertSameContentAcrossChannels(TestContext ctx, string pathA, JsonNode normalizedA,
        string pathB, JsonNode normalizedB)
    {
        var defA = normalizedA["definition"];
        var defB = normalizedB["definition"];
        ctx.AssertTrue(defA is not null && defB is not null,
            $"cross-method check {pathA} vs {pathB}: a side reported no definition");

        ctx.AssertTrue(CountNodes(defA!) > 0, $"get_asset_ir {pathA}: definition carries no expressions");
        ctx.AssertTrue(CountNodes(defB!) > 0, $"get_asset_ir {pathB}: definition carries no expressions");

        var strippedA = StripAssetPathFields(defA.DeepClone());
        var strippedB = StripAssetPathFields(defB.DeepClone());

        var diffs = new List<string>();
        DiffJson(strippedA, strippedB, "$", diffs);
        ctx.AssertTrue(diffs.Count == 0,
            $"Cross-method IR mismatch ({pathA} vs {pathB}): {diffs.Count} difference(s)\n" +
            string.Join("\n", diffs));
    }

    private static int CountNodes(JsonNode definition)
        => definition["nodes"] is JsonArray nodes ? nodes.Count : 0;

    /// <summary>
    /// Assert every texture-sample expression in the read-back IR references a texture.
    ///
    /// A sample whose asset path does not resolve keeps the property null while the command still reports
    /// success, so the reference is verified from the asset side instead of trusting the command's own flag.
    /// </summary>
    private static void AssertTextureReferencesResolve(TestContext ctx, string assetPath, JsonNode definition)
    {
        if (definition["nodes"] is not JsonArray nodes)
            return;

        foreach (var entry in nodes)
        {
            if (entry is not JsonObject node)
                continue;
            string type = node["type"]?.GetValue<string>() ?? "";
            if (!type.Contains("TextureSample", StringComparison.Ordinal))
                continue;

            var texture = node["texture"];
            bool resolved = texture is not null
                && texture.GetValueKind() == JsonValueKind.String
                && texture.GetValue<string>().Length > 0;
            ctx.AssertTrue(resolved,
                $"{assetPath}: {type} '{node["id"]?.GetValue<string>() ?? "?"}' references no texture " +
                $"(texture={texture?.ToJsonString() ?? "(absent)"}); a null sample makes the material " +
                "fail to compile and the error lands on the nodes wired to it");
        }
    }

    /// <summary>
    /// Remove asset-path-bearing fields so a comparison ignores where an asset lives:
    /// "path", "assetPath" and "packageName" at any depth.
    /// </summary>
    private static JsonNode StripAssetPathFields(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var keysToRemove = obj
                    .Where(kvp => kvp.Key is "path" or "assetPath" or "packageName")
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in keysToRemove)
                    obj.Remove(key);
                foreach (var kvp in obj)
                    StripAssetPathFields(kvp.Value!);
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item != null)
                        StripAssetPathFields(item);
                }
                break;
        }
        return node;
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
    // Public so a case asserting a step command's own business flags reads them the same way
    // (a ToolResult-level success still carries created=false / added=false with a reason).
    public static bool GetBool(JsonNode root, string field)
        => root?["response"]?[field]?.GetValue<bool>() ?? false;

    public static string GetStr(JsonNode root, string field)
        => root?["response"]?[field]?.GetValue<string>() ?? "";

    /// <summary>Assert the business flag a material step command reports (created / added / connected / compiled / saved).</summary>
    public static void StepOk(TestContext ctx, JsonNode response, string flag, string step)
    {
        ctx.AssertTrue(GetBool(response, flag), $"{step} -> {flag}=false: {GetStr(response, "reason")}");
    }

    /// <summary>
    /// Assert a material add step reported success and return the expressionId it issued.
    /// An add step writes every extra payload key as a property and reports the ones it
    /// could not write in propertyErrors while still answering added=true, so that field
    /// is checked here too - a node that was created without the value the step asked for
    /// is a failed step, not a pass.
    /// </summary>
    public static string ExpressionId(TestContext ctx, JsonNode response, string step)
    {
        ctx.AssertTrue(GetBool(response, "added"), $"{step} -> added=false: {GetStr(response, "reason")}");
        string propertyErrors = GetStr(response, "propertyErrors");
        ctx.AssertTrue(propertyErrors.Length == 0, $"{step} -> added, but these properties did not land: {propertyErrors}");
        string id = MaterialExpressionIdValue(response);
        ctx.AssertTrue(id.Length > 0, $"{step} -> response carries no expressionId: {response.ToJsonString()}");
        return id;
    }

    /// <summary>
    /// Read the finished material back through get_asset_ir and assert that every expression
    /// class the task doc names is present with the expected multiplicity. This is a live
    /// content check, not a snapshot: it fails with the missing class named, so a graph that
    /// is missing what the doc asks for surfaces here instead of being reported as a pass.
    /// Extra nodes are tolerated (the counts are lower bounds); the full-shape comparison is
    /// what VerifyIrSnapshot baselines cover.
    /// </summary>
    public static void ExpectExpressions(TestContext ctx, string assetPath, params (string Type, int Count)[] expected)
    {
        var response = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(response, "success"), $"get_asset_ir {assetPath}: {GetStr(response, "error")}");
        var graph = response["response"]?["definition"]?["nodes"]?.AsArray();
        ctx.AssertTrue(graph is not null, $"get_asset_ir {assetPath}: definition carries no nodes: {response.ToJsonString()}");
        var nodes = graph!;

        foreach ((string type, int count) in expected)
        {
            int found = nodes.Count(n => n?["type"]?.GetValue<string>() == type);
            ctx.AssertTrue(found >= count,
                $"{assetPath}: expected {count} x {type}, found {found}. Built graph: {DescribeNodes(nodes)}");
        }
    }

    private static string MaterialExpressionIdValue(JsonNode response)
        => response?["response"]?["expressionId"]?.GetValue<string>()
           ?? response?["response"]?["nodeId"]?.GetValue<string>()
           ?? "";

    private static string DescribeNodes(JsonArray nodes)
        => string.Join(", ", nodes.Select(n => n?["type"]?.GetValue<string>() ?? "?"));

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
