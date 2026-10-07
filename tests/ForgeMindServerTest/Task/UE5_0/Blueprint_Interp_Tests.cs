// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 12 · Blueprint Interpolation (UE 5.0)  — from Tasks/UE_5.0/Blueprint-Interp.md
// Probe (Spec Point 6): already supports create_function_graph outputs (function return value),
// Lerp/VInterpTo/FInterpTo real names, ReceiveTick/DeltaSeconds, double→string auto
// conversion; GetActorLocation/SetActorLocation engine real names are K2_GetActorLocation /
// K2_SetActorLocation. Gaps (written assuming already supported): ① DSL pure function init local
// declaration `Vector v = VInterpTo(...)`; ② DSL bare call real-name mapping for GetActorLocation.
// ③ Problem 3 docs translate "Current=current value" directly as GetCurrent() (UE has no such function) —
// both A/B sides use the minimal assumption of member variable Cur (initial 0); B-side nested calls
// directly wire to PrintString, consistent with A-side graph shape (docs expect "cross-method content equivalence").
// =========================================================================

public sealed class Blueprint_Interp : ITestCase
{
    public string Name => "Blueprint Interpolation (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 12;

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: Lerp (float) ──
        const string a1 = "/Game/Tests/UE5_0/Lerp_F_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Mix",
            new[] { new { name = "a", type = "float" }, new { name = "b", type = "float" } },
            outputs: new[] { new { name = "ReturnValue", type = "float" } });
        var lerp1 = BlueprintTestHelper.AddNode(ctx, a1, "Mix", "Lerp", 300, -100, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, lerp1.nodeId, "Alpha", "0.5");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "a", lerp1.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "b", lerp1.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a1, lerp1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Lerp_F_B");

        // ── Sub-task 2: VInterpTo (in Tick) ──
        const string a2 = "/Game/Tests/UE5_0/Lerp_V_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "Target", "vector");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        var ev2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveTick", 0, 0);
        var getLoc2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "K2_GetActorLocation", 150, -100);
        var getT2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "Target", 150, 100, kind: "getter");
        var vi2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "VInterpTo", 400, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, vi2.nodeId, "InterpSpeed", "5.0");
        BlueprintTestHelper.Connect(ctx, a2, getLoc2.nodeId, "ReturnValue", vi2.nodeId, "Current");
        BlueprintTestHelper.Connect(ctx, a2, getT2.nodeId, "Target", vi2.nodeId, "Target");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "DeltaSeconds", vi2.nodeId, "DeltaTime");
        var setLoc2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "K2_SetActorLocation", 700, 0);
        BlueprintTestHelper.Connect(ctx, a2, vi2.nodeId, "ReturnValue", setLoc2.nodeId, "NewLocation");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "then", setLoc2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Lerp_V_B");

        // ── Sub-task 3: FInterpTo (per-frame smoothing) ──
        // Docs "Current=current value" has no source — using minimal assumption of member variable Cur (initial 0).
        const string a3 = "/Game/Tests/UE5_0/Lerp_FI_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Goal", "float");
        BlueprintTestHelper.AddVariable(ctx, a3, "Cur", "float", defaultValue: "0");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveTick", 0, 0);
        var getCur3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Cur", 150, 100, kind: "getter");
        var getGoal3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Goal", 150, -100, kind: "getter");
        var fi3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "FInterpTo", 400, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, fi3.nodeId, "InterpSpeed", "3.0");
        BlueprintTestHelper.Connect(ctx, a3, getCur3.nodeId, "Cur", fi3.nodeId, "Current");
        BlueprintTestHelper.Connect(ctx, a3, getGoal3.nodeId, "Goal", fi3.nodeId, "Target");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "DeltaSeconds", fi3.nodeId, "DeltaTime");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 700, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, fi3.nodeId, "ReturnValue", p3.nodeId, "InString");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Lerp_FI_B");

        await Task.CompletedTask;
    }
}
