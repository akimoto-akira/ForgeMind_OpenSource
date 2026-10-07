// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 22 · Blueprint Function Library (UE 5.0)  — from Tasks/UE_5.0/Blueprint-FunctionLib.md
// Probe (Spec Point 6): DSL [BlueprintFunctionLibrary] attribute has no implementation evidence
// in the translator (only hits [Function]/[Macro], etc., not BlueprintFunctionLibrary); MCP also
// has no create_blueprint_function_library dedicated helper, so "create function library asset" is
// placeholder via CreateBlueprint(parentClass="BlueprintFunctionLibrary") — the parent class string and static
// function nature are both awaiting bridge confirmation. Static function bodies within the library
// can be expressed via CreateFunctionWithInputs; the ownerClass (library generated class "Lib_Static_A_C")
// for cross-blueprint calls to Square is awaiting bridge confirmation.
// Note: mis-touch comparison (VerifyIrSnapshot) has been inserted by user instruction, covering all A-side
// and B-side assets.
// =========================================================================

public sealed class Blueprint_FunctionLibrary : ITestCase
{
    public string Name => "Blueprint Function Library (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 22;

    public async Task Run(TestContext ctx)
    {
        // ── Task 1: Static function in library ──
        // MCP step: Create Blueprint Function Library Lib_Static_A + static function Square(x)
        const string l1 = "/Game/Tests/UE5_0/Lib_Static_A";
        // parentClass "BlueprintFunctionLibrary" awaiting bridge confirmation (helper does not expose dedicated create command).
        BlueprintTestHelper.CreateBlueprint(ctx, l1, "BlueprintFunctionLibrary");
        var fl1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, l1, "Square",
            new[] { new { name = "x", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "int" } });
        var mul1 = BlueprintTestHelper.AddNode(ctx, l1, "Square", "Multiply_IntInt", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, l1, mul1.nodeId, "B", "2");
        BlueprintTestHelper.Connect(ctx, l1, fl1.entryId, "x", mul1.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, l1, mul1.nodeId, "ReturnValue", fl1.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, l1, fl1.entryId, "then", fl1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, l1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, l1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Lib_Static_B");

        // ── Task 2: Cross-blueprint library function call ──
        // MCP step: Reuse Lib_Static_A (contains Square) + blueprint Lib_Call_A function Run
        //   calling Lib_Static_A.Square(3) wired to PrintString
        const string l2 = "/Game/Tests/UE5_0/Lib_Call_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, l2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, l2, "Run");
        // ownerClass uses the library asset generated class "Lib_Static_A_C" (awaiting bridge confirmation).
        var sq2 = BlueprintTestHelper.AddNode(ctx, l2, "Run", "Square", 300, 0, "Lib_Static_A_C");
        BlueprintTestHelper.SetPinDefault(ctx, l2, sq2.nodeId, "x", "3");
        var p2 = BlueprintTestHelper.AddNode(ctx, l2, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, l2, sq2.nodeId, "ReturnValue", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, l2, f2.entryId, "then", sq2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, l2, sq2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, l2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, l2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, l2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Lib_Call_B");

        // ── Task 3: Library function with multiple parameters ──
        // MCP step: Create function library Lib_Multi_A + static function AddMul(a,b,c) → (a+b)*c
        const string l3 = "/Game/Tests/UE5_0/Lib_Multi_A";
        BlueprintTestHelper.CreateBlueprint(ctx, l3, "BlueprintFunctionLibrary");
        var fl3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, l3, "AddMul",
            new[] { new { name = "a", type = "int" }, new { name = "b", type = "int" }, new { name = "c", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "int" } });
        var add3 = BlueprintTestHelper.AddNode(ctx, l3, "AddMul", "Add_IntInt", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, l3, fl3.entryId, "a", add3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, l3, fl3.entryId, "b", add3.nodeId, "B");
        var mul3 = BlueprintTestHelper.AddNode(ctx, l3, "AddMul", "Multiply_IntInt", 600, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, l3, add3.nodeId, "ReturnValue", mul3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, l3, fl3.entryId, "c", mul3.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, l3, mul3.nodeId, "ReturnValue", fl3.resultId, "ReturnValue");
        // Add_IntInt / Multiply_IntInt are both pure function nodes (no exec pins); the execution chain
        // aligns with Task 1: entry.then → result.execute single direct wire; pure functions only participate in the data chain.
        BlueprintTestHelper.Connect(ctx, l3, fl3.entryId, "then", fl3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, l3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, l3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Lib_Multi_B");

        await Task.CompletedTask;
    }
}
