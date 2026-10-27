// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 64 - Blueprint Audio (UE 5.0) - from Tasks/UE_5.0/Blueprint_Audio.md
//
// Probe (collaboration spec point 6):
// - UGameplayStatics sound functions (engine index 5.0, verified with ForgeMind.SpkTool):
//     PlaySound2D(WorldContextObject, Sound, VolumeMultiplier, PitchMultiplier, StartTime,
//                 ConcurrencySettings, OwningActor, bIsUISound) : void   @GameplayStatics.h:614
//     PlaySoundAtLocation(WorldContextObject, Sound, Location, Rotation, ...) : void  @:666
//     SpawnSound2D(@:633) / SpawnSoundAtLocation(@:688) / SpawnSoundAttached(@:712) — the
//       Spawn* family returns UAudioComponent and is what the editor's "Play Sound ..." node
//       titles map onto for the attached case.
// - **The doc's `PlaySoundAttached` does not exist**: 5.0.3 has no such UFUNCTION (no
//   declaration site in the index, and no PlaySoundAttached anywhere under
//   Engine\Source\Runtime\Engine\Classes). The attached flavour is SpawnSoundAttached
//   (USoundBase* Sound, USceneComponent* AttachToComponent, FName AttachPointName, ...).
//   Subtask 3 and its DSL therefore use SpawnSoundAttached — writing an unresolvable name
//   would mask the gap instead of exposing it (spec point 8).
// - Pin names are the real UFUNCTION parameter names (Sound / Location / AttachToComponent);
//   the WorldContextObject pin is hidden and auto-supplied (same as every Kismet call in
//   Blueprint_Particle / Blueprint_Curve).
// - Prerequisite asset missing: the doc needs a SoundBase asset (SfxAsset). The bridge has no
//   create_sound action, so the variable default stays empty — the wiring (and its compile)
//   is still fully exercised, exactly like Blueprint_Particle's P_sys placeholder.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison).
// =========================================================================

public sealed class Blueprint_Audio : ITestCase
{
    public string Name => "Blueprint Audio (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 64;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: PlaySound2D --
        // MCP steps: Audio_2D_A (Extends Actor) + SfxAsset : SoundBase
        //   function Sfx -> Play Sound 2D(Sound = SfxAsset)
        const string a1 = "/Game/Tests/UE5_0/Audio_2D_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "SfxAsset", "SoundBase");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Sfx");
        var getSfx1 = BlueprintTestHelper.AddNode(ctx, a1, "Sfx", "SfxAsset", 100, -50, kind: "getter");
        var play1 = BlueprintTestHelper.AddNode(ctx, a1, "Sfx", "PlaySound2D", 300, 0, "GameplayStatics");
        BlueprintTestHelper.Connect(ctx, a1, getSfx1.nodeId, "SfxAsset", play1.nodeId, "Sound");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", play1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, play1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Audio_2D_B");

        // -- Subtask 2: PlaySoundAtLocation --
        // MCP steps: Audio_At_A (Extends Actor) + SfxAsset : SoundBase
        //   function SfxAt(Vector loc) -> Play Sound at Location(Sound, Location = loc)
        const string a2 = "/Game/Tests/UE5_0/Audio_At_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "SfxAsset", "SoundBase");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "SfxAt",
            new[] { new { name = "loc", type = "vector" } });
        var getSfx2 = BlueprintTestHelper.AddNode(ctx, a2, "SfxAt", "SfxAsset", 100, -50, kind: "getter");
        var play2 = BlueprintTestHelper.AddNode(ctx, a2, "SfxAt", "PlaySoundAtLocation", 300, 0,
            "GameplayStatics");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "loc", play2.nodeId, "Location");
        BlueprintTestHelper.Connect(ctx, a2, getSfx2.nodeId, "SfxAsset", play2.nodeId, "Sound");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", play2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, play2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Audio_At_B");

        // -- Subtask 3: play sound attached to a component --
        // MCP steps: Audio_Att_A (Extends Actor, Mesh : StaticMeshComponent) + SfxAsset : SoundBase
        //   function SfxAttached -> SpawnSoundAttached(Sound = SfxAsset, AttachToComponent = Mesh)
        const string a3 = "/Game/Tests/UE5_0/Audio_Att_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "SfxAsset", "SoundBase");
        BlueprintTestHelper.AddComponent(ctx, a3, "Mesh", "StaticMeshComponent");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "SfxAttached");
        var getSfx3 = BlueprintTestHelper.AddNode(ctx, a3, "SfxAttached", "SfxAsset", 100, -50,
            kind: "getter");
        var getMesh3 = BlueprintTestHelper.AddNode(ctx, a3, "SfxAttached", "Mesh", 100, 120,
            kind: "getter");
        // "Play Sound Attached" real FName is SpawnSoundAttached (see file header).
        var play3 = BlueprintTestHelper.AddNode(ctx, a3, "SfxAttached", "SpawnSoundAttached", 300, 0,
            "GameplayStatics");
        BlueprintTestHelper.Connect(ctx, a3, getSfx3.nodeId, "SfxAsset", play3.nodeId, "Sound");
        BlueprintTestHelper.Connect(ctx, a3, getMesh3.nodeId, "Mesh", play3.nodeId, "AttachToComponent");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", play3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, play3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc; PlaySoundAttached corrected to the real FName
        // SpawnSoundAttached — see file header)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Audio_Att_B");

        await Task.CompletedTask;
    }
}
