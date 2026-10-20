// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 43 - Blueprint Input Events (UE 5.0) - from Tasks/UE_5.0/blueprint-input.md
//
// Probe (collaboration spec point 6):
// - Bridge add_blueprint_node SUPPORTS kind "inputaction" (UK2Node_InputAction,
//   event graph only, native "Pressed"/"Released"/"Key" pins, localized title)
//   and "inputaxis" (K2Node_InputAxisEvent) - ForgeMindBridgeClient_BlueprintGraph.cpp.
//   Task 1 / task 2 A-side use these real kinds.
// - Bridge add_blueprint_node supports kind "inputkey" (UK2Node_InputKey with
//   "Pressed"/"Released" exec out pins + a "Key" data out pin) - the pinned gap
//   was implemented 2026-10-13 (spec point 6 closed).
// - DSL translator supports [InputAction] / [InputAxis] / [InputKey] on create
//   mode (emits the matching event node + body exec flow); update mode
//   downgrades to function patch.
// - Action / axis names must be registered in Input Settings before node creation
//   (bridge validates registration, added 2026-10-13). Test calls
//   AddActionMapping / AddAxisMapping before the A-side node creation.
//
// Note: mis-touch comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9).
// =========================================================================

public sealed class Blueprint_Input : ITestCase
{
    public string Name => "Blueprint Input Events (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 43;

    public async Task Run(TestContext ctx)
    {
        // -- Task 1: Input Action event --
        // MCP steps: Input_Action_A (Extends Actor, enable Input) + Input Action event "Jump"
        //   -> body PrintString("Jump"). Bridge kind "inputaction" is supported.
        // Register "Jump" -> SpaceBar in Input Settings so the bridge validation
        // (added 2026-10-13) accepts the InputAction node creation.
        BlueprintTestHelper.AddActionMapping(ctx, "Jump", "SpaceBar");
        const string a1 = "/Game/Tests/UE5_0/Input_Action_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var act1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "Jump", 0, 0,
            kind: "inputaction");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "Jump");
        BlueprintTestHelper.Connect(ctx, a1, act1.nodeId, "Pressed", p1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc, Mode = Create added)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Input_Action_B");

        // -- Task 2: Input Axis event --
        // MCP steps: Input_Axis_A + Input Axis event "Move" (exposes AxisValue)
        //   -> body PrintString(AxisValue). Bridge kind "inputaxis" is supported.
        // Register "Move" -> D (scale 1.0) in Input Settings so the bridge
        // validation accepts the InputAxis node creation.
        BlueprintTestHelper.AddAxisMapping(ctx, "Move", "D", 1.0);
        const string a2 = "/Game/Tests/UE5_0/Input_Axis_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var ax2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "Move", 0, 0,
            kind: "inputaxis");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, ax2.nodeId, "AxisValue", p2.nodeId, "InString");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc, Mode = Create added)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Input_Axis_B");

        // -- Task 3: Input Key event --
        // MCP steps: Input_Key_A + Input Key event "E" -> body PrintString("E").
        // kind "inputkey" supported (2026-10-13; gap from spec point 6 closed).
        // K2Node_InputKey has "Pressed"/"Released" exec out pins (no "then"),
        // so the exec line wires "Pressed" (key-down fires the print).
        const string a3 = "/Game/Tests/UE5_0/Input_Key_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var key3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "E", 0, 0,
            kind: "inputkey");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, p3.nodeId, "InString", "E");
        BlueprintTestHelper.Connect(ctx, a3, key3.nodeId, "Pressed", p3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc, Mode = Create added; [InputKey] translator
        // support added 2026-10-13)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Input_Key_B");

        await Task.CompletedTask;
    }
}
