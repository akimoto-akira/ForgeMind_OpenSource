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
// Note: diff comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9), covering all A-side and B-side assets.
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
        // DSL SpawnEmitterAtLocation(P_sys, loc): P_sys getter -> EmitterTemplate
        var getPsys1 = BlueprintTestHelper.AddNode(ctx, a1, "Boom", "P_sys", 100, -50, kind: "getter");
        var spawn1 = BlueprintTestHelper.AddNode(ctx, a1, "Boom", "SpawnEmitterAtLocation", 300, 0,
            "GameplayStatics");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "loc", spawn1.nodeId, "Location");
        BlueprintTestHelper.Connect(ctx, a1, getPsys1.nodeId, "P_sys", spawn1.nodeId, "EmitterTemplate");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", spawn1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, spawn1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Particle_At_B");

        // -- Subtask 2: SpawnEmitterAttached --
        // MCP steps: Particle_Att_A (Extends Actor, Mesh : StaticMeshComponent)
        //   function Trail() -> SpawnEmitterAttached(AttachToComponent = Mesh)
        const string a2 = "/Game/Tests/UE5_0/Particle_Att_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "P_sys", "ParticleSystem");
        // add_blueprint_component contract: class field is 'class' (not 'componentType'),
        // no 'sceneRoot' field -- a new SCS component attaches to DefaultSceneRoot already
        // (see BlueprintTestHelper.AddComponent).
        BlueprintTestHelper.AddComponent(ctx, a2, "Mesh", "StaticMeshComponent");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Trail");
        // DSL SpawnEmitterAttached(P_sys, Mesh): P_sys getter -> EmitterTemplate,
        // Mesh component getter -> AttachToComponent (static lib plain ref pin, not "__target").
        var getPsys2 = BlueprintTestHelper.AddNode(ctx, a2, "Trail", "P_sys", 100, -50, kind: "getter");
        var getMesh2 = BlueprintTestHelper.AddNode(ctx, a2, "Trail", "Mesh", 100, 120, kind: "getter");
        var spawn2 = BlueprintTestHelper.AddNode(ctx, a2, "Trail", "SpawnEmitterAttached", 300, 0,
            "GameplayStatics");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", spawn2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, getPsys2.nodeId, "P_sys", spawn2.nodeId, "EmitterTemplate");
        BlueprintTestHelper.Connect(ctx, a2, getMesh2.nodeId, "Mesh", spawn2.nodeId, "AttachToComponent");
        BlueprintTestHelper.Connect(ctx, a2, spawn2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Particle_Att_B");

        // -- Subtask 3: activate / deactivate a particle component --
        // MCP steps: Particle_Sw_A (Extends Actor, FX : ParticleSystemComponent)
        //   function Toggle(bool on) -> Activate(FX) / Deactivate(FX)
        const string a3 = "/Game/Tests/UE5_0/Particle_Sw_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddComponent(ctx, a3, "FX", "ParticleSystemComponent");
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Toggle",
            new[] { new { name = "on", type = "bool" } });
        // DSL FX.Activate() / FX.Deactivate(): FX getter -> member call "__target"
        // (self) pin -- component member calls use the external member node pattern,
        // same as Blueprint_DynamicMaterial_Tests.
        var getFX3 = BlueprintTestHelper.AddNode(ctx, a3, "Toggle", "FX", 100, 0, kind: "getter");
        var act3 = BlueprintTestHelper.AddNode(ctx, a3, "Toggle", "Activate", 300, -100,
            "ParticleSystemComponent");
        var deact3 = BlueprintTestHelper.AddNode(ctx, a3, "Toggle", "Deactivate", 300, 100,
            "ParticleSystemComponent");
        var br3 = BlueprintTestHelper.AddNode(ctx, a3, "Toggle", "Branch", 500, 0);
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "on", br3.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", br3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, getFX3.nodeId, "FX", act3.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a3, getFX3.nodeId, "FX", deact3.nodeId, "__target");
        // Branch output exec pins are "then"/"else" (editor shows True/False via
        // PinFriendlyName) -- same convention as Blueprint_If_Tests.
        BlueprintTestHelper.Connect(ctx, a3, br3.nodeId, "then", act3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, br3.nodeId, "else", deact3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, act3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, deact3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Particle_Sw_B");

        await Task.CompletedTask;
    }
}