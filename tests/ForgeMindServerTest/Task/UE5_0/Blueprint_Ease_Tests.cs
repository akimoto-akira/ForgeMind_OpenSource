// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 26 - Blueprint Ease (UE 5.0) - from Tasks/UE_5.0/blueprint-ease.md
//
// Probe (collaboration spec point 6):
// - Ease node (UK2Node_EaseFunction) compiles down to UKismetMathLibrary::Ease
//   (KismetMathLibrary.h L791: Ease(A, B, Alpha, EasingFunc, BlendExp, Steps),
//   BlueprintPure static function, no self pin) -- MCP side is add_blueprint_node
//   with class=KismetMathLibrary + name=Ease, engine real-name pins A / B / Alpha / EasingFunc
//   and return value ReturnValue. float->double pin interchange
//   (EdGraphSchema_K2::ArePinTypesCompatible L4383-4388: PC_Real always compatible,
//   bytecode inserts implicit conversion), so float function args connect directly.
// - **Easing curve names in doc don't exist in UE 5.0.3**: EEasingFunc::Type
//   (KismetMathLibrary.h L26-70) only has Linear / Step / SinusoidalIn|Out|InOut /
//   EaseIn|Out|InOut / ExpoIn|Out|InOut / CircularIn|Out|InOut. Doc's
//   ExpOut / CubicInOut / ElasticOut belong to newer version curve sets, 5.0.3 has no same-named items --
//   rewritten per 5.0.3 real member names (ExpOut->ExpoOut, CubicInOut->EaseInOut,
//   ElasticOut->ExpoOut), otherwise set_pin_default_value would be judged as
//   illegal enum by IsPinDefaultValid and fail immediately, testing not "easing"
//   but a non-existent enum.
//   DSL enum args also use 5.0.3 real names (Enum.Value -> member name).
// - Enum pin filled with **enum item name** (not index): EasingFunc is a TEnumAsByte pin,
//   EdGraphSchema_K2::DefaultValueSimpleValidation's PC_Byte branch validates by
//   UEnum::GetIndexByNameString (EdGraphSchema_K2.cpp L4125-4134),
//   A-side set_pin_default_value(A, EasingFunc, "ExpoOut") bridge reads back set:true.
// - **Doc's Clamp is int32 overload** (KismetMathLibrary.h L386-388,
//   DisplayName "Clamp (Integer)"), float version real name FClamp (L747-749). Task 3's
//   DSL dispatches as FClamp, same as A-side name (see subtask 3 notes).
// - **Task 3's float addition real name is Add_DoubleDouble, not Add_FloatFloat**:
//   5.0.3's `Add_FloatFloat` (L575) is marked `UE_DEPRECATED(5.0, ...)` and **not
//   UFUNCTION** (only C++ static remains, not Blueprint-callable); callable one is L540's
//   `Add_DoubleDouble(double A, double B = 1.0)` (DisplayName "float + float").
//   Same family's Subtract / Less / GreaterEqual etc. *FloatFloat all similarly deprecated,
//   corresponding *DoubleDouble. A-side dispatches by real name, DSL TryLowerBinaryValue also picks Double family.
// - **Task 3's Tick param name must be DeltaSeconds** (doc wrote `d`): event override's
//   entry pin shaped by **parent class signature**, ReceiveTick's output pin real name is DeltaSeconds.
//   DSL using different name (d) does not become a real pin, function body references to it
//   all disconnected -- tested B-side `P + d` produced no operation node and no data connection,
//   with zero diagnostics throughout. Translator now has override signature consistency
//   check (same-name param inconsistency -> Error terminates batch), see BlueprintTranslator's
//   CheckOverrideSignatureConsistency.
//
// Note: mis-touch comparison (VerifyIrSnapshot) attached by user instruction (collaboration
//   spec point 9), one per subtask side: Ease_Out_A / Ease_Out_B / Ease_Types_A / Ease_Types_B /
//   Ease_Tick_A / Ease_Tick_B. First run records the baselines.
// =========================================================================

public sealed class Blueprint_Ease : ITestCase
{
    public string Name => "Blueprint Ease (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 26;

    private static readonly object[] EaseInputs =
        new[]
        {
            new { name = "a", type = "float" },
            new { name = "b", type = "float" },
            new { name = "t", type = "float" },
        };

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Ease (ExpoOut) --
        // MCP steps: Ease_Out_A (Extends Actor) + function E(a, b, t) : float
        //         -> Ease(A=a, B=b, Alpha=t, EasingFunc=ExpoOut) connects ReturnValue
        const string a1 = "/Game/Tests/UE5_0/Ease_Out_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "E", EaseInputs,
            outputs: new[] { new { name = "ReturnValue", type = "float" } });
        var ease1 = BlueprintTestHelper.AddNode(ctx, a1, "E", "Ease", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, ease1.nodeId, "EasingFunc", "ExpoOut");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "a", ease1.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "b", ease1.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "t", ease1.nodeId, "Alpha");
        BlueprintTestHelper.Connect(ctx, a1, ease1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        // Ease is a pure node (no exec pin), execution chain can only be entry straight to result (same as Lerp_F_A).
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc; only easing curve name rewritten per 5.0.3 real enum)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ease_Out_B");

        // -- Subtask 2: Ease (different easing types) --
        // Doc task 2 says CubicInOut -- 5.0.3 has no Cubic* items, use synonym real member
        // EaseInOut (smooth acceleration then smooth deceleration).
        const string a2 = "/Game/Tests/UE5_0/Ease_Types_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "C", EaseInputs,
            outputs: new[] { new { name = "ReturnValue", type = "float" } });
        var ease2 = BlueprintTestHelper.AddNode(ctx, a2, "C", "Ease", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, ease2.nodeId, "EasingFunc", "EaseInOut");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", ease2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", ease2.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "t", ease2.nodeId, "Alpha");
        BlueprintTestHelper.Connect(ctx, a2, ease2.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ease_Types_B");

        // -- Subtask 3: Ease driven in Tick --
        // MCP steps: Ease_Tick_A (Extends Actor) + P : float (0->1 accumulation)
        //   Event Tick -> P = FClamp(P + DeltaSeconds, 0, 1)
        //             -> Ease(A=0, B=100, Alpha=P, EasingFunc=ExpoOut) -> z
        //             -> Make Vector(X=0, Y=0, Z=z) -> SetActorLocation
        // Node real name verification: 5.0.3's float addition is Add_FloatFloat, clamping is FClamp
        // (KismetMathLibrary.h L575 / L749 -- there is no Clamp_FloatFloat function).
        // Doc says ElasticOut -- 5.0.3 has no Elastic* items, use closest-shaped ExpoOut.
        const string a3 = "/Game/Tests/UE5_0/Ease_Tick_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "P", "float", "0.0");

        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveTick", 0, 0);
        var getP3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "P", 200, -200, kind: "getter");
        var add3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Add_DoubleDouble", 450, -200,
            "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getP3.nodeId, "P", add3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "DeltaSeconds", add3.nodeId, "B");
        var clamp3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "FClamp", 700, -200,
            "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, clamp3.nodeId, "Min", "0.0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, clamp3.nodeId, "Max", "1.0");
        BlueprintTestHelper.Connect(ctx, a3, add3.nodeId, "ReturnValue", clamp3.nodeId, "Value");
        var setP3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "P", 950, -200, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a3, clamp3.nodeId, "ReturnValue", setP3.nodeId, "P");

        var ease3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Ease", 700, 100, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, ease3.nodeId, "A", "0.0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, ease3.nodeId, "B", "100.0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, ease3.nodeId, "EasingFunc", "ExpoOut");
        BlueprintTestHelper.Connect(ctx, a3, getP3.nodeId, "P", ease3.nodeId, "Alpha");

        // MakeStruct's 'struct' field not exposed by BlueprintTestHelper.AddNode (see
        // Blueprint_Struct_Tests same direct approach), here directly call add_blueprint_node for nodeId.
        var makeVec3 = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_node", new
        {
            path = a3,
            graphName = "EventGraph",
            name = "MakeStruct",
            x = 1000,
            y = 100,
            @struct = "Vector",
        });
        ctx.AssertTrue(makeVec3["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_node MakeStruct(Vector) @ Ease_Tick_A: "
            + $"{makeVec3["response"]?["reason"]?.GetValue<string>() ?? ""}");
        string makeVecId = makeVec3["response"]?["nodeId"]?.GetValue<string>() ?? "";
        // MakeStruct member input pins by property name (Vector -> X / Y / Z, see K2Node_MakeStruct
        // and Blueprint_Struct_Tests file header), output pin name = struct name "Vector".
        // X / Y are independent numeric pins (not FVector struct pin), each set to "0.0".
        BlueprintTestHelper.SetPinDefault(ctx, a3, makeVecId, "X", "0.0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, makeVecId, "Y", "0.0");
        BlueprintTestHelper.Connect(ctx, a3, ease3.nodeId, "ReturnValue", makeVecId, "Z");

        var setLoc3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "K2_SetActorLocation", 1350, 0);
        BlueprintTestHelper.Connect(ctx, a3, makeVecId, "Vector", setLoc3.nodeId, "NewLocation");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", setP3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, setP3.nodeId, "then", setLoc3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc; easing curve name / clamping function name / Tick param name
        // three places rewritten per 5.0.3 real names, see file header "Probe"). Tick param
        // **must** be DeltaSeconds: engine shapes event entry by parent class signature,
        // output pin real name is DeltaSeconds, writing an alias like d does not become a real pin,
        // and all references to that name in the function body disconnect
        // (doc wrote exactly d).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ease_Tick_B");

        await Task.CompletedTask;
    }
}
