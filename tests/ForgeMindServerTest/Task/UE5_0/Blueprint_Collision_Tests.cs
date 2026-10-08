// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 37 - Blueprint Collision Events (UE 5.0) - from Tasks/UE_5.0/Blueprint-Collision.md
//
// Probe (collaboration spec point 6):
// - All three events are Actor **BlueprintImplementableEvent**s, not dedicated K2 node
//   classes: this engine build has no K2Node_ActorBeginOverlapEvent /
//   ActorEndOverlapEvent / ActorHitEvent (Editor\BlueprintGraph\Classes only offers
//   K2Node_ActorBoundEvent / K2Node_ComponentBoundEvent). Verified declarations:
//   AActor::ReceiveActorBeginOverlap (Actor.h:1762-1763, DisplayName "ActorBeginOverlap",
//   one output pin **Other Actor**), ReceiveActorEndOverlap (Actor.h:1774-1775, same pin),
//   ReceiveHit (Actor.h:1867-1868, DisplayName "Hit", Category Collision). Nodes are added
//   by the BIE function name, the same way Blueprint_Ease_Tests adds "ReceiveTick".
// - **ReceiveHit's real signature has 8 parameters** (Actor.h:1867-1768): My Comp / Other /
//   Other Comp / Self Moved / Hit Location / Hit Normal / Normal Impulse / Hit. The doc's
//   DSL `(AActor other, Vector Normal, Vector Location)` matches neither the names nor the
//   count -- the translator's override signature check (CheckOverrideSignatureConsistency,
//   same reason Ease_Tests must use `DeltaSeconds`) will reject it.
// - **Triggering requires collision components**: Actor.h:1754/1760/1767 comments state
//   both actors need components with bGenerateOverlapEvents; the checkbox is component-level
//   "Generate Overlap Events" (PrimitiveComponent.h:342-343), and Hit needs "Simulation
//   Generates Hit Events" (BodyInstance.h:319-320). The bridge exposes no component-property
//   setter for that here, so the assets are created without them -- the case verifies event
//   construction, not runtime overlap.
// - DSL side: `void OnOverlap(AActor other)` also mismatches the parameter name (real pin:
//   "Other Actor"); copied verbatim per test-is-spec.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transcribing task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
// =========================================================================

public sealed class Blueprint_Collision : ITestCase
{
    public string Name => "Blueprint Collision Events (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 37;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Event ActorBeginOverlap --
        // MCP steps: Coll_Begin_A (Extends Actor) + Event ActorBeginOverlap
        //   -> Other Actor -> GetName -> PrintString
        const string a1 = "/Game/Tests/UE5_0/Coll_Begin_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var ev1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveActorBeginOverlap", 0, 0);
        var name1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "GetName", 300, 0);
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 550, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, ev1.nodeId, "Other Actor", name1.nodeId, "Target");
        BlueprintTestHelper.Connect(ctx, a1, name1.nodeId, "Return Value", print1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, ev1.nodeId, "then", print1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: Event Hit --
        // MCP steps: Coll_Hit_A (Extends Actor) + Event Hit -> PrintString("hit")
        const string a2 = "/Game/Tests/UE5_0/Coll_Hit_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var ev2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveHit", 0, 0);
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 400, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, print2.nodeId, "InString", "hit");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: Event ActorEndOverlap --
        // MCP steps: Coll_End_A (Extends Actor) + Event ActorEndOverlap -> PrintString("end")
        const string a3 = "/Game/Tests/UE5_0/Coll_End_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveActorEndOverlap", 0, 0);
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 400, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, print3.nodeId, "InString", "end");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}