// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 46 - Blueprint Math Library (UE 5.0) - from Tasks/UE_5.0/blueprint-math.md
//
// Tasks (each: one MCP-stepped asset + one DSL asset, "one task two assets" Q4):
//   Task 1 - Vector arithmetic:  Move(Vector a, Vector b) => a + b
//   Task 2 - Lerp + Clamp:       LerpIt(float a, float b, float alpha) => Math.Lerp(a, b, alpha)
//                                ClampIt(float x) => Math.Clamp(x, 0, 100)
//   Task 3 - Dot product:        DotIt(Vector a, Vector b) => Vector.Dot(a, b)
//   (Doc question 2 title is "Lerp / Clamp"; the concrete steps originally gave only the
//    Clamp half, leaving Math_Lerp_A/B named "Lerp" while containing just Clamp. Per
//    user direction 2026-10-15 the Lerp half is now also constructed so the asset name
//    matches its content. Sin / Cos / Cross remain unconstructed - not invented here.)
//
// Probe (collaboration spec point 6):
// - A-side math nodes are expressed through engine-real UKismetMathLibrary static
//   functions (the same ownerClass the translator targets for int math in
//   Blueprint_Function_Tests):
//     Task 1 a+b -> Add_VectorVector  (pins A/B/ReturnValue, returns FVector)
//     Task 2     -> FClamp            (pins Value/Min/Max/ReturnValue, returns double)
//     Task 2     -> Lerp              (pins A/B/Alpha/ReturnValue, returns double)
//     Task 3     -> Dot_VectorVector  (pins A/B/ReturnValue, returns double)
//   NOTE: the engine BP node names are FClamp / Dot_VectorVector - NOT "Clamp" /
//   "DotProduct" (those names do not exist; verified against KismetMathLibrary.h).
//   All three are pure function nodes (no exec pins), so the exec chain is
//   entry -> result directly (same form as Blueprint_Function_Tests task 2).
// - Engine math functions return double, so the Task 2/3 A-side function outputs are
//   typed "double" (bridge accepts the "double" type string -> PC_Double) to keep the
//   graph buildable; the doc's DSL keeps "float" verbatim (see next point).
// - DSL side: the doc's `a + b` / `Math.Clamp` / `Vector.Dot` forms are kept verbatim.
//   All three are now validated by this case (not a pinned gap): the translator
//   lowers `Vector + Vector` to Add_VectorVector, `Math.Clamp` to FClamp,
//   `Vector.Dot(a, b)` to Dot_VectorVector (KismetMathLibrary) - matching the A-side
//   node forms. The B-side runs as a real translator test; a DSL rejection / type
//   gap here would be a genuine defect to fix, not a reason to shrink the task.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction
// (collaboration spec point 9) after the case was visually confirmed. Each A-side and
// DSL-side asset builds its own graph-level IR baseline on first run (no failure);
// later runs diff against it, catching accidental damage to already-tested blueprint
// structures (per-asset self-comparison, not A/B cross-comparison).
// =========================================================================

public sealed class Blueprint_Math : ITestCase
{
    public string Name => "Blueprint Math (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 46;

    public async Task Run(TestContext ctx)
    {
        // -- Task 1: Vector arithmetic --
        // MCP step: Math_Vec_A (Extends Actor) + function Move(Vector a, Vector b) =>
        //   a + b -> ReturnValue (via KismetMathLibrary::Add_VectorVector, pure).
        const string a1 = "/Game/Tests/UE5_0/Math_Vec_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Move",
            new[] { new { name = "a", type = "Vector" }, new { name = "b", type = "Vector" } },
            outputs: new[] { new { name = "ReturnValue", type = "Vector" } });
        var add1 = BlueprintTestHelper.AddNode(ctx, a1, "Move", "Add_VectorVector", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "a", add1.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "b", add1.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a1, add1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        // Pure node: exec chain goes entry -> result directly (cannot chain through it).
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (doc form, verbatim)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Math_Vec_B");

        // -- Task 2: Lerp + Clamp --
        // MCP step: Math_Lerp_A + function ClampIt(double x) => FClamp(x, 0, 100)
        //   (KismetMathLibrary::FClamp, pure; engine BP clamp node, not "Clamp").
        //   Output typed double to match FClamp's return (buildable).
        const string a2 = "/Game/Tests/UE5_0/Math_Lerp_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "ClampIt",
            new[] { new { name = "x", type = "double" } },
            outputs: new[] { new { name = "ReturnValue", type = "double" } });
        var clamp2 = BlueprintTestHelper.AddNode(ctx, a2, "ClampIt", "FClamp", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "x", clamp2.nodeId, "Value");
        BlueprintTestHelper.SetPinDefault(ctx, a2, clamp2.nodeId, "Min", "0");
        BlueprintTestHelper.SetPinDefault(ctx, a2, clamp2.nodeId, "Max", "100");
        BlueprintTestHelper.Connect(ctx, a2, clamp2.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
        // Lerp: LerpIt(double a, double b, double alpha) => Lerp(a, b, alpha)
        //   (KismetMathLibrary::Lerp, pure; pins A/B/Alpha/ReturnValue, returns double).
        //   Same pure-node form: exec chain goes entry -> result directly.
        var f2b = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "LerpIt",
            new[] { new { name = "a", type = "double" }, new { name = "b", type = "double" }, new { name = "alpha", type = "double" } },
            outputs: new[] { new { name = "ReturnValue", type = "double" } });
        var lerp2 = BlueprintTestHelper.AddNode(ctx, a2, "LerpIt", "Lerp", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2b.entryId, "a", lerp2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a2, f2b.entryId, "b", lerp2.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a2, f2b.entryId, "alpha", lerp2.nodeId, "Alpha");
        BlueprintTestHelper.Connect(ctx, a2, lerp2.nodeId, "ReturnValue", f2b.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a2, f2b.entryId, "then", f2b.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (doc form, verbatim - "float" + Math.Clamp / Math.Lerp, test is spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Math_Lerp_B");

        // -- Task 3: Dot product --
        // MCP step: Math_Dot_A + function DotIt(Vector a, Vector b) => Dot(a, b)
        //   (KismetMathLibrary::Dot_VectorVector, pure; engine name is Dot_VectorVector,
        //   not "DotProduct"). Output typed double to match the return (buildable).
        const string a3 = "/Game/Tests/UE5_0/Math_Dot_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "DotIt",
            new[] { new { name = "a", type = "Vector" }, new { name = "b", type = "Vector" } },
            outputs: new[] { new { name = "ReturnValue", type = "double" } });
        var dot3 = BlueprintTestHelper.AddNode(ctx, a3, "DotIt", "Dot_VectorVector", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "a", dot3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "b", dot3.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a3, dot3.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (doc form, verbatim - "float" + unverified Vector.Dot, test is spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Math_Dot_B");

        await Task.CompletedTask;
    }
}
