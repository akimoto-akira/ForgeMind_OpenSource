// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 61 - Blueprint Physics (UE 5.0) - from Tasks/UE_5.0/blueprint-physics.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit.
// AddImpulse / SetSimulatePhysics / AddForce are UPrimitiveComponent member
// calls, BlueprintCallable (not pure) -- component getter -> "__target" (same
// form as Spline/Curve member calls). All have exec pins; exec chains through
// the call node. Vector literals (new Vector(0,0,100)) are set as pin defaults
// on the Impulse/Force pins in the engine's bare three-number comma form
// (e.g. "0,0,100") — parenthesised struct-literal like "(X=0,Y=0,Z=100)" is
// rejected by the bridge-side Vector/Rotator strict check.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Mesh.AddImpulse(0, 0, 100) in Push()
//   Task 2 - Mesh.SetSimulatePhysics(true) in Enable()
//   Task 3 - Mesh.AddForce(0, 0, 50) in Pull()
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-20 per user instruction.
// One per asset -- A side after CompileAndSave, DSL side after ApplyDsl (6 total:
// Phys_Impulse_A/B, Phys_Sim_A/B, Phys_Force_A/B). Same convention as
// Blueprint_ShapeTrace_Tests / Blueprint_Select_Tests.
// Baselines are seeded on the first run; from the second run onward they actually diff.
// =========================================================================

public sealed class Blueprint_Physics : ITestCase
{
    public string Name => "Blueprint Physics (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 61;

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
            const string a1 = "/Game/Tests/UE5_0/Phys_Impulse_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            BlueprintTestHelper.AddComponent(ctx, a1, "Mesh", "StaticMeshComponent");
            var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Push");
            var getMesh = BlueprintTestHelper.AddNode(ctx, a1, "Push", "Mesh", 100, 0,
                kind: "getter");
            var imp = BlueprintTestHelper.AddNode(ctx, a1, "Push", "AddImpulse", 300, 0,
                "PrimitiveComponent");
            BlueprintTestHelper.Connect(ctx, a1, getMesh.nodeId, "Mesh", imp.nodeId, "__target");
            BlueprintTestHelper.SetPinDefault(ctx, a1, imp.nodeId, "Impulse",
                "0.000000,0.000000,100.000000");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", imp.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, imp.nodeId, "then", f1.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Phys_Impulse_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Phys_Sim_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            BlueprintTestHelper.AddComponent(ctx, a2, "Mesh", "StaticMeshComponent");
            var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Enable");
            var getMesh = BlueprintTestHelper.AddNode(ctx, a2, "Enable", "Mesh", 100, 0,
                kind: "getter");
            var sim = BlueprintTestHelper.AddNode(ctx, a2, "Enable", "SetSimulatePhysics", 300, 0,
                "PrimitiveComponent");
            BlueprintTestHelper.Connect(ctx, a2, getMesh.nodeId, "Mesh", sim.nodeId, "__target");
            BlueprintTestHelper.SetPinDefault(ctx, a2, sim.nodeId, "bSimulate", "true");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", sim.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, sim.nodeId, "then", f2.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Phys_Sim_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Phys_Force_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            BlueprintTestHelper.AddComponent(ctx, a3, "Mesh", "StaticMeshComponent");
            var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Pull");
            var getMesh = BlueprintTestHelper.AddNode(ctx, a3, "Pull", "Mesh", 100, 0,
                kind: "getter");
            var force = BlueprintTestHelper.AddNode(ctx, a3, "Pull", "AddForce", 300, 0,
                "PrimitiveComponent");
            BlueprintTestHelper.Connect(ctx, a3, getMesh.nodeId, "Mesh", force.nodeId, "__target");
            BlueprintTestHelper.SetPinDefault(ctx, a3, force.nodeId, "Force",
                "0.000000,0.000000,50.000000");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", force.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a3, force.nodeId, "then", f3.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Phys_Force_B");
    }
}