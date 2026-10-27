// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 66 - Blueprint Predict Projectile Path (UE 5.0) - from Tasks/UE_5.0/Blueprint_PredictProjectile.md
//
// Probe (collaboration spec point 6):
// - **The doc's function names are not blueprint-callable in 5.0.3**: UGameplayStatics has two
//   UFUNCTIONs plus one plain C++ overload (GameplayStatics.h:1235 / :1259 / :1270):
//     Blueprint_PredictProjectilePath_ByObjectType(WorldContextObject, out OutHit,
//       out OutPathPositions, out OutLastTraceDestination, StartPos, LaunchVelocity,
//       bTracePath, ProjectileRadius, ObjectTypes, bTraceComplex, ActorsToIgnore,
//       DrawDebugType, DrawDebugTime, SimFrequency, MaxSimTime, OverrideGravityZ) : bool
//     Blueprint_PredictProjectilePath_ByTraceChannel(...) — TraceChannel instead of ObjectTypes
//     PredictProjectilePath(...) — **not** a UFUNCTION (no UFUNCTION marker at :1270), so
//       FindFunctionByName cannot see it and no "Predict Projectile Path" node exists.
//   Subtask 1 uses the ByObjectType node with bTracePath left default (no collision trace),
//   subtask 2 the ByTraceChannel node — the two blueprint-exposed flavours.
// - **Gravity is a float, not a vector**: the doc's `Gravity=(0,0,-980)` has no pin — the real
//   parameter is OverrideGravityZ (float, AdvancedDisplay; 0 = use WorldGravityZ). Subtasks
//   therefore set OverrideGravityZ = -980.
// - The three out parameters come first in the C++ signature, so they are the node's top output
//   pins (OutHit / OutPathPositions / OutLastTraceDestination).
// - **ObjectTypes is an ARRAY pin and must be wired to a MakeArray node**: the engine rejects
//   the literal default ("Object Types" pin's current value (ObjectTypeQuery1) is invalid: array input must
//   connect to a valid input) — the ByObjectType flavour's ObjectTypes (TArray<TEnumAsByte<
//   EObjectTypeQuery>>) needs a MakeArray (UK2Node_MakeContainer, output pin "Array", input
//   pins "[0]"/"[1]"/...) with its element defaulting to ObjectTypeQuery1 (the first
//   EObjectTypeQuery entry). User verdict 2026-10-22 (editor compile errors on both
//   ByObjectType subtasks). The DSL side auto-emits the same MakeArray in the translator.
// - Actor accessors: K2_GetActorLocation (Actor.h:1185) and GetActorForwardVector (Actor.h:1208)
//   — note the forward vector has **no** K2_ prefix in 5.0.3 (that rename is 5.1+), while the
//   location accessor does. Velocity scaling is UKismetMathLibrary::Multiply_VectorFloat
//   (KismetMathLibrary.h:1391, "vector * float").
// - Debug draw: UKismetSystemLibrary::DrawDebugPoint(WorldContextObject, Position, Size,
//   PointColor, Duration) @KismetSystemLibrary.h:1470. Array iteration is the StandardMacros
//   ForEachLoop (kind "macro", exec-in pin "Exec", "Array" / "Array Element" / "LoopBody" /
//   "Completed"), same form as Blueprint_For.
// - DSL side: `PredictProjectilePath(...)` / `PredictProjectilePathByChannel(...)` with `out`
//   parameters are DSL vocabulary, not engine names — kept verbatim as the requirement spec
//   (per spec point 6 the missing lowering is the gap to implement).
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison). The B-side baselines cover the translator's
// predict-family lowering: auto MakeArray (ObjectTypes), out-variable set nodes chained
// AFTER the call, and Array_Length(path) wired into ForLoop.LastIndex.
// =========================================================================

public sealed class Blueprint_PredictProjectile : ITestCase
{
    public string Name => "Blueprint Predict Projectile Path (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 66;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: predict the path (no collision trace) --
        // MCP steps: Predict_Path_A (Extends Actor) + function Aim:
        //   StartPos = GetActorLocation, LaunchVelocity = GetActorForwardVector * 1000,
        //   OverrideGravityZ = -980 -> consume OutLastTraceDestination
        const string a1 = "/Game/Tests/UE5_0/Predict_Path_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Aim");
        var loc1 = BlueprintTestHelper.AddNode(ctx, a1, "Aim", "K2_GetActorLocation", 0, -150);
        var fwd1 = BlueprintTestHelper.AddNode(ctx, a1, "Aim", "GetActorForwardVector", 0, 0);
        var mul1 = BlueprintTestHelper.AddNode(ctx, a1, "Aim", "Multiply_VectorFloat", 150, 0,
            "KismetMathLibrary");
        var predict1 = BlueprintTestHelper.AddNode(ctx, a1, "Aim",
            "Blueprint_PredictProjectilePath_ByObjectType", 400, 0, "GameplayStatics");
        // ObjectTypes is an array pin: wire a MakeArray (single element, default
        // ObjectTypeQuery1) or the engine rejects the pin's literal default (header note).
        var types1 = BlueprintTestHelper.AddNode(ctx, a1, "Aim", "MakeArray", 300, 150,
            inputs: 1);
        var conv1 = BlueprintTestHelper.AddNode(ctx, a1, "Aim", "Conv_VectorToString", 650, -150,
            "KismetStringLibrary");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "Aim", "PrintString", 850, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, fwd1.nodeId, "ReturnValue", mul1.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a1, mul1.nodeId, "B", "1000.0");
        BlueprintTestHelper.Connect(ctx, a1, loc1.nodeId, "ReturnValue", predict1.nodeId, "StartPos");
        BlueprintTestHelper.Connect(ctx, a1, mul1.nodeId, "ReturnValue", predict1.nodeId,
            "LaunchVelocity");
        // Gravity override is a single float (see file header).
        BlueprintTestHelper.SetPinDefault(ctx, a1, predict1.nodeId, "OverrideGravityZ", "-980.0");
        // Connect FIRST (propagates the EObjectTypeQuery element type to "[0]"), then set the
        // element default — on a wildcard pin the literal default is rejected.
        BlueprintTestHelper.Connect(ctx, a1, types1.nodeId, "Array", predict1.nodeId, "ObjectTypes");
        BlueprintTestHelper.SetPinDefault(ctx, a1, types1.nodeId, "[0]", "ObjectTypeQuery1");
        // OutLastTraceDestination (FVector) -> To String (Vector) -> PrintString.
        BlueprintTestHelper.Connect(ctx, a1, predict1.nodeId, "OutLastTraceDestination",
            conv1.nodeId, "InVec");
        BlueprintTestHelper.Connect(ctx, a1, conv1.nodeId, "ReturnValue", print1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", predict1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, predict1.nodeId, "then", print1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, print1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Predict_Path_B");

        // -- Subtask 2: predict the path by trace channel --
        // MCP steps: Predict_Chan_A (Extends Actor) + function Aim: same inputs plus
        //   TraceChannel = Visibility
        const string a2 = "/Game/Tests/UE5_0/Predict_Chan_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Aim");
        var loc2 = BlueprintTestHelper.AddNode(ctx, a2, "Aim", "K2_GetActorLocation", 0, -150);
        var fwd2 = BlueprintTestHelper.AddNode(ctx, a2, "Aim", "GetActorForwardVector", 0, 0);
        var mul2 = BlueprintTestHelper.AddNode(ctx, a2, "Aim", "Multiply_VectorFloat", 150, 0,
            "KismetMathLibrary");
        var predict2 = BlueprintTestHelper.AddNode(ctx, a2, "Aim",
            "Blueprint_PredictProjectilePath_ByTraceChannel", 400, 0, "GameplayStatics");
        var conv2 = BlueprintTestHelper.AddNode(ctx, a2, "Aim", "Conv_VectorToString", 650, -150,
            "KismetStringLibrary");
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "Aim", "PrintString", 850, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, fwd2.nodeId, "ReturnValue", mul2.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a2, mul2.nodeId, "B", "1000.0");
        BlueprintTestHelper.Connect(ctx, a2, loc2.nodeId, "ReturnValue", predict2.nodeId, "StartPos");
        BlueprintTestHelper.Connect(ctx, a2, mul2.nodeId, "ReturnValue", predict2.nodeId,
            "LaunchVelocity");
        BlueprintTestHelper.SetPinDefault(ctx, a2, predict2.nodeId, "OverrideGravityZ", "-980.0");
        // TraceChannel is TEnumAsByte<ECollisionChannel>; the node's meta default is
        // ECC_WorldDynamic, the doc asks for Visibility.
        BlueprintTestHelper.SetPinDefault(ctx, a2, predict2.nodeId, "TraceChannel", "ECC_Visibility");
        BlueprintTestHelper.Connect(ctx, a2, predict2.nodeId, "OutLastTraceDestination",
            conv2.nodeId, "InVec");
        BlueprintTestHelper.Connect(ctx, a2, conv2.nodeId, "ReturnValue", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", predict2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, predict2.nodeId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Predict_Chan_B");

        // -- Subtask 3: draw the predicted path --
        // MCP steps: Predict_Draw_A (Extends Actor) + function Show:
        //   predict -> ForEachLoop(OutPathPositions) -> DrawDebugPoint(element, 5, red, 0.1)
        const string a3 = "/Game/Tests/UE5_0/Predict_Draw_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Show");
        var loc3 = BlueprintTestHelper.AddNode(ctx, a3, "Show", "K2_GetActorLocation", 0, -150);
        var fwd3 = BlueprintTestHelper.AddNode(ctx, a3, "Show", "GetActorForwardVector", 0, 0);
        var mul3 = BlueprintTestHelper.AddNode(ctx, a3, "Show", "Multiply_VectorFloat", 150, 0,
            "KismetMathLibrary");
        var predict3 = BlueprintTestHelper.AddNode(ctx, a3, "Show",
            "Blueprint_PredictProjectilePath_ByObjectType", 400, 0, "GameplayStatics");
        // ObjectTypes array pin -> MakeArray (same form as subtask 1, header note).
        var types3 = BlueprintTestHelper.AddNode(ctx, a3, "Show", "MakeArray", 300, 150,
            inputs: 1);
        var each3 = BlueprintTestHelper.AddNode(ctx, a3, "Show", "ForEachLoop", 650, 0,
            kind: "macro");
        var draw3 = BlueprintTestHelper.AddNode(ctx, a3, "Show", "DrawDebugPoint", 900, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, fwd3.nodeId, "ReturnValue", mul3.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a3, mul3.nodeId, "B", "1000.0");
        BlueprintTestHelper.Connect(ctx, a3, loc3.nodeId, "ReturnValue", predict3.nodeId, "StartPos");
        BlueprintTestHelper.Connect(ctx, a3, mul3.nodeId, "ReturnValue", predict3.nodeId,
            "LaunchVelocity");
        BlueprintTestHelper.SetPinDefault(ctx, a3, predict3.nodeId, "OverrideGravityZ", "-980.0");
        // Connect first, then set the element default (type propagation, subtask 1 note).
        BlueprintTestHelper.Connect(ctx, a3, types3.nodeId, "Array", predict3.nodeId, "ObjectTypes");
        BlueprintTestHelper.SetPinDefault(ctx, a3, types3.nodeId, "[0]", "ObjectTypeQuery1");
        // ForEachLoop exec-in pin is "Exec" (StandardMacros convention, see Blueprint_For).
        BlueprintTestHelper.Connect(ctx, a3, predict3.nodeId, "OutPathPositions", each3.nodeId,
            "Array");
        BlueprintTestHelper.Connect(ctx, a3, predict3.nodeId, "then", each3.nodeId, "Exec");
        BlueprintTestHelper.Connect(ctx, a3, each3.nodeId, "Array Element", draw3.nodeId, "Position");
        BlueprintTestHelper.SetPinDefault(ctx, a3, draw3.nodeId, "Size", "5.0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, draw3.nodeId, "Duration", "0.1");
        BlueprintTestHelper.Connect(ctx, a3, each3.nodeId, "LoopBody", draw3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", predict3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, each3.nodeId, "Completed", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Predict_Draw_B");

        await Task.CompletedTask;
    }
}
