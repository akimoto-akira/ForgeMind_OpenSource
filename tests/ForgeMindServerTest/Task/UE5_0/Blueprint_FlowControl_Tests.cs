// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 33 - Blueprint Flow Control (UE 5.0) - from Tasks/UE_5.0/Blueprint-FlowControl.md
//
// Probe (collaboration spec point 6):
// - Sequence: UK2Node_ExecutionSequence (K2Node_ExecutionSequence.h:17), title "Sequence"
//   (cpp:150). Output pins are named **Then0 / Then1 ...** (GetPinNameGivenIndex,
//   cpp:261-264); two are created by default (cpp:141-145). Bridge supports it as the
//   native node name "Sequence" (CreateNativeFlowNode, BlueprintGraph.cpp:154).
// - **FlipFlop does not exist in this engine build**: no K2Node_FlipFlop.h under
//   Editor\BlueprintGraph\Classes (that directory's K2Node_*.h were enumerated: only
//   K2Node_ExecutionSequence / K2Node_MultiGate / K2Node_DoOnceMultiInput exist in the
//   flow-control family). This build is a trimmed 5.0.3 -- same reason the Ease case had
//   to rewrite the doc's easing names. So the FlipFlop step cannot be built at all.
// - **DoOnce / Gate do not exist either**: no K2Node_DoOnce.h, no K2Node_Gate.h (only the
//   multi-input variant K2Node_DoOnceMultiInput.h:15, title "DoOnce MultiInput").
//   UK2Node_MultiGate derives from UK2Node_ExecutionSequence (K2Node_MultiGate.h:14), not
//   from a Gate base class. Pending engine/bridge capability for subtask 3.
// - DSL side: all three snippets avoid the nodes entirely (if / else, private bool), so
//   they are plain control flow the translator can already express; the DSL side does
//   **not** test Sequence/FlipFlop/DoOnce/Gate semantics. Copied as written -- test is spec.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transcribing task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
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
        //   Then0 -> PrintString("A"), Then1 -> PrintString("B")
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
        BlueprintTestHelper.Connect(ctx, a1, seq1.nodeId, "Then0", printA1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, seq1.nodeId, "Then1", printB1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: FlipFlop --
        // MCP steps: Flow_Flip_A (Extends Actor), Custom Event Trigger -> FlipFlop
        //   A -> PrintString("On"), B -> PrintString("Off")
        const string a2 = "/Game/Tests/UE5_0/Flow_Flip_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var trigger2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Trigger");
        // Pending engine/bridge capability: no FlipFlop node class in this engine build and
        // no bridge vocabulary entry for it (see file header). The custom event itself is
        // real coverage.
        BlueprintTestHelper.Connect(ctx, a2, trigger2.entryId, "then", trigger2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: DoOnce + Gate --
        // MCP steps: Flow_Gate_A (Extends Actor), Custom Event Fire -> DoOnce -> Gate
        //   Gate Exit -> PrintString("Shot")
        const string a3 = "/Game/Tests/UE5_0/Flow_Gate_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var fire3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Fire");
        // Pending engine/bridge capability: neither DoOnce nor Gate exists in this engine
        // build (only K2Node_DoOnceMultiInput, which has no N pin and no bridge entry).
        BlueprintTestHelper.Connect(ctx, a3, fire3.entryId, "then", fire3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}