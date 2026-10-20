// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 58 - Blueprint Destroy (UE 5.0) - from Tasks/UE_5.0/blueprint-destroy.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit.
// DestroyActor (AActor::K2_DestroyActor) is a self member call, BlueprintCallable
// (not pure) -- has exec pins. DestroyComponent (UActorComponent::K2_DestroyComponent,
// ActorComponent.h L964: DisplayName="Destroy Component", ScriptName="DestroyComponent",
// meta DefaultToSelf="Object" / HidePin="Object") is the real FName; per spec point 8
// the bridge resolves via FindFunctionByName only, so the A-side uses K2_DestroyComponent
// (same pattern as K2_GetActorRotation / K2_DestroyActor). Member call on the component:
// component getter -> "__target" (same form as Spline/Curve member calls). Both chain
// exec through the call node.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - DestroyActor(this) in Die()
//   Task 2 - Mesh.DestroyComponent() in RemoveMesh()
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-18 per user instruction,
// after this case was confirmed by visual inspection. One per asset -- A side after
// CompileAndSave, DSL side after ApplyDsl (4 total: Kill_Actor_A/B, Kill_Comp_A/B).
// Same convention as Blueprint_Select_Tests / Blueprint_Rotation_Tests / Blueprint_ShapeTrace_Tests.
// Baselines are seeded on the first run; from the second run onward they actually diff.
// =========================================================================

public sealed class Blueprint_Destroy : ITestCase
{
    public string Name => "Blueprint Destroy (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 58;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        await Task.CompletedTask;
    }

    private static void BuildTask1(TestContext ctx)
    {
        {
            const string a1 = "/Game/Tests/UE5_0/Kill_Actor_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Die");
            // AActor destroy real name is K2_DestroyActor (display name "DestroyActor") --
            // same K2_ real-name rule as Blueprint_Interp_Tests' K2_GetActorLocation.
            var kill = BlueprintTestHelper.AddNode(ctx, a1, "Die", "K2_DestroyActor", 300, 0, "Actor");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", kill.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, kill.nodeId, "then", f1.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison (spec point 9: inserted per user instruction)
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Kill_Actor_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Kill_Comp_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            BlueprintTestHelper.AddComponent(ctx, a2, "Mesh", "StaticMeshComponent");
            var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "RemoveMesh");
            var getMesh = BlueprintTestHelper.AddNode(ctx, a2, "RemoveMesh", "Mesh", 100, 0,
                kind: "getter");
            var kill = BlueprintTestHelper.AddNode(ctx, a2, "RemoveMesh", "K2_DestroyComponent", 300, 0,
                "ActorComponent");
            BlueprintTestHelper.Connect(ctx, a2, getMesh.nodeId, "Mesh", kill.nodeId, "__target");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", kill.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, kill.nodeId, "then", f2.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Kill_Comp_B");
    }
}