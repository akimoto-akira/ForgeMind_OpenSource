// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 29 - Blueprint Struct (UE 5.0) - from Tasks/UE_5.0/blueprint-struct.md
//
// Probe (collaboration spec point 6):
// - MakeStruct / BreakStruct are bridge's native data construction nodes (add_blueprint_node's
//   name = MakeStruct / BreakStruct + 'struct' field takes struct name or path;
//   see HandleAddBlueprintNode's ConfigureNativeFlowNode and
//   add_blueprint_node's reason text). 'struct' field not exposed by
//   BlueprintTestHelper.AddNode -- this file directly sends editor commands to get nodeId
//   (same pattern as Blueprint_Switch_Tests directly sending create_blueprint_enum).
//   Pins are engine real names: Make's struct output pin name = struct name (StructType->GetFName(),
//   K2Node_MakeStruct.cpp L131), member input pins by property name (Vector -> X / Y / Z);
//   Break's struct input pin name is also struct name (K2Node_BreakStruct.cpp L180), member output
//   pins same name. Vector pin defaults use engine's bare three-number format (set_pin_default_value's
//   pre-validation of FVector pins: comma-separated three numbers).
// - Custom struct asset: create_blueprint_struct { path, members:[{name,type}] }
//   exists; structs have no compile concept, saving only goes through save_asset (same as
//   Blueprint_Switch_Tests' EState_A enum handling, not CompileAndSave). One per space: A-side FHealth_A,
//   B-side built by DSL's [Struct] domain block as FHealth_B.
// - User-defined struct as type: create_function_graph's params/outputs.type goes through
//   PinTypeTextToPinType, whose lexical table only has base types + UClass names + container forms, no
//   user-defined structs -- task 3's return type written directly as struct asset path (test is spec).
// - DSL side: doc task 3 writes public struct inside [Blueprint] block (domain attribute on struct
//   not valid, and UserDefinedStructTranslator only recognizes [Struct] domain block) -- split into
//   independent [Struct] block + blueprint block reference per supported form;
//   `new FHealth(100, 100)'s location construction and return type FHealth -> struct asset resolution
//   pending verification, test is spec.
//   Task 1 / task 2 DSL copied from doc.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transwriting task docs.
// =========================================================================

public sealed class Blueprint_Struct : ITestCase
{
    public string Name => "Blueprint Struct (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 29;

    /// <summary>
    /// Directly sends add_blueprint_node to build MakeStruct / BreakStruct and retrieve nodeId.
    /// These two native nodes must carry 'struct' field (struct name or asset path), which
    /// is not in BlueprintTestHelper.AddNode's parameter list.
    /// </summary>
    private static string AddStructNode(TestContext ctx, string assetPath, string graphName,
        string nodeName, string structName, int x, int y)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_node", new
        {
            path = assetPath,
            graphName,
            name = nodeName,
            x,
            y,
            @struct = structName,
        });
        ctx.AssertTrue(r["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_node {nodeName}({structName}) @ {graphName}: "
            + $"{r["response"]?["reason"]?.GetValue<string>() ?? ""}");
        return r["response"]?["nodeId"]?.GetValue<string>() ?? "";
    }

    /// <summary>
    /// Save non-blueprint assets (structs etc.): structs have no compile concept, cannot use CompileAndSave
    /// (that would send compile_blueprint for struct assets) -- same as Blueprint_Switch_Tests.
    /// </summary>
    private static void SaveAsset(TestContext ctx, string assetPath)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(r["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {assetPath}: {r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Make Struct (built-in Vector) --
        // MCP steps: Struct_Make_A (Extends Actor) + function Build() : Vector
        //   -> Make Vector (X=1, Y=2, Z=3) connects ReturnValue
        const string a1 = "/Game/Tests/UE5_0/Struct_Make_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Build", Array.Empty<object>(),
            outputs: new[] { new { name = "ReturnValue", type = "vector" } });
        var make1 = AddStructNode(ctx, a1, "Build", "MakeStruct", "Vector", 300, 0);
        BlueprintTestHelper.SetPinDefault(ctx, a1, make1, "X", "1.0");
        BlueprintTestHelper.SetPinDefault(ctx, a1, make1, "Y", "2.0");
        BlueprintTestHelper.SetPinDefault(ctx, a1, make1, "Z", "3.0");
        BlueprintTestHelper.Connect(ctx, a1, make1, "Vector", f1.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: Break Struct --
        // MCP steps: Struct_Break_A (Extends Actor) + function Show(v: Vector)
        //   -> Break Vector decompose v, X connects PrintString
        const string a2 = "/Game/Tests/UE5_0/Struct_Break_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Show",
            new[] { new { name = "v", type = "vector" } });
        var brk2 = AddStructNode(ctx, a2, "Show", "BreakStruct", "Vector", 300, 0);
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Show", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "v", brk2, "Vector");
        BlueprintTestHelper.Connect(ctx, a2, brk2, "X", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: Custom Struct --
        // MCP steps: 1. Build struct asset FHealth_A (Cur : int, Max : int)
        //   2. Struct_Custom_A (Extends Actor) + function Make() : FHealth
        //   3. Make FHealth (Cur=100, Max=100) connects ReturnValue
        const string healthA = "/Game/Tests/UE5_0/FHealth_A";
        var structCreated = BlueprintTestHelper.EditorCall(ctx, "create_blueprint_struct", new
        {
            path = healthA,
            members = new[]
            {
                new { name = "Cur", type = "int" },
                new { name = "Max", type = "int" },
            },
        });
        ctx.AssertTrue(structCreated["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_blueprint_struct {healthA}: "
            + $"{structCreated["response"]?["reason"]?.GetValue<string>() ?? ""}");
        SaveAsset(ctx, healthA);

        const string a3 = "/Game/Tests/UE5_0/Struct_Custom_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Make", Array.Empty<object>(),
            outputs: new[] { new { name = "ReturnValue", type = healthA } });
        var make3 = AddStructNode(ctx, a3, "Make", "MakeStruct", healthA, 300, 0);
        BlueprintTestHelper.SetPinDefault(ctx, a3, make3, "Cur", "100");
        BlueprintTestHelper.SetPinDefault(ctx, a3, make3, "Max", "100");
        BlueprintTestHelper.Connect(ctx, a3, make3, "FHealth_A", f3.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc, struct block as standalone [Struct] domain per file header notes)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}
