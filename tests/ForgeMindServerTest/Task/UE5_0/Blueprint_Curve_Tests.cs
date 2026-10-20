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
//   Blueprint_DynamicMaterial_Tests' BaseMat placeholder), so the variables carry no
//   default asset value -- the case verifies node construction and signature, not a
//   sampled value. Pending requirement. Visual bug fix 2026/10/11 (user report: A-side
//   curve variables appeared nowhere in the graphs): the original authoring had
//   conflated "cannot set a default (needs an asset)" with "cannot wire the reference";
//   a variable getter -> "__target" wire needs no asset (same form as the B-side
//   translator and DynamicMaterial member calls), so each sample node's target pin is
//   now getter-connected.
// - DSL side: `CF_Speed.GetFloatValue(t)` is an instance-method call on a variable,
//   which the blueprint translator treats as a member access (K2Node_VariableGet + call,
//   receiver wired to the call node's "__target"). Verified 2026/10/11 single-case run:
//   all three DSL blocks applied with no warnings and compiled (float->string implicit
//   conversion in subtask 3 included). Copied as written -- test is spec.
//
// Note: "diff comparison" (VerifyIrSnapshot, mis-touch detection) inserted per user
// instruction 2026/10/11 after user visual confirmation of cases and results
// (collaboration spec point 9) -- covers all six assets (_A MCP path / _B DSL path).
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
        var getSpeed1 = BlueprintTestHelper.AddNode(ctx, a1, "Sample", "CF_Speed", 100, 0, kind: "getter");
        var get1 = BlueprintTestHelper.AddNode(ctx, a1, "Sample", "GetFloatValue", 300, 0, "CurveFloat");
        // Member call on the CF_Speed variable: getter -> "__target" (bridge resolves to the
        // call node's PN_Self, same form as the B-side translator). Wiring needs no asset;
        // only the variable's default asset value stays empty (create-curve pending).
        BlueprintTestHelper.Connect(ctx, a1, getSpeed1.nodeId, "CF_Speed", get1.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "t", get1.nodeId, "InTime");
        // GetFloatValue is a const BlueprintCallable -> Pure node (no exec pins); exec flow
        // goes directly from function entry to function result, bypassing the pure node.
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, get1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Curve_Float_B");

        // -- Subtask 2: sample a CurveVector --
        // MCP steps: Curve_Vec_A (Extends Actor) + CV_Pos : CurveVector
        //   Sample(float t) -> Get Vector Value -> ReturnValue
        const string a2 = "/Game/Tests/UE5_0/Curve_Vec_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "CV_Pos", "CurveVector");
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Sample",
            new[] { new { name = "t", type = "float" } },
            outputs: new[] { new { name = "ReturnValue", type = "vector" } });
        var getPos2 = BlueprintTestHelper.AddNode(ctx, a2, "Sample", "CV_Pos", 100, 0, kind: "getter");
        var get2 = BlueprintTestHelper.AddNode(ctx, a2, "Sample", "GetVectorValue", 300, 0, "CurveVector");
        // Getter -> "__target" (same form as subtask 1; no CurveVector asset to assign --
        // only the default value stays empty, see file header).
        BlueprintTestHelper.Connect(ctx, a2, getPos2.nodeId, "CV_Pos", get2.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "t", get2.nodeId, "InTime");
        // GetVectorValue is also const -> Pure node; exec flow direct to result.
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, get2.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Curve_Vec_B");

        // -- Subtask 3: time-driven sampling in Tick --
        // MCP steps: Curve_Tick_A (Extends Actor) + CF_Speed : CurveFloat
        //   Event Tick -> Get Float Value(InTime = DeltaSeconds) -> PrintString
        const string a3 = "/Game/Tests/UE5_0/Curve_Tick_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "CF_Speed", "CurveFloat");
        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveTick", 0, 0);
        var getSpeed3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "CF_Speed", 150, 100,
            kind: "getter");
        var get3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "GetFloatValue", 300, 0,
            "CurveFloat");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 600, 0,
            "KismetSystemLibrary");
        // Tick's output pin is named DeltaSeconds (parent-class signature), not 'd' -- same
        // reason Blueprint_Ease_Tests uses DeltaSeconds.
        BlueprintTestHelper.Connect(ctx, a3, getSpeed3.nodeId, "CF_Speed", get3.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "DeltaSeconds", get3.nodeId, "InTime");
        BlueprintTestHelper.Connect(ctx, a3, get3.nodeId, "ReturnValue", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Curve_Tick_B");

        await Task.CompletedTask;
    }
}