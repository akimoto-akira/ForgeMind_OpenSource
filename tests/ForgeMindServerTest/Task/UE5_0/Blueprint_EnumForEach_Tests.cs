// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 35 - Blueprint Enum For Each (UE 5.0) - from Tasks/UE_5.0/blueprint-enum-for-each.md
//
// Probe (collaboration spec point 6):
// - **Every node step below is attempted as written** -- support verification belongs to the
//   implementation phase (spec point 6: write tests directly assuming supported).
//   `ForEachElementInEnum` had no entry in the bridge's native node vocabulary, and the case
//   failed with "Node is unresolved (zero pins after compile)" -- the requirement signal.
//   Bridge support was added on 2026-10-10 (native vocabulary + creation + 'enum' binding).
// - UK2Node_ForEachElementInEnum exists in the engine
//   (Editor\BlueprintGraph\Classes\K2Node_ForEachElementInEnum.h:17, menu category Enum,
//   cpp:335, title "ForEach {EnumName}" cpp:197). Real pins (cpp:140-168): input `Execute`,
//   advanced bool `SkipHidden`; outputs `LoopBody`, `EnumValue`, `Then` (friendly name
//   "Completed"). There is **no `Array` output** -- the loop index is a compile-time
//   temporary pin only (FForExpandNodeHelper::ArrayIndexOutPin, cpp:29-30/66-70). The enum is
//   a node property `TObjectPtr<UEnum> Enum` (h:21-22), not a pin, so it is passed through
//   `enumName` here (same payload key SwitchOnEnum uses).
// - Enum -> string: 5.0.3 has **no** EnumToString anywhere in Runtime\Engine;
//   UKismetStringLibrary::Conv_ByteToString ("To String (Byte)", KismetStringLibrary.h:32-33)
//   is what actually exists and only yields the numeric string. Subtask 2 uses it.
// - DSL side: doc writes `foreach (EColor e in System.Enum.GetValues<EColor>())`, i.e. C#
//   reflection over an enum. The translator now lowers that form to the engine's
//   UK2Node_ForEachElementInEnum (2026-10-10). The enum is referenced by its **asset leaf
//   name** `EColor_B`: this test's enum asset is created on the MCP side, not by an [Enum]
//   DSL block, so no declaration-name alias (EColor → EColor_B) is registered for the session
//   -- unlike Order 34, where the DSL declares the enum and the alias comes from that block.
//
// Note: diff comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9), covering all A-side and B-side assets.
// =========================================================================

public sealed class Blueprint_EnumForEach : ITestCase
{
    public string Name => "Blueprint Enum For Each (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 35;

    /// <summary>Enum asset EColor (Red/Green/Blue), prerequisite for all three subtasks.</summary>
    private static void CreateColorEnum(TestContext ctx)
    {
        const string ecolor = "/Game/Tests/UE5_0/EColor_B";
        BlueprintTestHelper.EditorCall(ctx, "create_blueprint_enum", new { path = ecolor });
        foreach (var entry in new[] { "Red", "Green", "Blue" })
        {
            BlueprintTestHelper.EditorCall(ctx, "add_enum_entry", new { path = ecolor, name = entry });
        }
        var save = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = ecolor });
        ctx.AssertTrue(save["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {ecolor}: {save["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: iterate enum entries --
        // MCP steps: EnumFor_Each_A (Extends Actor) + Walk()
        //   -> For Each Element in EColor -> EnumValue -> PrintString
        CreateColorEnum(ctx);
        const string a1 = "/Game/Tests/UE5_0/EnumFor_Each_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Walk");
        var each1 = BlueprintTestHelper.AddNode(ctx, a1, "Walk", "ForEachElementInEnum", 300, 0,
            enumName: "EColor_B");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "Walk", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", each1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, each1.nodeId, "EnumValue", print1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, each1.nodeId, "LoopBody", print1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, each1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EnumFor_Each_B");

        // -- Subtask 2: iterate and read the entry name --
        // MCP steps: EnumFor_Name_A (Extends Actor) + List()
        //   -> For Each Element in EColor -> Enum to String -> PrintString
        const string a2 = "/Game/Tests/UE5_0/EnumFor_Name_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "List");
        var each2 = BlueprintTestHelper.AddNode(ctx, a2, "List", "ForEachElementInEnum", 300, 0,
            enumName: "EColor_B");
        var str2 = BlueprintTestHelper.AddNode(ctx, a2, "List", "Conv_ByteToString", 550, 0,
            "KismetStringLibrary");
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "List", "PrintString", 800, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", each2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, each2.nodeId, "EnumValue", str2.nodeId, "InByte");
        BlueprintTestHelper.Connect(ctx, a2, str2.nodeId, "Return Value", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, each2.nodeId, "LoopBody", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, each2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EnumFor_Name_B");

        // -- Subtask 3: count entries --
        // MCP steps: EnumFor_Count_A (Extends Actor) + Count()
        //   -> For Each Element in EColor -> local counter -> PrintString(count)
        const string a3 = "/Game/Tests/UE5_0/EnumFor_Count_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Count",
            locals: new[] { new { name = "N", type = "int" } });
        var each3 = BlueprintTestHelper.AddNode(ctx, a3, "Count", "ForEachElementInEnum", 300, 0,
            enumName: "EColor_B");
        var getN3 = BlueprintTestHelper.AddNode(ctx, a3, "Count", "N", 550, -150, kind: "getter");
        var add3 = BlueprintTestHelper.AddNode(ctx, a3, "Count", "Add_DoubleDouble", 750, -150,
            "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, add3.nodeId, "B", "1.0");
        var setN3 = BlueprintTestHelper.AddNode(ctx, a3, "Count", "N", 950, -150, kind: "setter");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "Count", "PrintString", 800, 100,
            "KismetSystemLibrary");
        var getN3b = BlueprintTestHelper.AddNode(ctx, a3, "Count", "N", 550, 100, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", each3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, getN3.nodeId, "N", add3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, add3.nodeId, "Return Value", setN3.nodeId, "N");
        BlueprintTestHelper.Connect(ctx, a3, each3.nodeId, "LoopBody", setN3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, getN3b.nodeId, "N", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, each3.nodeId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, print3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EnumFor_Count_B");

        await Task.CompletedTask;
    }
}