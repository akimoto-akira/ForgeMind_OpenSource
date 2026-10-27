// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 71 - Blueprint Transform (UE 5.0) - from Tasks/UE_5.0/Blueprint_Transform.md
//
// Probe (collaboration spec point 6):
// - All four transform functions are real UKismetMathLibrary UFUNCTIONs (engine index 5.0,
//   verified with ForgeMind.SpkTool) and are BlueprintPure, so they carry no exec pins:
//     MakeTransform(FVector Location, FRotator Rotation, FVector Scale) : FTransform   @:3730
//     BreakTransform(const FTransform& InTransform, out Location, out Rotation, out Scale)
//       : void  @:3734
//     ComposeTransforms(const FTransform& A, const FTransform& B) : FTransform  @:3761
//     InvertTransform(const FTransform& T) : FTransform  @:3831 (doc's InverseTransform)
// - "transform" is one of the bridge's base type keywords
//   (ForgeMindBlueprintUtils.cpp BaseTypeTextToPinType :1688), so both parameters and function
//   return values can be declared as Transform.
// - Printing: FVector → FString on a PrintString pin is an engine auto-cast —
//   UKismetStringLibrary::Conv_VectorToString(FVector InVec) : FString (KismetStringLibrary.h:40,
//   DisplayName "To String (Vector)", meta carries **BlueprintAutocast**, verified with
//   ForgeMind.SpkTool --meta). So a bare FVector data line into InString materialises exactly the
//   conversion node the A side builds explicitly; the earlier note here claimed FVector had no
//   implicit string conversion, which the meta contradicts.
// - DSL style (user ruling 2026-10-23, same as the enhanced-input case): a struct value is read
//   by **member access** (`t.Location`), never by calling the break function as a statement with
//   out-parameters — `BreakTransform(t, out loc, out rot, out scl);` is not DSL syntax. The
//   member access lowers through the engine's native BreakTransform node
//   (KismetMathLibrary.h:3734: InTransform in, Location / Rotation / **Scale** out — the third
//   pin is named Scale, not Scale3D), which is also the node the A side builds by hand.
//   The doc's `t.GetLocation()` remains unusable (resolve: NoDeclaration, no UFUNCTION).
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison). The B-side baselines also pin the member-access
// lowering of subtask 2 (`t.Location` → native BreakTransform), so a regression in
// NativeBreakFunctions would show up as a snapshot diff instead of a silently rebuilt graph.
// =========================================================================

public sealed class Blueprint_Transform : ITestCase
{
    public string Name => "Blueprint Transform (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 71;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: MakeTransform --
        // MCP steps: Xform_Make_A (Extends Actor) + function Make(Vector loc, Rotator rot,
        //   Vector scl) -> MakeTransform(loc, rot, scl) -> Return
        const string a1 = "/Game/Tests/UE5_0/Xform_Make_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Make",
            new[]
            {
                new { name = "loc", type = "vector" },
                new { name = "rot", type = "rotator" },
                new { name = "scl", type = "vector" },
            },
            outputs: new[] { new { name = "ReturnValue", type = "transform" } });
        var make1 = BlueprintTestHelper.AddNode(ctx, a1, "Make", "MakeTransform", 300, 0,
            "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "loc", make1.nodeId, "Location");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "rot", make1.nodeId, "Rotation");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "scl", make1.nodeId, "Scale");
        BlueprintTestHelper.Connect(ctx, a1, make1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Xform_Make_B");

        // -- Subtask 2: BreakTransform --
        // MCP steps: Xform_Break_A (Extends Actor) + function Split(Transform t)
        //   -> BreakTransform(t) -> Location -> To String (Vector) -> PrintString
        const string a2 = "/Game/Tests/UE5_0/Xform_Break_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Split",
            new[] { new { name = "t", type = "transform" } });
        var break2 = BlueprintTestHelper.AddNode(ctx, a2, "Split", "BreakTransform", 300, 0,
            "KismetMathLibrary");
        var conv2 = BlueprintTestHelper.AddNode(ctx, a2, "Split", "Conv_VectorToString", 500, -100,
            "KismetStringLibrary");
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "Split", "PrintString", 700, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "t", break2.nodeId, "InTransform");
        BlueprintTestHelper.Connect(ctx, a2, break2.nodeId, "Location", conv2.nodeId, "InVec");
        BlueprintTestHelper.Connect(ctx, a2, conv2.nodeId, "ReturnValue", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Xform_Break_B");

        // -- Subtask 3: ComposeTransforms --
        // MCP steps: Xform_Comp_A (Extends Actor) + function Combine(Transform a, Transform b)
        //   -> ComposeTransforms(a, b) -> Return
        const string a3 = "/Game/Tests/UE5_0/Xform_Comp_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Combine",
            new[] { new { name = "a", type = "transform" }, new { name = "b", type = "transform" } },
            outputs: new[] { new { name = "ReturnValue", type = "transform" } });
        var comp3 = BlueprintTestHelper.AddNode(ctx, a3, "Combine", "ComposeTransforms", 300, 0,
            "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "a", comp3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "b", comp3.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a3, comp3.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Xform_Comp_B");

        await Task.CompletedTask;
    }
}
