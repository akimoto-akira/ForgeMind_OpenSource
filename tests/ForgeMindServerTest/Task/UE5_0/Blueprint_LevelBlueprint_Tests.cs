// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 20 - Blueprint Level Blueprint (UE 5.0) - from Tasks/UE_5.0/blueprint-level-blueprint.md
// Refactoring (2026-10-03, user's three decisions):
//   Decision 1=A: bridge adds create_level capability; test uses create_level to self-build level,
//     level blueprint is level-embedded asset (.umap), no longer uses CreateBlueprint to build
//     standalone .uasset (UK2Node_Literal references level Actor can only be stored in the level's
//     own level blueprint; standalone asset cross-package save is rejected by engine --
//     20.md subtask 2/3 not closed-loop root cause).
//   Decision 2=A: two levels for each subtask (_A=MCP steps, _B=DSL), keeping A/B equivalence verification.
//   Decision 3=B: DSL omits Path, bridge's get_current_level injects current level (must
//     CreateLevel to open the corresponding _B level first, then ApplyDsl).
// Dependencies (ready):
//   - Bridge HandleCreateLevel (create_level: path required, idempotent reuse/new save .umap
//     and load as current level); GEditor->NewMap() no modal prompt, test runs unattended.
//   - SaveLoadedPackage selects .umap/.uasset extension by Package->ContainsMap().
//   - ResolveBlueprint level blueprint fallback (LevelScriptActor.ClassGeneratedBy).
//   - LevelBlueprintTranslator.ResolvePath override: explicit Path takes priority, otherwise
//     get_current_level injects levelPath (DSL without Path no longer reports FM1005).
// Preceding test impact (Order 1-19): all build standalone blueprint assets under Content/Tests and immediately
//   compile+save; GroupLifecycle deletes Content/Tests before starting each round; this test
//   self-built levels coexist with other assets in the same directory, no residual dependencies; create_level's
//   internal EditorDestroyWorld discards preceding temporary Actors in default level, no cross-case state leak.
// Note: diff comparison (VerifyIrSnapshot) inserted per user instruction, covering A-side (LevelBP_Begin_A /
// LevelBP_Ref_A / LevelBP_Call_A) and B-side (LevelBP_Begin_B / LevelBP_Ref_B /
// LevelBP_Call_B) all level assets. Level blueprint is a level-embedded asset (.umap); IR read follows
// level path through ResolveBlueprint's level blueprint fallback.
// =========================================================================

public sealed class Blueprint_LevelBlueprint : ITestCase
{
    public string Name => "Blueprint Level Blueprint (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 20;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Event Level BeginPlay --
        // A-side (MCP steps): create_level self-build level -> level blueprint EventGraph add
        //   Event Level BeginPlay (i.e. override event ReceiveBeginPlay) -> PrintString("lvl")
        const string a1 = "/Game/Tests/UE5_0/LevelBP_Begin_A";
        BlueprintTestHelper.CreateLevel(ctx, a1);
        var begin1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 0, 0, kind: "event");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "lvl");
        BlueprintTestHelper.Connect(ctx, a1, begin1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // B-side (DSL): no Path, get_current_level injects current level (open _B level first).
        const string b1 = "/Game/Tests/UE5_0/LevelBP_Begin_B";
        BlueprintTestHelper.CreateLevel(ctx, b1);
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);

        // -- Subtask 2: Level Actor Reference --
        // A-side (MCP steps): self-build level -> place named Actor Target -> level blueprint
        //   Create Reference to Target (UK2Node_Literal literal reference) -> in BeginPlay
        //   Target.PrintName() (GetDisplayName pure function, execution via PrintString).
        const string a2 = "/Game/Tests/UE5_0/LevelBP_Ref_A";
        BlueprintTestHelper.CreateLevel(ctx, a2);
        BlueprintTestHelper.SpawnLevelActor(ctx, "Actor", "Target");
        var ref2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "Target", 100, 0, kind: "levelLiteral");
        var begin2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveBeginPlay", 0, 120, kind: "event");
        var pName2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "GetDisplayName", 400, 0, "KismetSystemLibrary");
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, ref2.nodeId, "ReturnValue", pName2.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a2, pName2.nodeId, "ReturnValue", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, begin2.nodeId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // B-side (DSL): no Path; open _B level first and place Target, then dispatch.
        const string b2 = "/Game/Tests/UE5_0/LevelBP_Ref_B";
        BlueprintTestHelper.CreateLevel(ctx, b2);
        BlueprintTestHelper.SpawnLevelActor(ctx, "Actor", "Target");
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);

        // -- Subtask 3: Call Level Actor Function --
        // A-side (MCP steps): level Target has function Ping() -> in BeginPlay call Ping()
        //   on Target reference (external member call, ownerClass=Actor, "__target"=Target).
        const string a3 = "/Game/Tests/UE5_0/LevelBP_Call_A";
        BlueprintTestHelper.CreateLevel(ctx, a3);
        BlueprintTestHelper.SpawnLevelActor(ctx, "Actor", "Target");
        var ref3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Target", 100, 0, kind: "levelLiteral");
        var begin3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveBeginPlay", 0, 120, kind: "event");
        // markdown's Ping() shows 0 entries in engine index 5.0 (fictional function). Changed to real
        // AActor function SetActorHiddenInGame(bNewHidden: bool).
        var hide3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "SetActorHiddenInGame", 400, 0, "Actor");
        BlueprintTestHelper.SetPinDefault(ctx, a3, hide3.nodeId, "bNewHidden", "true");
        BlueprintTestHelper.Connect(ctx, a3, ref3.nodeId, "ReturnValue", hide3.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a3, begin3.nodeId, "then", hide3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // B-side (DSL): no Path; open _B level first and place Target, then dispatch.
        const string b3 = "/Game/Tests/UE5_0/LevelBP_Call_B";
        BlueprintTestHelper.CreateLevel(ctx, b3);
        BlueprintTestHelper.SpawnLevelActor(ctx, "Actor", "Target");
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);

        await Task.CompletedTask;
    }
}
