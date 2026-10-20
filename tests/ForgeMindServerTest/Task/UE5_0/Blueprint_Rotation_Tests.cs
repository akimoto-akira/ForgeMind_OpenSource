// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 56 - Blueprint Rotation (UE 5.0) - from Tasks/UE_5.0/blueprint-rotation.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit.
// MakeRotator / ComposeRotators / RInterpTo are BlueprintPure on
// UKismetMathLibrary -- pure nodes with no exec pins; exec goes directly
// entry.then -> result.execute. Task 3 runs in Event Tick (not a function):
// GetActorRotation (pure) + Goal variable getter (pure) feed RInterpTo (pure),
// whose result wires to SetActorRotation (BlueprintCallable, has exec) driven
// by Tick.then.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - MakeRotator(p, y, r) -> Rotator
//   Task 2 - ComposeRotators(a, b) -> Rotator
//   Task 3 - RInterpTo + SetActorRotation in Tick (Goal variable)
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-18 per user instruction,
// after this case was confirmed by visual inspection. One per asset -- A side after
// CompileAndSave, DSL side after ApplyDsl (6 total: Rot_Make_A/B, Rot_Comb_A/B,
// Rot_Interp_A/B). Same convention as Blueprint_Select_Tests. Baselines are seeded on
// the first run; from the second run onward they actually diff.
// =========================================================================

public sealed class Blueprint_Rotation : ITestCase
{
    public string Name => "Blueprint Rotation (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 56;

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
            const string a1 = "/Game/Tests/UE5_0/Rot_Make_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Make",
                new[] { new { name = "p", type = "float" },
                        new { name = "y", type = "float" },
                        new { name = "r", type = "float" } },
                outputs: new[] { new { name = "ReturnValue", type = "Rotator" } });
            var mk = BlueprintTestHelper.AddNode(ctx, a1, "Make", "MakeRotator", 300, 0,
                "KismetMathLibrary");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "p", mk.nodeId, "Pitch");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "y", mk.nodeId, "Yaw");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "r", mk.nodeId, "Roll");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, mk.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison (spec point 9: inserted per user instruction)
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Rot_Make_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Rot_Comb_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Combine",
                new[] { new { name = "a", type = "Rotator" },
                        new { name = "b", type = "Rotator" } },
                outputs: new[] { new { name = "ReturnValue", type = "Rotator" } });
            var comb = BlueprintTestHelper.AddNode(ctx, a2, "Combine", "ComposeRotators", 300, 0,
                "KismetMathLibrary");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", comb.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", comb.nodeId, "B");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, comb.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Rot_Comb_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Rot_Interp_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            BlueprintTestHelper.AddVariable(ctx, a3, "Goal", "Rotator");
            var ev = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveTick", 0, 0);
            // AActor getter/setter real names carry the K2_ prefix (K2_GetActorRotation /
            // K2_SetActorRotation; display names are "GetActorRotation"/"SetActorRotation") --
            // same rule as Blueprint_Interp_Tests' K2_GetActorLocation. No ownerClass: the
            // skeleton-class FindFunctionByName chain finds the K2_ name (Interp-verified form).
            var getRot = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "K2_GetActorRotation", 200, 0);
            var getGoal = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Goal", 200, 150,
                kind: "getter");
            var interp = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "RInterpTo", 400, 0,
                "KismetMathLibrary");
            var setRot = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "K2_SetActorRotation", 600, 0);
            BlueprintTestHelper.Connect(ctx, a3, getRot.nodeId, "ReturnValue", interp.nodeId, "Current");
            BlueprintTestHelper.Connect(ctx, a3, getGoal.nodeId, "Goal", interp.nodeId, "Target");
            BlueprintTestHelper.Connect(ctx, a3, ev.nodeId, "DeltaSeconds", interp.nodeId, "DeltaTime");
            BlueprintTestHelper.SetPinDefault(ctx, a3, interp.nodeId, "InterpSpeed", "4.0");
            BlueprintTestHelper.Connect(ctx, a3, interp.nodeId, "ReturnValue", setRot.nodeId, "NewRotation");
            BlueprintTestHelper.Connect(ctx, a3, ev.nodeId, "then", setRot.nodeId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Rot_Interp_B");
    }
}