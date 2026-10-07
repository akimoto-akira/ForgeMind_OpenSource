// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 21 · Blueprint Function (UE 5.0)  from Tasks/UE_5.0/Blueprint-Function.md
// Probe (Spec Point 6): DSL [Function] / [Function(Pure)] supported in the translator;
// MCP step can fully express via CreateFunction / CreateFunctionWithInputs (params → entry pins
// a/b/x/v, outputs → result node ReturnValue/X/Y). Only the "pure function" flag
// (bIsPureFunc) helper does not expose a corresponding bridge command; needs bridge set_blueprint_function_pure
// backfill after (DSL uses [Function(Pure)] to express).
// Mis-touch comparison (VerifyIrSnapshot): each asset in MCP step and DSL path each build comparison snapshot,
// catching subsequent changes that accidentally damage already-tested asset structures; first run only builds snapshots, no failure.
// =========================================================================

public sealed class Blueprint_Function : ITestCase
{
    public string Name => "Blueprint Function (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 21;

    public async Task Run(TestContext ctx)
    {
        // ── Task 1: Parameterless void function ──
        // MCP step: Func_Void_A (Extends Actor) + Function Hello → PrintString("Hi")
        const string a1 = "/Game/Tests/UE5_0/Func_Void_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Hello");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Hello", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "Hi");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Func_Void_B");

        // ── Task 2: With parameters and return value ──
        // MCP step: Func_Ret_A + Function Add(int a, int b) → ReturnValue = a + b
        const string a2 = "/Game/Tests/UE5_0/Func_Ret_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Add",
            new[] { new { name = "a", type = "int" }, new { name = "b", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "int" } });
        var add2 = BlueprintTestHelper.AddNode(ctx, a2, "Add", "Add_IntInt", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", add2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", add2.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a2, add2.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
        // Add_IntInt is a **pure function** node (no exec pins, verified: has no pin named 'then',
        // pins = self/A/B/ReturnValue) → execution chain goes entry direct to result node, cannot chain through it.
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Func_Ret_B");

        // ── Task 3: Pure function ──
        // MCP step: Func_Pure_A + Function Double(int x) → ReturnValue = x * 2
        //   Pure flag is issued via create_function_graph's 'pure' field (bridge already supports,
        //   compiles to FUNC_BlueprintPure: no exec pins).
        const string a3 = "/Game/Tests/UE5_0/Func_Pure_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Double",
            new[] { new { name = "x", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "int" } },
            pure: true);
        var mul3 = BlueprintTestHelper.AddNode(ctx, a3, "Double", "Multiply_IntInt", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, mul3.nodeId, "B", "2");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "x", mul3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, mul3.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
        // The pure function's entry / result nodes **still have exec pins** (UE's UK2Node_FunctionEntry /
        // FunctionResult AllocateDefaultPins unconditionally creates Then / Execute pins; the purity only
        // affects call sites, not the graph itself). The bridge create_function_graph skips auto-connect
        // for pure (!bPure guard), but the exec pins themselves exist — so still need to explicitly wire
        // entry.then → result.execute, otherwise visually "function header and return node are not linked"
        // (Task 2/4 same form).
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Func_Pure_B");

        // ── Task 4: Multiple outputs (out parameters) ──
        // MCP step: Func_Out_A + Function Decompose(Vector v, out X, out Y)
        //   → BreakVector(v) connects to result's X / Y
        const string a4 = "/Game/Tests/UE5_0/Func_Out_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a4);
        var f4 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a4, "Decompose",
            new[] { new { name = "v", type = "Vector" } },
            outputs: new[] { new { name = "X", type = "float" }, new { name = "Y", type = "float" } });
        var br4 = BlueprintTestHelper.AddNode(ctx, a4, "Decompose", "BreakVector", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a4, f4.entryId, "v", br4.nodeId, "InVec");
        BlueprintTestHelper.Connect(ctx, a4, br4.nodeId, "X", f4.resultId, "X");
        BlueprintTestHelper.Connect(ctx, a4, br4.nodeId, "Y", f4.resultId, "Y");
        BlueprintTestHelper.Connect(ctx, a4, f4.entryId, "then", f4.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a4);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a4);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Func_Out_B");

        await Task.CompletedTask;
    }
}
