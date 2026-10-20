// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 54 - Blueprint Delay (UE 5.0) - from Tasks/UE_5.0/blueprint-delay.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit; the
// Set Timer task uses the verified method-group form SetTimer(this.OnTick, 1.0f, true)
// (DSL-syntax.md §4.20) instead of the doc's SetTimer(this, "OnTick", 1, true) string
// form. Delay/RetriggerableDelay are Latent nodes (UK2Node_Delay). A latent call
// node carries BOTH a 'then' exec output (continues IMMEDIATELY) and the latent
// 'Completed' exec output (fires when the wait finishes) -- so an ordinary
// statement placed after `Delay(1);` lands on 'then' and runs before the delay
// elapses. That is not what the task asks for and it diverged from the A-side,
// which wires Delay.Completed -> PrintString.execute. Per DSL-syntax.md §4.11.3 the
// continuation MUST be passed as the trailing delegate argument (it binds
// Completed), and nothing may follow the delay call:
//   Delay(1, () => { PrintString("Done"); });
// SetTimer is not latent (plain BlueprintCallable, handler passed by method
// group), so task 3 keeps its statement-after-call form.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Delay (BeginPlay -> Delay(1) -> PrintString "Done")
//   Task 2 - Retriggerable Delay (BeginPlay -> RetriggerableDelay(1) -> PrintString "Go")
//   Task 3 - Set Timer by Event (BeginPlay -> SetTimer(OnTick, 1, loop))
//
// Node names are the C++ UFunction FNames, never the editor DisplayName: the bridge
// resolves add_blueprint_node through FindFunctionByName (verified in #16 Timer -
// "Set Timer by Event" is K2_SetTimerDelegate, "Set Timer by Function Name" is
// K2_SetTimer). Task 3 therefore builds K2_SetTimerDelegate and feeds its Delegate
// pin from a CreateDelegate node bound to the OnTick custom event, matching the
// method-group DSL form SetTimer(this.OnTick, 1.0f, true).
//
// Note: mis-touch comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9), covering all six assets - the 3 A-side (MCP)
// and the 3 DSL-side blueprints. Each stores its own graph-level IR baseline
// on first run (no failure); later runs diff against it to catch accidental
// damage to already-tested structures (per-asset self-comparison, not A/B).
// =========================================================================

public sealed class Blueprint_Delay : ITestCase
{
    public string Name => "Blueprint Delay (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 54;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    private static void BuildTask1(TestContext ctx)
    {
        {
            const string a1 = "/Game/Tests/UE5_0/Delay_Basic_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            var ev = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 0, 0);
            var delay = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "Delay", 300, 0,
                "KismetSystemLibrary");
            var print = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 600, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.SetPinDefault(ctx, a1, delay.nodeId, "Duration", "1.0");
            BlueprintTestHelper.Connect(ctx, a1, ev.nodeId, "then", delay.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, delay.nodeId, "Completed", print.nodeId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Delay_Basic_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Delay_Retrig_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            var ev = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveBeginPlay", 0, 0);
            var delay = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "RetriggerableDelay", 300, 0,
                "KismetSystemLibrary");
            var print = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 600, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.SetPinDefault(ctx, a2, delay.nodeId, "Duration", "1.0");
            BlueprintTestHelper.Connect(ctx, a2, ev.nodeId, "then", delay.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, delay.nodeId, "Completed", print.nodeId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Delay_Retrig_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Delay_Timer_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            var ev = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveBeginPlay", 0, 0);
            // "Set Timer by Event" is the editor DisplayName; the bridge resolves the
            // UFunction FName through FindFunctionByName, so the node name is the C++
            // name K2_SetTimerDelegate (KismetSystemLibrary.h:566). Passing "SetTimer"
            // found nothing and left an unresolved zero-pin node
            // (run 20261017_2017: "Node is unresolved (zero pins after compile)"),
            // same class of mistake as LineTrace -> LineTraceSingle (#41) and
            // SphereTraceByChannel -> SphereTraceSingle (#57).
            // Pin real names: Delegate (DisplayName="Event"), Time, bLooping.
            var timer = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "K2_SetTimerDelegate", 300, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.SetPinDefault(ctx, a3, timer.nodeId, "Time", "1.0");
            BlueprintTestHelper.SetPinDefault(ctx, a3, timer.nodeId, "bLooping", "true");
            // A custom event has no delegate output pin, so the handler is wrapped into a
            // delegate value by a CreateDelegate node (same form as Timer #16 subtask 1).
            var cd = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "CreateDelegate", 150, 200,
                function: "OnTick");
            BlueprintTestHelper.Connect(ctx, a3, cd.nodeId, "ReturnValue", timer.nodeId, "Delegate");
            BlueprintTestHelper.Connect(ctx, a3, ev.nodeId, "then", timer.nodeId, "execute");
            // Task doc step 3: custom event OnTick whose body prints "T".
            var onTick = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "OnTick", 600, 200,
                kind: "customevent");
            var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 800, 200,
                "KismetSystemLibrary");
            BlueprintTestHelper.SetPinDefault(ctx, a3, p3.nodeId, "InString", "T");
            BlueprintTestHelper.Connect(ctx, a3, onTick.nodeId, "then", p3.nodeId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        // Method-group form per DSL-syntax.md §4.20 (verified in Blueprint_Timer_Tests).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Delay_Timer_B");
    }
}