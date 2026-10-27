// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 62 - Blueprint Text (UE 5.0) - from Tasks/UE_5.0/blueprint-text.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit.
// Format("HP={0}", hp) maps to UK2Node_FormatText -- a special node (not a
// regular CallFunction); the format string goes on the "Format" pin, the {0}
// argument on the "0" pin, output on "Result". The DSL Format() syntax is not
// formally specified in DSL-syntax.md -- written as-is so the run either verifies
// it or exposes the Format-Text gap as a spec item. Concat_StrStr (string
// append) and Conv_IntToString (int ToString) are BlueprintPure on
// UKismetStringLibrary -- pure nodes, exec goes directly entry.then ->
// result.execute.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Format("HP={0}", hp) -> string
//   Task 2 - a + b (string concatenation)
//   Task 3 - n.ToString() (int to string)
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-20 per user instruction,
// after this case was confirmed by visual inspection. One per asset -- A side after
// CompileAndSave, DSL side after ApplyDsl (6 total: Text_Fmt_A/B, Text_App_A/B,
// Text_Num_A/B). Same convention as Blueprint_ShapeTrace_Tests / Blueprint_Destroy_Tests /
// Blueprint_Vector_Tests / Blueprint_Camera_Tests. Baselines are seeded on the first
// run; from the second run onward they actually diff. Task 2 and Task 3 are exactly the
// two forms this case guards: `a + b` must stay a single Concat_StrStr (it had silently
// degraded to Add_DoubleDouble with string->number casts) and `n.ToString()` must wire
// the value straight to the FString pin (it had emitted class="int").
// =========================================================================

public sealed class Blueprint_Text : ITestCase
{
    public string Name => "Blueprint Text (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 62;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    private static void BuildTask1(TestContext ctx)
    {
        {
            const string a1 = "/Game/Tests/UE5_0/Text_Fmt_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Build",
                new[] { new { name = "hp", type = "int" } },
                outputs: new[] { new { name = "ReturnValue", type = "string" } });
            var fmt = BlueprintTestHelper.AddNode(ctx, a1, "Build", "FormatText", 300, 0);
            BlueprintTestHelper.SetPinDefault(ctx, a1, fmt.nodeId, "Format", "HP={0}");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "hp", fmt.nodeId, "0");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, fmt.nodeId, "Result", f1.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison (spec point 9: inserted per user instruction)
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Text_Fmt_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Text_App_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Cat",
                new[] { new { name = "a", type = "string" },
                        new { name = "b", type = "string" } },
                outputs: new[] { new { name = "ReturnValue", type = "string" } });
            var cat = BlueprintTestHelper.AddNode(ctx, a2, "Cat", "Concat_StrStr", 300, 0,
                "KismetStringLibrary");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", cat.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", cat.nodeId, "B");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, cat.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Text_App_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Text_Num_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Num",
                new[] { new { name = "n", type = "int" } },
                outputs: new[] { new { name = "ReturnValue", type = "string" } });
            var conv = BlueprintTestHelper.AddNode(ctx, a3, "Num", "Conv_IntToString", 300, 0,
                "KismetStringLibrary");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "n", conv.nodeId, "InInt");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a3, conv.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Text_Num_B");
    }
}