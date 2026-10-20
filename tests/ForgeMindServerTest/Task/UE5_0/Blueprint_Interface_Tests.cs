// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 28 - Blueprint Interface (UE 5.0) - from Tasks/UE_5.0/blueprint-interface.md
//
// Three subtasks share one pair of interface assets (doc task 1 builds IDamageable, task 2/3 use it directly):
// A space /Game/Tests/UE5_0/IDamageable (MCP built), B space
// /Game/Tests/UE5_0/IDamageable_B (DSL built, two spaces cannot share the same asset).
//
// Probe (collaboration spec point 6) -- interfaces whole domain not yet implemented on MCP or DSL sides;
// this case written directly as "supported"; failing points are pending requirements:
// - Create interface asset: bridge currently has no interface command (AdvertisedCommands only has create_blueprint /
//   create_blueprint_struct / create_blueprint_enum family); spec takes
//   create_blueprint_interface { path, messages:[{ name, params:[{name,type}] }] }
//   (assetType side corresponds to DSL domain blueprint_interface).
// - Blueprint implements interface: create_blueprint adds implements field (same name/shape as
//   BlueprintTranslator-produced IR field def["implements"]), semantics align with engine's
//   FBlueprintEditorUtils::ImplementNewInterface -- while attaching interface, auto-build
//   implementation graphs per message signature (InterfaceGuid points to interface graph).
// - Message implementation graph already built by implements, so cannot go through create_function_graph
//   (GraphNameExists fails because graph already exists): use list_blueprint_graph to get
//   entry / result node ids (see FindTerminators in this file).
// - Call message: UE 5.0.3's UK2Node_Message inherits UK2Node_CallFunction, but its self
//   pin is PC_Object (K2Node_Message.cpp L83-88 CreateSelfPin); "cast to interface"
//   is handled by ExpandNode internally-generated UK2Node_DynamicCast (same file ExpandNode /
//   ExpandLevelStreamingHandlers) -- so A-side does not self-build Cast node, task 2 doc's
//   "cast to interface first then call message" is the message node's single call on engine side.
//   Node spec takes add_blueprint_node's kind=message (name = message name, class = interface class path).
// - Interface-typed function parameter: create_function_graph's params.type goes through
//   PinTypeTextToPinType -> BaseTypeTextToPinType -> SetObjectPinType, which emits
//   PC_Interface + the interface class for UInterface-derived classes (2026-10-08), so the
//   parameter keeps its interface type in the editor instead of degrading to a plain Object
//   reference. Connecting it to a message node's self pin (PC_Object + UObject) works through
//   the engine's own Conv_InterfaceToObject autocast (EdGraphSchema_K2.cpp L2502-2513).
//   The test passes the interface generated class path (/Game/.../IDamageable.IDamageable_C).
// - DSL side: ParserFactory.DomainMap carries BlueprintInterface -> blueprint_interface
//   (entry 50, added when the interface translator was wired in), so [BlueprintInterface] is
//   no longer rejected by DetectAssetType; method-level
//   interface implementation marked by [InterfaceImplementation] (BlueprintTranslator puts method
//   into InterfaceGraph per this and produces interfaceImplementation patch); doc's [Function]
//   would fall into custom event rather than interface implementation -- written directly as supported
//   form; [InterfaceImplementation] alias outside [Override]/[Function] noted as gap.
// - Task 2 DSL side copies doc's explicit `as` + null check forms (both supported by translator),
//   `d.ApplyDamage(5)` whether it lowers to interface message node pending verification -- test is spec.
//
// Note: mis-touch comparison (VerifyIrSnapshot) has been inserted by user instruction,
// covering all A-side and B-side assets -- the three implement/call/param blueprints on
// both sides plus the two interface assets (IDamageable / IDamageable_B).
// =========================================================================

public sealed class Blueprint_Interface : ITestCase
{
    public string Name => "Blueprint Interface (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 28;

    /// <summary>Interface asset class path (MCP side's Cast / message nodes locate interface by class).</summary>
    private const string IfaceClassA = "/Game/Tests/UE5_0/IDamageable.IDamageable_C";

    /// <summary>
    /// Gets the entry / result node ids of the specified graph. Interface message graphs are auto-built by implements,
    /// cannot be created via create_function_graph (same-name graph already exists), must read graph to locate.
    /// </summary>
    private static BlueprintTestHelper.FuncInfo FindTerminators(
        TestContext ctx, string assetPath, string graphName)
    {
        var graph = BlueprintTestHelper.ListGraph(ctx, assetPath, graphName);
        string entryId = "", resultId = "";
        foreach (var node in graph.nodes)
        {
            if (node.nodeClass == "K2Node_FunctionEntry") entryId = node.nodeId;
            else if (node.nodeClass == "K2Node_FunctionResult") resultId = node.nodeId;
        }
        ctx.AssertTrue(entryId.Length > 0 && resultId.Length > 0,
            $"graph '{graphName}' of '{assetPath}' is missing its entry/result node"
            + $" -- implements has not built this implementation graph by message signature?");
        return new BlueprintTestHelper.FuncInfo(entryId, resultId);
    }

    /// <summary>
    /// Save non-blueprint assets (interfaces): interfaces have no compile concept, so CompileAndSave
    /// cannot be used (it would send compile_blueprint). Neither creation path saves either --
    /// create_blueprint_interface only marks the package dirty, and ApplyDsl compiles + saves only
    /// blueprint-backed asset types (blueprint / level_blueprint / blueprint_function_library /
    /// blueprint_macro_library) -- interfaces are "blueprint_interface", so an explicit
    /// save is required, otherwise the asset lingers in memory dirty and the IR snapshot below is
    /// taken against unsaved state. Same as Blueprint_Switch_Tests' SaveAsset.
    /// </summary>
    private static void SaveAsset(TestContext ctx, string assetPath)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(r["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {assetPath}: {r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Implement Interface Message --
        // MCP steps: 1. Build interface asset IDamageable (message ApplyDamage(int Amount))
        //   2. Build Iface_Impl_A (Extends Actor, implements = IDamageable)
        //   3. PrintString(Amount) in ApplyDamage message implementation graph body
        const string ifaceA = "/Game/Tests/UE5_0/IDamageable";
        var ifaceCreated = BlueprintTestHelper.EditorCall(ctx, "create_blueprint_interface", new
        {
            path = ifaceA,
            messages = new[]
            {
                new
                {
                    name = "ApplyDamage",
                    @params = new[] { new { name = "Amount", type = "int" } },
                },
            },
        });
        ctx.AssertTrue(ifaceCreated["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_blueprint_interface {ifaceA}: "
            + $"{ifaceCreated["response"]?["reason"]?.GetValue<string>() ?? ""}");
        SaveAsset(ctx, ifaceA);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, ifaceA);

        const string a1 = "/Game/Tests/UE5_0/Iface_Impl_A";
        var implCreated = BlueprintTestHelper.EditorCall(ctx, "create_blueprint", new
        {
            path = a1,
            parentClass = "Actor",
            implements = new[] { ifaceA },
        });
        ctx.AssertTrue(implCreated["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_blueprint {a1} (implements {ifaceA}): "
            + $"{implCreated["response"]?["reason"]?.GetValue<string>() ?? ""}");

        var g1 = FindTerminators(ctx, a1, "ApplyDamage");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "ApplyDamage", "PrintString", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, g1.entryId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, g1.entryId, "Amount", p1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", g1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc; interface block needs Path -- domain attribute must carry asset path;
        // [InterfaceImplementation] is translator-supported interface implementation marker;
        // how bare interface name in [Implements(typeof(IDamageable))] resolves to
        // IDamageable_B asset (file-level `using IDamageable = "path";` alias is
        // core syntax section 11.5's established mechanism) pending verification -- test is spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        // ApplyDsl compiles + saves only blueprint-backed asset types; the DSL-created interface
        // is "blueprint_interface" (no compile concept) -- add an explicit save instruction.
        const string ifaceB = "/Game/Tests/UE5_0/IDamageable_B";
        SaveAsset(ctx, ifaceB);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, ifaceB);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Iface_Impl_B");

        // -- Subtask 2: Call Interface Message --
        // MCP steps: Iface_Call_A (Extends Actor)
        //   Function Hit(target: Actor): message ApplyDamage(self = target, Amount = 5)
        const string a2 = "/Game/Tests/UE5_0/Iface_Call_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Hit",
            new[] { new { name = "target", type = "Actor" } });
        var msg2 = BlueprintTestHelper.AddNode(ctx, a2, "Hit", "ApplyDamage", 300, 0,
            IfaceClassA, kind: "message");
        BlueprintTestHelper.SetPinDefault(ctx, a2, msg2.nodeId, "Amount", "5");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", msg2.nodeId, "execute");
        // Message node's self pin is object pin (engine internally casts to interface), connect target directly.
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "target", msg2.nodeId, "self");
        BlueprintTestHelper.Connect(ctx, a2, msg2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc: explicit cast to interface + null check + call message)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Iface_Call_B");

        // -- Subtask 3: Interface as Function Parameter Type --
        // MCP steps: Iface_Param_A (Extends Actor)
        //   Function Deal(d: IDamageable): message ApplyDamage(self = d, Amount = 1)
        const string a3 = "/Game/Tests/UE5_0/Iface_Param_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Deal",
            new[] { new { name = "d", type = IfaceClassA } });
        var msg3 = BlueprintTestHelper.AddNode(ctx, a3, "Deal", "ApplyDamage", 300, 0,
            IfaceClassA, kind: "message");
        BlueprintTestHelper.SetPinDefault(ctx, a3, msg3.nodeId, "Amount", "1");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", msg3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "d", msg3.nodeId, "self");
        BlueprintTestHelper.Connect(ctx, a3, msg3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc: interface type as function parameter + call message)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Iface_Param_B");

        await Task.CompletedTask;
    }
}
