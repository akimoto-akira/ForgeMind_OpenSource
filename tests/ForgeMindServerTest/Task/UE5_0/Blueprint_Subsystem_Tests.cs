// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 72 - Blueprint Subsystem (UE 5.0) - from Tasks/UE_5.0/Blueprint_Subsystem.md
//
// Probe (collaboration spec point 6):
// - The Get Subsystem node IS supported: add_blueprint_node kind "subsystem" creates
//   UK2Node_GetSubsystem and resolves the class by name with a U-prefix retry
//   (ForgeMindBridgeClient_BlueprintGraph.cpp:2658-2688 — 'name' is the subsystem class name,
//   e.g. "EnhancedInputLocalPlayerSubsystem"), then Initialize(SubsystemClass).
// - **The doc's subsystem classes cannot be created**: MyGISub / MyWSub / MyLPSub are C++
//   UGameInstanceSubsystem / UWorldSubsystem / ULocalPlayerSubsystem subclasses, and there is
//   no MCP action that writes and compiles a new C++ class. The A side therefore instantiates
//   the Get Subsystem node for the three engine base subsystem classes — the node only needs a
//   subsystem class, and the three scopes it distinguishes are exactly GameInstance / World /
//   LocalPlayer (UK2Node_GetSubsystem::ExpandNode lowers to
//   USubsystemBlueprintLibrary::GetGameInstanceSubsystem / GetWorldSubsystem /
//   GetLocalPlayerSubsystem, SubsystemBlueprintLibrary.h:26 / :34 / :30).
// - The base subsystem classes expose no BlueprintCallable members, so "call Ping / Tick / Init"
//   has no engine counterpart on them; the A side consumes the node's output with
//   UKismetSystemLibrary::GetDisplayName (the same object-to-text path Blueprint_SoftRef uses)
//   and prints it. Both deviations are stated here instead of being silently absorbed.
// - DSL side (narrowed 2026-10-21 per spec §6 "verify first, then write"): the doc's
//   `GetSubsystem<T>()` with user-defined subsystem classes (MyGISub / MyWSub / MyLPSub) and
//   `s.Ping() / s.Tick() / s.Init()` is not writable here — `GetSubsystem` is not an engine
//   FName/DisplayName (SpkTool resolve: NoDeclaration), those subclasses cannot be created (no
//   MCP action compiles C++), and the base subsystem classes expose no BlueprintCallable members.
//   The B side therefore calls the three real USubsystemBlueprintLibrary accessors, each verified
//   Resolved by SpkTool, and consumes the returned object the same way the A side does
//   (GetDisplayName -> PrintString). Intent is narrowed from "derive a subsystem and call its
//   own method" to "fetch an engine subsystem by class and consume the object".
// - Node parity (user ruling 2026-10-23): writing those accessors used to produce a plain
//   CallFunction + Self node, which is NOT the node the A side builds (UK2Node_GetSubsystem) —
//   the graphs disagreed even though both compiled. Engine fact: UK2Node_GetSubsystem::ExpandNode
//   expands into exactly these three [Pure] accessors (SubsystemBlueprintLibrary.h:26 / :30 / :34),
//   so the DSL spelling IS the node's expanded form; the translator now recognises the static
//   shape (bare call, first argument this/self, second a typeof(X) literal, X an index-verified
//   subsystem of that accessor's scope) and raises it back to the Get Subsystem node, so A and B
//   are the same graph. Any other shape (e.g. a variable as the context object) is left as the
//   library call — the raise never guesses.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9). Each A-side (MCP) and DSL-side asset stores its own graph-level IR
// baseline on first run (no failure); later runs diff against it, catching accidental damage
// to already-tested structures (per-asset self-comparison, not A/B cross-comparison). The
// B-side baselines are the guard for today's node-parity fix: they pin the raised
// Subsystem node (and its ReturnValue wiring into the local-variable write), so a regression
// in TryLowerGetSubsystemCall / ValueSourceNodeTypes shows up as a snapshot diff instead of a
// silently rebuilt graph. Each A-side blueprint here compiles only once, so the snapshot sits
// right after that CompileAndSave; the three subsystem class names differ per subtask, which
// is part of what the baseline pins.
// =========================================================================

public sealed class Blueprint_Subsystem : ITestCase
{
    public string Name => "Blueprint Subsystem (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 72;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: GameInstance subsystem --
        // MCP steps: Sub_GI_A (Extends Actor) + function U: Get GameInstance Subsystem
        //   -> GetDisplayName -> PrintString
        const string a1 = "/Game/Tests/UE5_0/Sub_GI_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "U");
        var sub1 = BlueprintTestHelper.AddNode(ctx, a1, "U", "GameInstanceSubsystem", 200, 0,
            kind: "subsystem");
        var name1 = BlueprintTestHelper.AddNode(ctx, a1, "U", "GetDisplayName", 400, -100,
            "KismetSystemLibrary");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "U", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, sub1.nodeId, "ReturnValue", name1.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a1, name1.nodeId, "Return Value", print1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", print1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, print1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Sub_GI_B");

        // -- Subtask 2: World subsystem --
        // MCP steps: Sub_World_A (Extends Actor) + function W: Get World Subsystem
        //   -> GetDisplayName -> PrintString
        const string a2 = "/Game/Tests/UE5_0/Sub_World_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "W");
        var sub2 = BlueprintTestHelper.AddNode(ctx, a2, "W", "WorldSubsystem", 200, 0,
            kind: "subsystem");
        var name2 = BlueprintTestHelper.AddNode(ctx, a2, "W", "GetDisplayName", 400, -100,
            "KismetSystemLibrary");
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "W", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, sub2.nodeId, "ReturnValue", name2.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a2, name2.nodeId, "Return Value", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Sub_World_B");

        // -- Subtask 3: LocalPlayer subsystem --
        // MCP steps: Sub_LP_A (Extends Actor) + function L: Get LocalPlayer Subsystem
        //   -> GetDisplayName -> PrintString
        const string a3 = "/Game/Tests/UE5_0/Sub_LP_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "L");
        var sub3 = BlueprintTestHelper.AddNode(ctx, a3, "L", "LocalPlayerSubsystem", 200, 0,
            kind: "subsystem");
        var name3 = BlueprintTestHelper.AddNode(ctx, a3, "L", "GetDisplayName", 400, -100,
            "KismetSystemLibrary");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "L", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, sub3.nodeId, "ReturnValue", name3.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a3, name3.nodeId, "Return Value", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, print3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Sub_LP_B");

        await Task.CompletedTask;
    }
}
