// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 35 - Blueprint Enum For Each (UE 5.0) - from Tasks/UE_5.0/Blueprint-EnumForEach.md
//
// Probe (collaboration spec point 6):
// - UK2Node_ForEachElementInEnum exists in 5.0.3
//   (Editor\BlueprintGraph\Classes\K2Node_ForEachElementInEnum.h:17, menu category Enum,
//   cpp:335, title "ForEach {EnumName}" cpp:197) but is **not** in the bridge's native node
//   vocabulary (CreateNativeFlowNode, BlueprintGraph.cpp:148-233) -> the MCP node step
//   cannot be built. Pending requirement: bridge support for this node.
// - **Doc's pin names are wrong**: real pins are input `Execute` + advanced bool `SkipHidden`
//   (cpp:140-168) and outputs `LoopBody`, `EnumValue`, `Then` (friendly name "Completed",
//   cpp:167). There is **no `Array` output** -- the loop index is a compile-time temporary
//   pin only (FForExpandNodeHelper::ArrayIndexOutPin, cpp:29-30/66-70). And the enum type
//   is a node property `TObjectPtr<UEnum> Enum` (h:21-22), not a pin, so it would also
//   need a payload field to be set.
// - DSL side: doc writes `foreach (EColor e in System.Enum.GetValues<EColor>())`, i.e. C#
//   reflection over an enum -- there is no such thing as a blueprint enum value list, and
//   the translator has no System.Enum model. Copied as written; the B side is therefore a
//   spec for "how should enum iteration be expressed", not a runnable shape today.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transcribing task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
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
        //   -> For Each Element in EColor -> PrintString
        CreateColorEnum(ctx);
        const string a1 = "/Game/Tests/UE5_0/EnumFor_Each_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Walk");
        // Pending bridge capability: the ForEachElementInEnum node (see file header).
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: iterate and read the entry name --
        // MCP steps: EnumFor_Name_A (Extends Actor) + List()
        //   -> For Each Element in EColor -> Enum to String -> PrintString
        const string a2 = "/Game/Tests/UE5_0/EnumFor_Name_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "List");
        // Pending bridge capability: the ForEachElementInEnum node. Also note the doc's
        // "Enum to String" step has no 5.0.3 equivalent for entry *names*: there is no
        // EnumToString anywhere in Runtime\Engine; UKismetStringLibrary::Conv_ByteToString
        // ("To String (Byte)", KismetStringLibrary.h:32-33) only yields the numeric string,
        // and the name-capable helpers (UKismetNodeHelperLibrary::GetEnumeratorUserFriendlyName,
        // KismetNodeHelperLibrary.h:130-131) are BlueprintInternalUseOnly.
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: count entries --
        // MCP steps: EnumFor_Count_A (Extends Actor) + Count()
        //   -> For Each Element in EColor -> counter -> PrintString(count)
        const string a3 = "/Game/Tests/UE5_0/EnumFor_Count_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Count");
        // Pending bridge capability: the ForEachElementInEnum node (see file header).
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}