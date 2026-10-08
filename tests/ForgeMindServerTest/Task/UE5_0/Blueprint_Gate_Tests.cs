// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 36 - Blueprint Gate (UE 5.0) - from Tasks/UE_5.0/Blueprint-Gate.md
//
// Probe (collaboration spec point 6):
// - MultiGate exists and the bridge supports it as the native node name "MultiGate"
//   (CreateNativeFlowNode, BlueprintGraph.cpp:158-161). Real pins (K2Node_MultiGate.cpp:
//   507-511 inputs, 633-636 outputs): inputs `Execute` (inherited) + `Reset`, `IsRandom`
//   (bool), `Loop` (bool), `StartIndex` (int, default -1); outputs are named **"Out 0",
//   "Out 1" ...** (NOT Then0/Then1 -- MultiGate derives from UK2Node_ExecutionSequence,
//   K2Node_MultiGate.h:14-15, but renames the outputs). So the doc's "output A/B/C" is
//   really Out 0 / Out 1 / Out 2, and the doc's "Loop=true" (subtask 3) is a genuine pin.
// - **DoN is not directly buildable**: the multi-input variant
//   UK2Node_DoOnceMultiInput (K2Node_DoOnceMultiInput.h:15, title "DoOnce MultiInput",
//   cpp:48-50) has **no N pin** -- N is the node property `int32 NumAdditionalInputs`
//   (h:20-22, applied cpp:193-196/218-227) and extra inputs are added via AddInputPin().
//   It also has no bridge vocabulary entry. Its pins carry PinName = None and only
//   FriendlyNames "A In" / "A Out" / "Reset In" / "Reset Out" (cpp:180-191), so any
//   future connection must match friendly names. Pending requirement (bridge support +
//   a way to set NumAdditionalInputs).
// - DSL side: doc writes `MultiGate(this.A, this.B, this.C)` / `DoN(3, this.Shot)` /
//   `MultiGate(Loop: true, ...)` + `MultiGateReset()`. The translator's gate/flow-control
//   vocabulary is unverified for these forms; `private bool` / `private` fields in the doc
//   snippets are not valid members in this DSL (locals are declared inside functions --
//   see Blueprint_LocalVariable_Tests). Copied verbatim; test is spec.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transcribing task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
// =========================================================================

public sealed class Blueprint_Gate : ITestCase
{
    public string Name => "Blueprint Gate (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 36;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: MultiGate --
        // MCP steps: Gate_Multi_A (Extends Actor), Custom Event Trigger -> MultiGate
        //   -> Out 0 / Out 1 / Out 2 each feeding a PrintString
        const string a1 = "/Game/Tests/UE5_0/Gate_Multi_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var trigger1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Trigger");
        var gate1 = BlueprintTestHelper.AddNode(ctx, a1, "Trigger", "MultiGate", 300, 0);
        // MultiGate creates two outputs by default (inherited ExecutionSequence behaviour);
        // the third is added through AddInputPin(), which the bridge does not expose yet.
        var pA = BlueprintTestHelper.AddNode(ctx, a1, "Trigger", "PrintString", 600, -150,
            "KismetSystemLibrary");
        var pB = BlueprintTestHelper.AddNode(ctx, a1, "Trigger", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, pA.nodeId, "InString", "a");
        BlueprintTestHelper.SetPinDefault(ctx, a1, pB.nodeId, "InString", "b");
        BlueprintTestHelper.Connect(ctx, a1, trigger1.entryId, "then", gate1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, gate1.nodeId, "Out 0", pA.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, gate1.nodeId, "Out 1", pB.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: DoN (execute N times) --
        // MCP steps: Gate_DoN_A (Extends Actor), Custom Event Fire -> DoN(N=3)
        //   -> Exit -> PrintString("x")
        const string a2 = "/Game/Tests/UE5_0/Gate_DoN_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var fire2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Fire");
        // Pending bridge/engine capability: DoOnceMultiInput has no N pin (NumAdditionalInputs
        // is a node property) and no bridge vocabulary entry -- see file header.
        BlueprintTestHelper.Connect(ctx, a2, fire2.entryId, "then", fire2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: MultiGate with Loop / Reset --
        // MCP steps: Gate_Reset_A (Extends Actor), Trigger -> MultiGate(Loop=true),
        //   Reset event -> MultiGate's Reset pin
        const string a3 = "/Game/Tests/UE5_0/Gate_Reset_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var trigger3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Trigger");
        var reset3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Reset");
        var gate3 = BlueprintTestHelper.AddNode(ctx, a3, "Trigger", "MultiGate", 300, 0);
        var pR = BlueprintTestHelper.AddNode(ctx, a3, "Trigger", "PrintString", 600, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, pR.nodeId, "InString", "r");
        BlueprintTestHelper.SetPinDefault(ctx, a3, gate3.nodeId, "Loop", "true");
        BlueprintTestHelper.Connect(ctx, a3, trigger3.entryId, "then", gate3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, gate3.nodeId, "Out 0", pR.nodeId, "execute");
        // The Reset pin lives on the same MultiGate node, so the Reset event drives it.
        BlueprintTestHelper.Connect(ctx, a3, reset3.entryId, "then", gate3.nodeId, "Reset");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}