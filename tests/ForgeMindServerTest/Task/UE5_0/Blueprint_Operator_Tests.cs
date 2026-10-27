// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 68 - Blueprint Operators (UE 5.0) - from Tasks/UE_5.0/Blueprint_Operator.md
//
// Probe (collaboration spec point 6):
// - UE 5.0.3 has UK2Node_PromotableOperator (the unified "+ - * /" / comparison / logic node),
//   but the bridge's add_blueprint_node has **no** operator kind and no native-flow-node branch
//   for it (IsNativeFlowNodeName lists Branch / Sequence / Switch / Cast / Select / AddComponent
//   / MakeArray / ... only). What *is* resolvable through FindFunctionByName — and therefore what
//   the MCP side uses per the "query, do not guess" rule (spec point 8) — are the
//   UKismetMathLibrary UFUNCTIONs behind those operators (engine index 5.0):
//     Add_IntInt(int32 A, int32 B) : int32        DisplayName="int + int"        @:312
//     Multiply_IntInt(int32 A, int32 B) : int32   DisplayName="int * int"        @:300
//     Greater_IntInt(int32 A, int32 B) : bool     DisplayName="integer > integer" @:324
//     BooleanAND(bool A, bool B) : bool           DisplayName="AND Boolean"      @:220
//     Not_PreBool(bool A) : bool                  DisplayName="NOT Boolean"      @:208
//   All five are BlueprintPure, so they carry no exec pins and are chained data-first, then
//   wired into the function result node ("ReturnValue", same form as Blueprint_ArrayOps).
// - Operator pin names are the C++ parameter names A / B with a "ReturnValue" output (UE 5.0
//   KismetMathLibrary convention; the blueprint editor shows the friendly "+"/"*" titles).
// - DSL side: the doc writes C# operators ((a + b) * 2, a > b, a && !b) — kept verbatim; the
//   operator-expression lowering is the requirement this case pins.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison). The B-side baselines pin the operator
// lowering itself: Add_IntInt / Multiply_IntInt for arithmetic, Greater_IntInt for the
// comparison, and BooleanAND + Not_PreBool for `a && !b`.
// =========================================================================

public sealed class Blueprint_Operator : ITestCase
{
    public string Name => "Blueprint Operators (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 68;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: arithmetic operators --
        // MCP steps: Op_Arith_A (Extends Actor) + function Calc(int a, int b)
        //   -> (a + b) * 2 -> Return
        const string a1 = "/Game/Tests/UE5_0/Op_Arith_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Calc",
            new[] { new { name = "a", type = "int" }, new { name = "b", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "int" } });
        var add1 = BlueprintTestHelper.AddNode(ctx, a1, "Calc", "Add_IntInt", 300, -100,
            "KismetMathLibrary");
        var mul1 = BlueprintTestHelper.AddNode(ctx, a1, "Calc", "Multiply_IntInt", 500, 0,
            "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "a", add1.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "b", add1.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a1, add1.nodeId, "ReturnValue", mul1.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a1, mul1.nodeId, "B", "2");
        // Pure chain: the function result is wired from the last operator only.
        BlueprintTestHelper.Connect(ctx, a1, mul1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Op_Arith_B");

        // -- Subtask 2: comparison operator --
        // MCP steps: Op_Cmp_A (Extends Actor) + function Test(int a, int b) -> a > b -> Return
        const string a2 = "/Game/Tests/UE5_0/Op_Cmp_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Test",
            new[] { new { name = "a", type = "int" }, new { name = "b", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "bool" } });
        var gt2 = BlueprintTestHelper.AddNode(ctx, a2, "Test", "Greater_IntInt", 300, 0,
            "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", gt2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", gt2.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a2, gt2.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Op_Cmp_B");

        // -- Subtask 3: logical operators --
        // MCP steps: Op_Logic_A (Extends Actor) + function Test(bool a, bool b)
        //   -> a && !b -> Return
        const string a3 = "/Game/Tests/UE5_0/Op_Logic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Test",
            new[] { new { name = "a", type = "bool" }, new { name = "b", type = "bool" } },
            outputs: new[] { new { name = "ReturnValue", type = "bool" } });
        var not3 = BlueprintTestHelper.AddNode(ctx, a3, "Test", "Not_PreBool", 300, -100,
            "KismetMathLibrary");
        var and3 = BlueprintTestHelper.AddNode(ctx, a3, "Test", "BooleanAND", 500, 0,
            "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "b", not3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "a", and3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, not3.nodeId, "ReturnValue", and3.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a3, and3.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Op_Logic_B");

        await Task.CompletedTask;
    }
}
