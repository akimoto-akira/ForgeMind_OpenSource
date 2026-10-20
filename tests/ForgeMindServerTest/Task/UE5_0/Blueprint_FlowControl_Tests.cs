// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 33 - Blueprint Flow Control (UE 5.0) - from Tasks/UE_5.0/blueprint-flow-control.md
//
// Redesigned so that every node of the flow-control family is exercised on its own, each with
// an MCP (A) side and a DSL (B) side. This file owns **Sequence / FlipFlop / DoOnce / DoN /
// Gate**; MultiGate belongs to No. 36 (Blueprint Gate) and is deliberately not duplicated
// here. Per collaboration spec point 6 every step is attempted as written -- where the bridge
// or the translator cannot do it yet, the case fails with a concrete reason and that failure
// is the requirement signal.
//
// Probe results behind the design:
// - Sequence: bridge native name "Sequence" (CreateNativeFlowNode, BlueprintGraph.cpp:160-163)
//   -> UK2Node_ExecutionSequence. Output pins are **then_0 / then_1**: GetPinNameGivenIndex
//   (K2Node_ExecutionSequence.cpp:261-264) formats as PN_Then + "_%d", NOT "Then0"; two
//   outputs are created by default (cpp:141-145).
// - FlipFlop / DoOnce / DoN: **no K2Node exists for them in 5.0.3** -- the flow-control family
//   in Editor\BlueprintGraph\Classes is only K2Node_ExecutionSequence / K2Node_MultiGate /
//   K2Node_DoOnceMultiInput, and the bridge native vocabulary is Branch / Sequence /
//   MultiGate / SwitchOnInt / SwitchOnString. They live in the engine's StandardMacros
//   library, so the A side builds them exactly the way Blueprint_While_Tests builds WhileLoop /
//   ForLoopWithBreak: add_blueprint_node with kind = "macro" (the bridge itself lists
//   "DoOnce, ForEachLoop, ForLoop, Gate, MultiGate, Sequence, WhileLoop, ..." as system macros
//   in its own error text, BlueprintGraph.cpp:1626).
// - Their pin names below are **runtime-confirmed** (connect_blueprint_pins answers a wrong
//   name with the node's real pin list, which is how then_0 was found; TestRuns\20261010_1512
//   pinned Do N down to "Enter / n / Reset / Exit / Counter"):
//   DoOnce = execute / Reset / Start Closed / Completed, DoN = Enter / n / Reset / Exit /
//   Counter, FlipFlop = None (unnamed entry) / A / B / IsA. Gate follows
//   domain-support.md -- Enter / Open / Close / Toggle in, Exit out -- still pending its own
//   runtime confirmation.
// - Reset: it is a **second exec input pin** on the macro, so the A side needs a second exec
//   chain in the same graph -- DoOnce / DoN sit in the event graph with two entries:
//   ReceiveBeginPlay -> execute, a custom event -> Reset. The DSL mirrors that shape: the
//   [FlowControl]-marked method *is* the macro, so triggering it is a call -- BeginPlay calls
//   Fire() (Execute) and a second event calls Reset(), the entry named by ResetName
//   (requirements\DSL\Blueprint\DSL-syntax.md §4.11.1). DoN's N has no documented DSL form yet -- §4.11.1
//   defines ResetName only -- so the A side carries it as the pin default N = 3.
// - DSL side: the blueprint translator lowers all of them (LowerFlowControlInvocation) --
//   bare-name calls Sequence / FlipFlop / MultiGate (one lambda per output pin) and the
//   [FlowControl] attribute form for DoOnce / DoN / Gate, where the marked method *is* the
//   macro (see requirements\DSL\Blueprint\DSL-syntax.md §4.11.1). A [FlowControl] method's body chains from
//   the macro's completion pin, so chaining two of them is how DSL expresses "DoOnce then
//   Gate". DoN's N and DoOnce's StartClosed come from named attribute args.
//
// Note: mis-touch comparison (VerifyIrSnapshot) inserted per user instruction,
// after visual confirmation of all subtasks and test results.
// =========================================================================

public sealed class Blueprint_FlowControl : ITestCase
{
    public string Name => "Blueprint Flow Control (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 33;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Sequence --
        // MCP steps: Flow_Seq_A (Extends Actor), Event BeginPlay -> Sequence (2 outputs)
        //   then_0 -> PrintString("A"), then_1 -> PrintString("B")
        const string a1 = "/Game/Tests/UE5_0/Flow_Seq_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var ev1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var seq1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "Sequence", 300, 0);
        var printA1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 600, -100,
            "KismetSystemLibrary");
        var printB1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 600, 100,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, printA1.nodeId, "InString", "A");
        BlueprintTestHelper.SetPinDefault(ctx, a1, printB1.nodeId, "InString", "B");
        BlueprintTestHelper.Connect(ctx, a1, ev1.nodeId, "then", seq1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, seq1.nodeId, "then_0", printA1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, seq1.nodeId, "then_1", printB1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path: one call, one lambda per output
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Flow_Seq_B");

        // -- Subtask 2: FlipFlop (StandardMacros, A / B alternate) --
        // MCP steps: Flow_Flip_A (Extends Actor), function Trigger() -> FlipFlop
        //   A -> PrintString("On"), B -> PrintString("Off")
        const string a2 = "/Game/Tests/UE5_0/Flow_Flip_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Trigger");
        var flip2 = BlueprintTestHelper.AddNode(ctx, a2, "Trigger", "FlipFlop", 300, 0,
            kind: "macro");
        var on2 = BlueprintTestHelper.AddNode(ctx, a2, "Trigger", "PrintString", 600, -100,
            "KismetSystemLibrary");
        var off2 = BlueprintTestHelper.AddNode(ctx, a2, "Trigger", "PrintString", 600, 100,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, on2.nodeId, "InString", "On");
        BlueprintTestHelper.SetPinDefault(ctx, a2, off2.nodeId, "InString", "Off");
        // FlipFlop macro entry exec pin is NAME_None (StandardMacros convention:
        // K2Node_MacroInstance::AllocateDefaultPins copies Tunnel->PinName, which
        // is unnamed for FlipFlop/DoOnce/DoN entry tunnels).
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", flip2.nodeId, "None");
        BlueprintTestHelper.Connect(ctx, a2, flip2.nodeId, "A", on2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, flip2.nodeId, "B", off2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, on2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, off2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path: one lambda per output, same shape as Sequence
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Flow_Flip_B");

        // -- Subtask 3: DoOnce (StandardMacros, fires once until Reset) --
        // MCP steps: Flow_Once_A (Extends Actor), event graph with two entries:
        //   ReceiveBeginPlay -> DoOnce.execute, CustomEvent ResetFire -> DoOnce.Reset
        //   Completed -> PrintString("Once")
        const string a3 = "/Game/Tests/UE5_0/Flow_Once_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var rst3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ResetFire", 0, 200,
            kind: "customevent");
        var once3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "DoOnce", 300, 0,
            kind: "macro");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, p3.nodeId, "InString", "Once");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", once3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, rst3.nodeId, "then", once3.nodeId, "Reset");
        BlueprintTestHelper.Connect(ctx, a3, once3.nodeId, "Completed", p3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path: the [FlowControl]-marked method *is* the macro, so triggering it is a call --
        // BeginPlay calls Fire() (Execute), a second event calls Reset(), the entry named by
        // ResetName (requirements\DSL\Blueprint\DSL-syntax.md §4.11.1). Mirrors the two-entry A side.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Flow_Once_B");

        // -- Subtask 4: DoN (StandardMacros, fires N times, resettable) --
        // MCP steps: Flow_DoN_A (Extends Actor), event graph with two entries:
        //   ReceiveBeginPlay -> DoN.execute (N = 3), CustomEvent ResetFire -> DoN.Reset
        //   Exit -> PrintString("x")
        const string a4 = "/Game/Tests/UE5_0/Flow_DoN_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a4);
        var ev4 = BlueprintTestHelper.AddNode(ctx, a4, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var rst4 = BlueprintTestHelper.AddNode(ctx, a4, "EventGraph", "ResetFire", 0, 200,
            kind: "customevent");
        // StandardMacros names this macro "Do N" (with space); the bridge matches
        // Candidate->GetName() == MemberName exactly.
        var don4 = BlueprintTestHelper.AddNode(ctx, a4, "EventGraph", "Do N", 300, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, a4, don4.nodeId, "n", "3");
        var p4 = BlueprintTestHelper.AddNode(ctx, a4, "EventGraph", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a4, p4.nodeId, "InString", "x");
        BlueprintTestHelper.Connect(ctx, a4, ev4.nodeId, "then", don4.nodeId, "Enter");
        BlueprintTestHelper.Connect(ctx, a4, rst4.nodeId, "then", don4.nodeId, "Reset");
        BlueprintTestHelper.Connect(ctx, a4, don4.nodeId, "Exit", p4.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a4);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a4);

        // DSL path: same two-entry shape as DoOnce. N has no documented DSL form yet
        // (§4.11.1 defines ResetName only) -- the A side carries it as the pin default N = 3.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Flow_DoN_B");

        // -- Subtask 5: DoOnce + Gate (StandardMacros, once then through the gate) --
        // MCP steps: Flow_Gate_A (Extends Actor), event graph with two entries:
        //   ReceiveBeginPlay -> DoOnce.execute, CustomEvent ResetFire -> DoOnce.Reset
        //   DoOnce.Completed -> Gate.Enter, Gate.Exit -> PrintString("Shot")
        const string a5 = "/Game/Tests/UE5_0/Flow_Gate_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a5);
        var ev5 = BlueprintTestHelper.AddNode(ctx, a5, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var rst5 = BlueprintTestHelper.AddNode(ctx, a5, "EventGraph", "ResetFire", 0, 200,
            kind: "customevent");
        var once5 = BlueprintTestHelper.AddNode(ctx, a5, "EventGraph", "DoOnce", 300, 0,
            kind: "macro");
        var gate5 = BlueprintTestHelper.AddNode(ctx, a5, "EventGraph", "Gate", 500, 0,
            kind: "macro");
        var p5 = BlueprintTestHelper.AddNode(ctx, a5, "EventGraph", "PrintString", 750, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a5, p5.nodeId, "InString", "Shot");
        BlueprintTestHelper.Connect(ctx, a5, ev5.nodeId, "then", once5.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a5, rst5.nodeId, "then", once5.nodeId, "Reset");
        BlueprintTestHelper.Connect(ctx, a5, once5.nodeId, "Completed", gate5.nodeId, "Enter");
        BlueprintTestHelper.Connect(ctx, a5, gate5.nodeId, "Exit", p5.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a5);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a5);

        // DSL path: two macros chained -- the [FlowControl]-marked method *is* the macro, so
        // DoOnce's body calling Shoot() is exactly "DoOnce.Completed -> Gate.Enter", and
        // Shoot()'s body chains from Gate.Exit. Same two-entry reset shape as DoOnce above.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Flow_Gate_B");

        await Task.CompletedTask;
    }
}
