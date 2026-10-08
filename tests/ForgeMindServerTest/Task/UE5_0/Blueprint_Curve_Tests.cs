// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 38 - Blueprint Curve (UE 5.0) - from Tasks/UE_5.0/Blueprint-Curve.md
//
// Probe (collaboration spec point 6):
// - UCurveFloat::GetFloatValue (Curves/CurveFloat.h:43-44) is BlueprintCallable, Category
//   "Math|Curves", **const**, parameter named `InTime`, no DisplayName meta -> node text is
//   "Get Float Value". UCurveVector::GetVectorValue (Curves/CurveVector.h:39-40) likewise:
//   `FVector GetVectorValue(float InTime) const`, node text "Get Vector Value". Note the
//   headers live under Engine\Classes\Curves\ in 5.0.3, not Engine\Classes\Engine\.
//   Both classes are BlueprintType (CurveFloat.h:29 / CurveVector.h:29) so they are legal
//   blueprint variable types.
// - **Prerequisite curve assets missing**: the doc needs CurveFloat `CF_Speed` and
//   CurveVector `CV_Pos`. The bridge has no create-curve action (same gap as
//   Blueprint_DynamicMaterial_Tests' BaseMat placeholder), so the variables are created
//   with empty defaults and the sample nodes' curve input stays unconnected -- the case
//   verifies node construction and signature, not a sampled value. Pending requirement.
// - DSL side: `CF_Speed.GetFloatValue(t)` is an instance-method call on a variable, which
//   the blueprint translator treats as a member access (K2Node_VariableGet + call); whether
//   UFUNCTION-on-variable dispatch lowers correctly is unverified. `PrintString(<float>)`
//   in subtask 3 also relies on the implicit float->string conversion the translator has to
//   synthesize. Copied as written -- test is spec.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transwriting task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
// =========================================================================

public sealed class Blueprint_Curve : ITestCase
{
    public string Name => "Blueprint Curve (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 38;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: sample a CurveFloat --
        // MCP steps: Curve_Float_A (Extends Actor) + CF_Speed : CurveFloat
        //   Sample(float t) -> Get Float Value(InTime = t) -> ReturnValue
        const string a1 = "/Game/Tests/UE5_0/Curve_Float_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "CF_Speed", "CurveFloat");
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Sample",
            new[] { new { name = "t", type = "float" } },
            outputs: new[] { new { name = "ReturnValue", type = "float" } });
        var get1 = BlueprintTestHelper.AddNode(ctx, a1, "Sample", "GetFloatValue", 300, 0, "CurveFloat");
        // Pending bridge capability: CF_Speed has no asset to point at (no create-curve
        // action), so the curve input pin is left unconnected -- see file header.
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "t", get1.nodeId, "InTime");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", get1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, get1.nodeId, "Return Value", f1.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: sample a CurveVector --
        // MCP steps: Curve_Vec_A (Extends Actor) + CV_Pos : CurveVector
        //   Sample(float t) -> Get Vector Value -> ReturnValue
        const string a2 = "/Game/Tests/UE5_0/Curve_Vec_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "CV_Pos", "CurveVector");
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Sample",
            new[] { new { name = "t", type = "float" } },
            outputs: new[] { new { name = "ReturnValue", type = "vector" } });
        var get2 = BlueprintTestHelper.AddNode(ctx, a2, "Sample", "GetVectorValue", 300, 0, "CurveVector");
        // Pending bridge capability: no CurveVector asset to assign (see file header).
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "t", get2.nodeId, "InTime");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", get2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, get2.nodeId, "Return Value", f2.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: time-driven sampling in Tick --
        // MCP steps: Curve_Tick_A (Extends Actor) + CF_Speed : CurveFloat
        //   Event Tick -> Get Float Value(InTime = DeltaSeconds) -> PrintString
        const string a3 = "/Game/Tests/UE5_0/Curve_Tick_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "CF_Speed", "CurveFloat");
        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveTick", 0, 0);
        var get3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "GetFloatValue", 300, 0,
            "CurveFloat");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 600, 0,
            "KismetSystemLibrary");
        // Tick's output pin is named DeltaSeconds (parent-class signature), not 'd' -- same
        // reason Blueprint_Ease_Tests uses DeltaSeconds.
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "DeltaSeconds", get3.nodeId, "InTime");
        BlueprintTestHelper.Connect(ctx, a3, get3.nodeId, "Return Value", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}