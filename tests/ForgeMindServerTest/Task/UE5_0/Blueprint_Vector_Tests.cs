// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 59 - Blueprint Vector (UE 5.0) - from Tasks/UE_5.0/blueprint-vector.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit.
// All math functions (VSize / Normal / Dot_VectorVector / Cross_VectorVector /
// GreaterGreater_VectorRotator) are BlueprintPure on UKismetMathLibrary -- pure
// nodes with no exec pins. Both A and B side write engine-queryable names, using
// the two bases allowed by the user rule "query uses only two criteria: DisplayName and direct
// function name":
//   * A side: engine FNames (FindFunctionByName strict, spec 8).
//   * B side: the same FNames in namespace-qualified static form
//     (Vector.VSize / Vector.Normal / Vector.Dot / Vector.Cross /
//     Vector.GreaterGreater_VectorRotator). TryFindFunctionOwner resolves these
//     via Pass 2 exact FName, Pass 3 the Operation_TypeType prefix convention
//     ("Dot" matches "Dot_VectorVector"), and Pass 1 friendly alias -- which is
//     only DisplayName (ScriptName / ScriptMethod are engine-internal bindings
//     and are NOT part of the queryable pair). No hard-coded per-name table.
// Rotate-by-rotator FName is GreaterGreater_VectorRotator (KismetMathLibrary.h
// L1439: DisplayName="Rotate Vector"). Since DisplayName carries a space and
// C# identifiers cannot, B side writes the FName directly.
// PrintString (KismetSystemLibrary) is BlueprintCallable -- exec chains through
// each PrintString node.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Vector.VSize(v) + Vector.Normal(v) -> PrintString each
//   Task 2 - Vector.Dot(a,b) + Vector.Cross(a,b) -> PrintString each
//   Task 3 - Vector.GreaterGreater_VectorRotator(v,r) -> PrintString
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-19 per user instruction,
// after this case was confirmed by visual inspection. One per asset -- A side after
// CompileAndSave, DSL side after ApplyDsl (6 total: Vec_Norm_A/B, Vec_Prod_A/B,
// Vec_Rot_A/B). Same convention as Blueprint_Select_Tests / Blueprint_Rotation_Tests /
// Blueprint_ShapeTrace_Tests / Blueprint_Destroy_Tests. Baselines are seeded on the
// first run; from the second run onward they actually diff.
// =========================================================================

public sealed class Blueprint_Vector : ITestCase
{
    public string Name => "Blueprint Vector (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 59;

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
            const string a1 = "/Game/Tests/UE5_0/Vec_Norm_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Proc",
                new[] { new { name = "v", type = "vector" } });
            var len = BlueprintTestHelper.AddNode(ctx, a1, "Proc", "VSize", 200, 0,
                "KismetMathLibrary");
            var norm = BlueprintTestHelper.AddNode(ctx, a1, "Proc", "Normal", 200, 150,
                "KismetMathLibrary");
            var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Proc", "PrintString", 400, 0,
                "KismetSystemLibrary");
            var p2 = BlueprintTestHelper.AddNode(ctx, a1, "Proc", "PrintString", 600, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "v", len.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "v", norm.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a1, len.nodeId, "ReturnValue", p1.nodeId, "InString");
            BlueprintTestHelper.Connect(ctx, a1, norm.nodeId, "ReturnValue", p2.nodeId, "InString");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", p1.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", p2.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, p2.nodeId, "then", f1.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison (spec point 9: inserted per user instruction)
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Vec_Norm_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Vec_Prod_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Proc",
                new[] { new { name = "a", type = "vector" },
                        new { name = "b", type = "vector" } });
            var dot = BlueprintTestHelper.AddNode(ctx, a2, "Proc", "Dot_VectorVector", 200, 0,
                "KismetMathLibrary");
            var cross = BlueprintTestHelper.AddNode(ctx, a2, "Proc", "Cross_VectorVector", 200, 150,
                "KismetMathLibrary");
            var p1 = BlueprintTestHelper.AddNode(ctx, a2, "Proc", "PrintString", 400, 0,
                "KismetSystemLibrary");
            var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Proc", "PrintString", 600, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", dot.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", dot.nodeId, "B");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", cross.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", cross.nodeId, "B");
            BlueprintTestHelper.Connect(ctx, a2, dot.nodeId, "ReturnValue", p1.nodeId, "InString");
            BlueprintTestHelper.Connect(ctx, a2, cross.nodeId, "ReturnValue", p2.nodeId, "InString");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", p1.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, p1.nodeId, "then", p2.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Vec_Prod_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Vec_Rot_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Proc",
                new[] { new { name = "v", type = "vector" },
                        new { name = "r", type = "Rotator" } });
            var rot = BlueprintTestHelper.AddNode(ctx, a3, "Proc", "GreaterGreater_VectorRotator", 200, 0,
                "KismetMathLibrary");
            var p1 = BlueprintTestHelper.AddNode(ctx, a3, "Proc", "PrintString", 400, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "v", rot.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "r", rot.nodeId, "B");
            BlueprintTestHelper.Connect(ctx, a3, rot.nodeId, "ReturnValue", p1.nodeId, "InString");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", p1.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a3, p1.nodeId, "then", f3.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Vec_Rot_B");
    }
}