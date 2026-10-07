// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 25 - Blueprint Level Streaming (UE 5.0) - from Tasks/UE_5.0/blueprint-level-streaming.md
// Probe (spec point 6): LoadStreamLevel / UnloadStreamLevel are UGameplayStatics static
// (latent) functions, called via ownerClass="GameplayStatics".
//
// Bridge review (2026/10/05, TestRuns\20261005_1257): task doc task 3 original design
// "MakeLatentActionInfo + delegate binding OnLevelLoaded" doesn't hold in engine --
// UE 5.0.3 full source tree has no MakeLatentActionInfo UFUNCTION; FLatentActionInfo is an
// internal latent mechanism struct (blueprint VM auto-fills when compiling latent nodes;
// LatentInfo pin not exposed for user wiring). The "completion callback" for latent nodes is
// correctly the **completion branch wiring**:
// output exec pin name "then" (display name "Completed", K2Node_CallFunction.cpp:874-882
// only changes PinFriendlyName). Task 3 both sides changed to this form (load completion branch -> PrintString).
//
// Visual feedback correction (2026/10/05, 1322 run visual): nodes with latent trigger pins (latent)
// can only be used in event graphs (EventGraph), not in function bodies -- same family constraint as
// "custom events can only be in event graphs". All three A-side tasks changed to event graph form:
// custom event (customevent) as trigger source, LoadStreamLevel/UnloadStreamLevel and subsequent
// PrintString all wired in EventGraph.
//
// Note: diff comparison (VerifyIrSnapshot) inserted 2026/10/05 per user instruction --
// A-side three blueprints (Stream_Load_A / Stream_Unload_A / Stream_Cb_A) each compared once
// against IR baseline after compile+save (first run generates baseline, subsequent runs full structural comparison).
// =========================================================================

public sealed class Blueprint_LevelStreaming : ITestCase
{
    public string Name => "Blueprint Level Streaming (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 25;

    public async Task Run(TestContext ctx)
    {
        // -- Task 1: LoadStreamLevel --
        // MCP steps: Stream_Load_A (Extends Actor) + event graph custom event Load ->
        //   LoadStreamLevel(LevelName="Sub", bMakeVisibleAfterLoad=true, bShouldBlockOnLoad=true)
        // latent nodes only fit in event graphs (visual feedback 2026/10/05), trigger source uses customevent.
        const string s1 = "/Game/Tests/UE5_0/Stream_Load_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, s1);
        var ev1 = BlueprintTestHelper.AddNode(ctx, s1, "EventGraph", "Load", 0, 0, kind: "customevent");
        var load1 = BlueprintTestHelper.AddNode(ctx, s1, "EventGraph", "LoadStreamLevel", 300, 0, "GameplayStatics");
        // Pins by engine real name (GameplayStatics.h:268 LoadStreamLevel(LevelName,
        // bMakeVisibleAfterLoad, bShouldBlockOnLoad, LatentInfo)) -- task doc's
        // "MakeVisible"/"bShouldBlock" are colloquial; dispatching directly rejected by bridge (no pin named).
        BlueprintTestHelper.SetPinDefault(ctx, s1, load1.nodeId, "LevelName", "Sub");
        BlueprintTestHelper.SetPinDefault(ctx, s1, load1.nodeId, "bMakeVisibleAfterLoad", "true");
        BlueprintTestHelper.SetPinDefault(ctx, s1, load1.nodeId, "bShouldBlockOnLoad", "true");
        BlueprintTestHelper.Connect(ctx, s1, ev1.nodeId, "then", load1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, s1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, s1);

        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Task 2: UnloadStreamLevel --
        // MCP steps: Stream_Unload_A + event graph custom event Unload ->
        //   UnloadStreamLevel(LevelName="Sub") (latent only fits in event graph)
        const string s2 = "/Game/Tests/UE5_0/Stream_Unload_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, s2);
        var ev2 = BlueprintTestHelper.AddNode(ctx, s2, "EventGraph", "Unload", 0, 0, kind: "customevent");
        var un2 = BlueprintTestHelper.AddNode(ctx, s2, "EventGraph", "UnloadStreamLevel", 300, 0, "GameplayStatics");
        BlueprintTestHelper.SetPinDefault(ctx, s2, un2.nodeId, "LevelName", "Sub");
        BlueprintTestHelper.Connect(ctx, s2, ev2.nodeId, "then", un2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, s2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, s2);

        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Task 3: LoadStreamLevel Completion Callback --
        // MCP steps: Stream_Cb_A + event graph custom event Load -> LoadStreamLevel(...)
        //   completion branch (then, display name Completed) -> PrintString("loaded")
        // Bridge review: engine has no MakeLatentActionInfo (FLatentActionInfo auto-filled by blueprint VM,
        // LatentInfo pin hidden), completion callback = latent completion branch wiring;
        // latent only fits in event graph (visual feedback 2026/10/05), all wired in EventGraph.
        const string s3 = "/Game/Tests/UE5_0/Stream_Cb_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, s3);
        var ev3 = BlueprintTestHelper.AddNode(ctx, s3, "EventGraph", "Load", 0, 0, kind: "customevent");
        var load3 = BlueprintTestHelper.AddNode(ctx, s3, "EventGraph", "LoadStreamLevel", 300, 0, "GameplayStatics");
        BlueprintTestHelper.SetPinDefault(ctx, s3, load3.nodeId, "LevelName", "Sub");
        BlueprintTestHelper.SetPinDefault(ctx, s3, load3.nodeId, "bMakeVisibleAfterLoad", "true");
        BlueprintTestHelper.SetPinDefault(ctx, s3, load3.nodeId, "bShouldBlockOnLoad", "true");
        var p3 = BlueprintTestHelper.AddNode(ctx, s3, "EventGraph", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, s3, p3.nodeId, "InString", "loaded");
        BlueprintTestHelper.Connect(ctx, s3, ev3.nodeId, "then", load3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, s3, load3.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, s3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, s3);

        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}
