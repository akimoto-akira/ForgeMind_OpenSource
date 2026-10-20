// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 42 - Blueprint Timeline (UE 5.0) - from Tasks/UE_5.0/blueprint-timeline.md
//
// Probe (collaboration spec point 6):
// - At write time the DSL translator recognised the [Timeline] method attribute but
//   only emitted a function graph with a Warn; the bridge had no "timeline" kind and
//   no MCP action created timeline components, tracks, or keyframes. All tasks below
//   were written per the "already supported" assumption - the unknown-kind rejection
//   WAS the pinned gap to implement.
// - IMPLEMENTED (2026-10-12, run 20261012_1608 failure feedback): bridge
//   add_blueprint_node gained kind "timeline" (creates the UTimelineTemplate +
//   UK2Node_Timeline with a default float track named after the timeline);
//   ForgeMindBridgeClient_AssetIR maps node type "Timeline" to that kind and
//   forwards the node's "length"; the translator lowers [Timeline] methods to a
//   real timeline node (body chained from the Update exec output, first parameter
//   bound to the track value pin) and T.Play()/Reverse()/Stop() to exec inputs.
//   The track's CurveFloat is left null (no curve asset is created) - the pin
//   exists for wiring, which is what these construction tests verify.
// - A-side timeline node exec pin names per UK2Node_Timeline: Play / Reverse / Stop,
//   Update output for the float track.
//
// Note: mis-touch comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9) - one call per asset, A side after CompileAndSave
// and B side after ApplyDsl, same placement as the other test cases. Baselines
// live under TestRuns/<ir version>/<assetName>.json and are written on first run.
// =========================================================================

// =========================================================================
// Visual inspection record (2026-10-12, user visual inspection).
// This is a timeline node's external appearance as seen in the editor.
//
// Input exec pins (triggers):
// - Play (play from the current position)
// - Play From Start
// - Stop (acts as pause)
// - Reverse (reverse from the current position)
// - Reverse From End
// - Set New Time (set the current position), plus a float parameter of
//   Set New Time (New Time)
//
// Output exec pins:
// - Update (fires every frame)
// - Finished (fires on the end frame)
//
// Invariant data outputs:
// - Direction (playback direction: forward, reverse)
//
// Track kinds (each track is a same-named pin on the timeline's output side):
// - Float
// - Vector
// - Color
// - Event (output is an exec pin)
// =========================================================================

// =========================================================================
// SYNTAX REDESIGN (2026-10-12): the [Timeline] method-attribute form is replaced
// by a nested [Timeline] class -
//   class name                       = timeline variable name
//   [Timeline(Length = ...)]         = timeline length in seconds
//   fields (float/Vector/Color)      = value tracks (same-named output pins),
//                                      Curve((t, v), ...) = inline keyframes
//                                      (time seconds, value; Vector(x,y,z) /
//                                      Color(r,g,b) for composite values),
//                                      Curve("/Game/...") = external curve asset
//   EventTrack field + At(t, ...)    = event track, keyframe times; output is
//                                      the same-named exec pin
//   [Update] / [Finished] methods    = the Update / Finished exec outputs' bodies
//                                      (track values enter via same-named params)
//   [EventTrack("X")] method         = the event-track pin's trigger body
//   T.Play()/PlayFromStart()/Stop()/Reverse()/ReverseFromEnd()/SetNewTime(x)
//                                    = control calls to the exec input pins
//                                      (SetNewTime(x) feeds the NewTime pin)
// The DSL blocks below pin the new syntax; the bridge-side timeline decoder
// (tracks, curves, Finished/event-track anchors) is expected to lag - failures
// there ARE the pinned gaps to implement (collaboration spec point 6). A-side
// MCP steps stay on the kind=timeline construction form (empty-curve track),
// unchanged this round.
// =========================================================================

public sealed class Blueprint_Timeline : ITestCase
{
    public string Name => "Blueprint Timeline (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 42;

    public async Task Run(TestContext ctx)
    {
        // -- Task 1: Float track --
        // MCP steps: Time_Float_A (Extends Actor) + Timeline "T" with a float track (0->1, 1s)
        //   -> timeline Update output connects PrintString(Float).
        const string a1 = "/Game/Tests/UE5_0/Time_Float_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        // Track + curve data is passed explicitly (tracks payload): the bridge never
        // invents keyframes, so the A side must declare the same keyframes the DSL
        // B side declares below - same count, same times, same values.
        // Same float curve as Time_Float_B's `Curve((0.0f, 0.0f), (1.0f, 1.0f))`.
        object[] floatTrack =
        [
            new Dictionary<string, object>
            {
                ["name"] = "v",
                ["type"] = "float",
                ["keys"] = new object[]
                {
                    new Dictionary<string, object> { ["t"] = 0.0, ["v"] = 0.0 },
                    new Dictionary<string, object> { ["t"] = 1.0, ["v"] = 1.0 },
                },
            },
        ];
        var tl1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "T", 0, 0, kind: "timeline",
            tracks: floatTrack);
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 300, 0,
            "KismetSystemLibrary");
        // The engine's "Update" pin is an EXEC output (K2Node_Timeline.cpp L80/L110:
        // UpdatePinName = "Update", CreatePin(EGPD_Output, PC_Exec, ...)), and the
        // bridge has no autocast for exec->data - so the original single connect
        // Update -> InString was an exec-output-to-string-input mismatch that the
        // K2 schema rejects. The float track value is a separate output pin named
        // after the track ("v" - the track declared above, previously the bridge's
        // default track named after the timeline "T"), and float->string does
        // autocast (Conv_FloatToString). Fixed here accordingly.
        BlueprintTestHelper.Connect(ctx, a1, tl1.nodeId, "Update", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, tl1.nodeId, "v", p1.nodeId, "InString");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path - redesigned syntax: nested [Timeline] class, float track with
        // inline keyframes, body chained from the Update exec output.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Time_Float_B");

        // -- Task 2: Event track --
        // MCP steps: Time_Event_A + Timeline "T2" with an event track (fires "Fire" at 0.5s)
        //   -> Fire connects PrintString("Boom").
        // The event track is declared through the tracks payload (its keyframe time is
        // data, not a node), and the timeline's Fire exec output drives PrintString -
        // the same shape the DSL B side produces below. Previously the A side faked the
        // event with a standalone custom event because the MCP path had no track
        // support; that also left the A side's timeline without any keyframe at all.
        const string a2 = "/Game/Tests/UE5_0/Time_Event_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        object[] eventTrack =
        [
            new Dictionary<string, object>
            {
                ["name"] = "Fire",
                ["type"] = "event",
                ["times"] = new object[] { 0.5 },
            },
        ];
        var tl2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "T2", 0, 0, kind: "timeline",
            tracks: eventTrack);
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 500, 200,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, p2.nodeId, "InString", "Boom");
        // Event track output: an exec pin named after the track (K2Node_Timeline.cpp
        // AllocateDefaultPins: event tracks create PC_Exec outputs).
        BlueprintTestHelper.Connect(ctx, a2, tl2.nodeId, "Fire", p2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path - redesigned syntax: event track with keyframe time + trigger body.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Time_Event_B");

        // -- Task 3: Reverse and Stop --
        // MCP steps: Time_Ctrl_A + Timeline "T3" (same float track as the B side);
        //   ReceiveBeginPlay -> Play, custom event Back -> Reverse, StopE -> Stop,
        //   SetTime -> SetNewTime (NewTime pin default 0.5).
        const string a3 = "/Game/Tests/UE5_0/Time_Ctrl_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var tl3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "T3", 300, 0, kind: "timeline",
            tracks: floatTrack);
        var beg3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var back3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Back", 0, 200,
            kind: "customevent");
        var stop3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "StopE", 0, 400,
            kind: "customevent");
        var setTime3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "SetTime", 0, 600,
            kind: "customevent");
        BlueprintTestHelper.Connect(ctx, a3, beg3.nodeId, "then", tl3.nodeId, "Play");
        BlueprintTestHelper.Connect(ctx, a3, back3.nodeId, "then", tl3.nodeId, "Reverse");
        BlueprintTestHelper.Connect(ctx, a3, stop3.nodeId, "then", tl3.nodeId, "Stop");
        // Set New Time: the exec input fires it, the float argument is the NewTime pin
        // default (unconnected data pins read their default - same as the B side,
        // where T3.SetNewTime(0.5f) lowers to the pin default).
        BlueprintTestHelper.Connect(ctx, a3, setTime3.nodeId, "then", tl3.nodeId, "SetNewTime");
        BlueprintTestHelper.SetPinDefault(ctx, a3, tl3.nodeId, "NewTime", "0.5");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path - redesigned syntax: control calls to the exec input pins
        // (Play / Reverse / Stop per the task doc; SetNewTime(0.5f) added per the
        // visual inspection record - it feeds the NewTime float pin). One control
        // call per event body: a timeline exec input has no exec output, so calls
        // cannot be chained within one body.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Time_Ctrl_B");

        await Task.CompletedTask;
    }
}
