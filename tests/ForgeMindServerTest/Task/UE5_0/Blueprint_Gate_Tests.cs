// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 36 - Blueprint Gate (UE 5.0) - from Tasks/UE_5.0/blueprint-gate.md
//
// Probe (collaboration spec point 6):
// - **Every node step below is attempted as written** -- support verification belongs to the
//   implementation phase (spec point 6: write tests directly assuming supported). Where the bridge or
//   the engine cannot do it yet, the case fails with a concrete reason, which is the
//   requirement signal.
// - MultiGate exists and the bridge supports it as the native node name "MultiGate"
//   (CreateNativeFlowNode, BlueprintGraph.cpp:158-161). Real pins (K2Node_MultiGate.cpp:
//   507-511 inputs, 633-636 outputs): inputs `Execute` (inherited) + `Reset`, `IsRandom`
//   (bool), `Loop` (bool), `StartIndex` (int, default -1); outputs are named **"Out 0",
//   "Out 1" ...** (NOT Then0/Then1 -- MultiGate derives from UK2Node_ExecutionSequence,
//   K2Node_MultiGate.h:14-15, but renames the outputs). The doc asks for three outputs
//   (A/B/C), so three are connected here; extra outputs are added with AddInputPin(), which
//   the bridge now exposes via the payload field 'outputs' (see below): without it a third
//   output pin simply does not exist.
// - DoN: the doc passes N as a parameter (`DoN(3, this.Shot)`), so it is written as a pin
//   default named `n` (lowercase -- that is how StandardMacros names the pin). The engine's multi-input variant (UK2Node_DoOnceMultiInput,
//   K2Node_DoOnceMultiInput.h:15) instead holds N in the node property
//   `int32 NumAdditionalInputs` (h:20-22) and its pins carry PinName = None with FriendlyNames
//   "A In" / "A Out" / "Reset In" / "Reset Out" (cpp:180-191). Reconciling the two is an
//   implementation task, not a transcription one.
// - DoN is not a native node either: like DoOnce / FlipFlop / Gate it lives in the engine's
//   StandardMacros library, so the A side builds it with kind = "macro" (same as
//   Blueprint_While_Tests building WhileLoop / ForLoopWithBreak).
// - **Reset must come from an event in the same graph, not from another function graph**: a
//   node of the Trigger / event graph cannot be driven by the entry of a different function
//   graph, so the Reset side is a custom event node (kind = "customevent") sitting next to the
//   trigger in the very same graph (same shape as No. 33 Flow Control's DoOnce / DoN).
// - DSL side: every gating construct here uses the **[FlowControl]** form of
//   requirements\DSL\Blueprint\DSL-syntax.md §4.11.1 (same as DoN in subtask 2), because all of them carry a
//   Reset pin: `[FlowControl(FlowType.MultiGate, ResetName = "Reset", Outputs = 3)]`. The marked
//   method *is* the node -- its top-level statements go one per output (Out 0 / Out 1 / Out 2),
//   which is how the A side wires them -- and the reset entry is a separate `Reset()` method the
//   second event calls. Not the doc's `MultiGate(this.A, this.B, ...)` / `MultiGateReset()`
//   spelling: those would declare named events the A side has no counterpart for.
//
// Note: diff comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9), covering all A-side and B-side assets.
// =========================================================================

public sealed class Blueprint_Gate : ITestCase
{
    public string Name => "Blueprint Gate (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 36;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: MultiGate --
        // MCP steps: Gate_Multi_A (Extends Actor), event graph with two entries:
        //   CustomEvent Trigger -> MultiGate.execute, CustomEvent ResetGate -> MultiGate.Reset
        //   Out 0 / Out 1 / Out 2 each feeding a PrintString
        const string a1 = "/Game/Tests/UE5_0/Gate_Multi_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        // Custom event, not a function graph: the doc says "add Custom Event Trigger" and the
        // DSL side is an [Event] method, so both sides must live in the event graph.
        var trigger1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "Trigger", 0, 0,
            kind: "customevent");
        var reset1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ResetGate", 0, 250,
            kind: "customevent");
        // Three outputs (A/B/C) per the task doc -- MultiGate defaults to two.
        var gate1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "MultiGate", 300, 0, outputs: 3);
        var pA = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 600, -150,
            "KismetSystemLibrary");
        var pB = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 600, 0,
            "KismetSystemLibrary");
        var pC = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 600, 150,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, pA.nodeId, "InString", "a");
        BlueprintTestHelper.SetPinDefault(ctx, a1, pB.nodeId, "InString", "b");
        BlueprintTestHelper.SetPinDefault(ctx, a1, pC.nodeId, "InString", "c");
        BlueprintTestHelper.Connect(ctx, a1, trigger1.nodeId, "then", gate1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, gate1.nodeId, "Out 0", pA.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, gate1.nodeId, "Out 1", pB.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, gate1.nodeId, "Out 2", pC.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, reset1.nodeId, "then", gate1.nodeId, "Reset");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path: [FlowControl] form, same as subtask 2's DoN (§4.11.1) -- the marked method
        // *is* the MultiGate node, its top-level statements go one per output (Out 0/1/2),
        // matching the A side where each output is wired straight into a PrintString node.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Gate_Multi_B");

        // -- Subtask 2: DoN (execute N times, resettable) --
        // MCP steps: Gate_DoN_A (Extends Actor), event graph with two entries:
        //   ReceiveBeginPlay -> DoN.Enter (n = 3), CustomEvent ResetFire -> DoN.Reset
        //   Exit -> PrintString("x")
        // Pin names are the runtime-confirmed StandardMacros ones (TestRuns\20261010_1512:
        // "Node 'Do N' has no pin named 'execute'; its pins are: Enter, n, Reset, Exit,
        // Counter") -- the macro is named "Do N" (with space) because the bridge matches
        // macro graph names exactly.
        const string a2 = "/Game/Tests/UE5_0/Gate_DoN_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var ev2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var rst2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ResetFire", 0, 200,
            kind: "customevent");
        var don2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "Do N", 300, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, a2, don2.nodeId, "n", "3");
        var shot2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, shot2.nodeId, "InString", "x");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "then", don2.nodeId, "Enter");
        BlueprintTestHelper.Connect(ctx, a2, rst2.nodeId, "then", don2.nodeId, "Reset");
        BlueprintTestHelper.Connect(ctx, a2, don2.nodeId, "Exit", shot2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path: anonymous trigger + named Reset entry (§4.11.1), same shape as No. 33's DoN
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Gate_DoN_B");

        // -- Subtask 3: MultiGate with Loop / Reset --
        // MCP steps: Gate_Reset_A (Extends Actor), event graph with two custom events:
        //   Trigger -> MultiGate.execute (Loop = true) -> Out 0 / Out 1 -> PrintString,
        //   ResetGate -> MultiGate.Reset -- an event in the *same* graph, because the Reset pin
        //   sits on that very node and no other graph can reach it.
        const string a3 = "/Game/Tests/UE5_0/Gate_Reset_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var trigger3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Trigger", 0, 0,
            kind: "customevent");
        var reset3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ResetGate", 0, 200,
            kind: "customevent");
        var gate3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "MultiGate", 300, 0);
        var pR0 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 600, -100,
            "KismetSystemLibrary");
        var pR1 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 600, 100,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, pR0.nodeId, "InString", "a");
        BlueprintTestHelper.SetPinDefault(ctx, a3, pR1.nodeId, "InString", "b");
        BlueprintTestHelper.SetPinDefault(ctx, a3, gate3.nodeId, "Loop", "true");
        BlueprintTestHelper.Connect(ctx, a3, trigger3.nodeId, "then", gate3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, gate3.nodeId, "Out 0", pR0.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, gate3.nodeId, "Out 1", pR1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, reset3.nodeId, "then", gate3.nodeId, "Reset");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path: [FlowControl] form, same as subtasks 1 and 2 -- the marked method *is* the
        // MultiGate node (Loop = true), its two statements go to Out 0 / Out 1, and ResetName
        // gives the reset entry the second event calls.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Gate_Reset_B");

        await Task.CompletedTask;
    }
}