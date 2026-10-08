// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 32 - Blueprint Particle (UE 5.0) - from Tasks/UE_5.0/Blueprint-Particle.md
//
// Probe (collaboration spec point 6):
// - UGameplayStatics::SpawnEmitterAtLocation (GameplayStatics.h:489-490) real pin names:
//   WorldContextObject (hidden) / EmitterTemplate / Location / Rotation / Scale /
//   bAutoDestroy / PoolingMethod / **bAutoActivateSystem** -- note the last one is
//   "Auto Activate System", NOT "bAutoActivate" (same as 5.0.3 source line).
// - UGameplayStatics::SpawnEmitterAttached (GameplayStatics.h:522-523) real pin names:
//   EmitterTemplate / AttachToComponent / AttachPointName / Location / Rotation / Scale /
//   **LocationType** / bAutoDestroy / PoolingMethod / bAutoActivate. It has **no**
//   WorldContextObject (no world meta) and **no** AutoDestroyParameters (5.4+ only) --
//   the doc's two-argument shape is therefore incomplete on the MCP side too.
// - Activate / Deactivate are UActorComponent UFUNCTIONs (ActorComponent.h:403-404),
//   BlueprintCallable, inherited by UParticleSystemComponent (which only overrides them
//   in C++, ParticleSystemComponent.h:1417-1419 -- no UFUNCTION there). Pin names:
//   Activate(bReset) -> pin "Reset"; Deactivate() has no pin. UParticleSystemComponent's
//   own BlueprintCallable API is SetAutoAttachmentParameters (:419) / ReleaseToPool (:437);
//   ActivateSystem (:442) is NOT a UFUNCTION and cannot be called from a blueprint.
// - **Prerequisite particle asset missing**: doc needs a ParticleSystem asset (P_sys).
//   The bridge has no create_particle_system action (same gap as
//   Blueprint_DynamicMaterial_Tests' BaseMat placeholder), so P_sys / CF-style asset
//   defaults stay empty and EmitterTemplate is left unconnected. Pending requirement.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transcribing task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
// =========================================================================

public sealed class Blueprint_Particle : ITestCase
{
    public string Name => "Blueprint Particle (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 32;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: SpawnEmitterAtLocation --
        // MCP steps: Particle_At_A (Extends Actor) + P_sys : ParticleSystem
        //   function Boom(Vector loc) -> SpawnEmitterAtLocation(loc)
        const string a1 = "/Game/Tests/UE5_0/Particle_At_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "P_sys", "ParticleSystem");
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Boom",
            new[] { new { name = "loc", type = "vector" } });
        var spawn1 = BlueprintTestHelper.AddNode(ctx, a1, "Boom", "SpawnEmitterAtLocation", 300, 0,
            "GameplayStatics");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "loc", spawn1.nodeId, "Location");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", spawn1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, spawn1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: SpawnEmitterAttached --
        // MCP steps: Particle_Att_A (Extends Actor, Mesh : StaticMeshComponent)
        //   function Trail() -> SpawnEmitterAttached(AttachToComponent = Mesh)
        const string a2 = "/Game/Tests/UE5_0/Particle_Att_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "P_sys", "ParticleSystem");
        var comp2 = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_component", new
        {
            path = a2,
            name = "Mesh",
            componentType = "StaticMeshComponent",
            sceneRoot = true,
        });
        ctx.AssertTrue(comp2["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_component Mesh @ Particle_Att_A: "
            + $"{comp2["response"]?["reason"]?.GetValue<string>() ?? ""}");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Trail");
        var spawn2 = BlueprintTestHelper.AddNode(ctx, a2, "Trail", "SpawnEmitterAttached", 300, 0,
            "GameplayStatics");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", spawn2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, spawn2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: activate / deactivate a particle component --
        // MCP steps: Particle_Sw_A (Extends Actor, FX : ParticleSystemComponent)
        //   function Toggle(bool on) -> Activate(FX) / Deactivate(FX)
        const string a3 = "/Game/Tests/UE5_0/Particle_Sw_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var comp3 = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_component", new
        {
            path = a3,
            name = "FX",
            componentType = "ParticleSystemComponent",
            sceneRoot = true,
        });
        ctx.AssertTrue(comp3["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_component FX @ Particle_Sw_A: "
            + $"{comp3["response"]?["reason"]?.GetValue<string>() ?? ""}");
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Toggle",
            new[] { new { name = "on", type = "bool" } });
        var act3 = BlueprintTestHelper.AddNode(ctx, a3, "Toggle", "Activate", 300, -100,
            "ParticleSystemComponent");
        var deact3 = BlueprintTestHelper.AddNode(ctx, a3, "Toggle", "Deactivate", 300, 100,
            "ParticleSystemComponent");
        var br3 = BlueprintTestHelper.AddNode(ctx, a3, "Toggle", "Branch", 500, 0);
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "on", br3.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", br3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, br3.nodeId, "True", act3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, br3.nodeId, "False", deact3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, act3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, deact3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}