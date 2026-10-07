// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 16 - Blueprint Timer (UE 5.0) - from Tasks/UE_5.0/blueprint-timer.md
// Engine function location (KismetSystemLibrary.h):
//   "Set Timer by Event"    -> K2_SetTimerDelegate(Delegate, Time, bLooping, ...)
//   "Set Timer by Function Name" -> K2_SetTimer(Object, FunctionName, Time, bLooping, ...)
//   "Clear and Invalidate Timer by Handle" -> K2_ClearAndInvalidateTimerHandle(WorldContextObject, Handle(ref))
// Bridge add_blueprint_node finds UFunction's FName via FindFunctionByName (i.e.,
// C++ function names K2_SetTimerDelegate / K2_SetTimer / K2_ClearTimerHandle),
// not blueprint editor display names. Function library members must pass class:"KismetSystemLibrary" to locate.
// Pin names take UFunction param real names (Delegate/Time/bLooping/FunctionName/Handle),
// not DisplayName (e.g., "Event" is Delegate's DisplayName).
// Note: this file is task doc transwriting; VerifyIrSnapshot (diff comparison) inserted after user visual confirmation.
// =========================================================================

public sealed class Blueprint_Timer : ITestCase
{
    public string Name => "Blueprint Timer (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 16;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Set Timer by Event --
        // MCP steps: Timer_Event_A (Extends Actor) + function Start ->
        //   Set Timer by Event(Time=1.0, Looping=true) -> Delegate pin via
        //   CreateDelegate node binds custom event OnTick (event graph) -> OnTick body PrintString("t")
        // Note: custom events can only be placed in event graph (EventGraph), not in function graph.
        // CreateDelegate in function graph wraps event name into delegate value, connects to SetTimerDelegate.Delegate.
        const string a1 = "/Game/Tests/UE5_0/Timer_Event_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Start");
        // K2_SetTimerDelegate = "Set Timer by Event" (KismetSystemLibrary).
        // Pin real names: Delegate(DisplayName="Event"), Time, bLooping.
        var setT1 = BlueprintTestHelper.AddNode(ctx, a1, "Start", "K2_SetTimerDelegate", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, setT1.nodeId, "Time", "1.0");
        BlueprintTestHelper.SetPinDefault(ctx, a1, setT1.nodeId, "bLooping", "true");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", setT1.nodeId, "execute");
        // CreateDelegate node: wraps custom event OnTick into delegate value.
        // ReturnValue -> SetTimerDelegate.Delegate (same form as AddDelegate/RemoveDelegate).
        var cd1 = BlueprintTestHelper.AddNode(ctx, a1, "Start", "CreateDelegate", 150, 120, function: "OnTick");
        BlueprintTestHelper.Connect(ctx, a1, cd1.nodeId, "ReturnValue", setT1.nodeId, "Delegate");
        // Custom event OnTick placed in event graph (EventGraph) -- function graph does not allow creating events.
        var onTick1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "OnTick", 600, 0, kind: "customevent");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "t");
        BlueprintTestHelper.Connect(ctx, a1, onTick1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, setT1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (from task doc; SetTimer form pending finalization after real BlueprintDSL validation).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Timer_Event_B");

        // -- Subtask 2: Set Timer by Function Name --
        // MCP steps: Timer_Func_A (Extends Actor) + function Step(PrintString("s"))
        //   + function Start -> Set Timer by Function Name(FunctionName="Step", Time=2.0)
        const string a2 = "/Game/Tests/UE5_0/Timer_Func_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var stepA2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Step");
        var pStep2 = BlueprintTestHelper.AddNode(ctx, a2, "Step", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, pStep2.nodeId, "InString", "s");
        BlueprintTestHelper.Connect(ctx, a2, stepA2.entryId, "then", pStep2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, pStep2.nodeId, "then", stepA2.resultId, "execute");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Start");
        // K2_SetTimer = "Set Timer by Function Name" (KismetSystemLibrary).
        // Pin real names: Object(DefaultToSelf, auto-connect self), FunctionName, Time, bLooping.
        var setFn2 = BlueprintTestHelper.AddNode(ctx, a2, "Start", "K2_SetTimer", 300, 120, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, setFn2.nodeId, "FunctionName", "Step");
        BlueprintTestHelper.SetPinDefault(ctx, a2, setFn2.nodeId, "Time", "2.0");
        BlueprintTestHelper.SetPinDefault(ctx, a2, setFn2.nodeId, "bLooping", "true");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", setFn2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, setFn2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Timer_Func_B");

        // -- Subtask 3: Clear Timer --
        // MCP steps: Reuse Timer_Event_A structure (Start starts Timer + OnTick) + function
        //   Stop -> Clear Timer(Handle = saved timer handle)
        // OnTick custom event placed in event graph (same as subtask 1), Delegate via CreateDelegate relay.
        const string a3 = "/Game/Tests/UE5_0/Timer_Clear_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Start");
        var setT3 = BlueprintTestHelper.AddNode(ctx, a3, "Start", "K2_SetTimerDelegate", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, setT3.nodeId, "Time", "1.0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, setT3.nodeId, "bLooping", "true");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", setT3.nodeId, "execute");
        var cd3 = BlueprintTestHelper.AddNode(ctx, a3, "Start", "CreateDelegate", 150, 120, function: "OnTick");
        BlueprintTestHelper.Connect(ctx, a3, cd3.nodeId, "ReturnValue", setT3.nodeId, "Delegate");
        // SetTimerDelegate returns FTimerHandle -> assign to TimerHandle variable.
        // VariableSet node: setT3.then -> set.execute, setT3.ReturnValue -> set.TimerHandle (value input pin name = variable name).
        BlueprintTestHelper.AddVariable(ctx, a3, "TimerHandle", "TimerHandle");
        var setH3 = BlueprintTestHelper.AddNode(ctx, a3, "Start", "TimerHandle", 500, 0, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a3, setT3.nodeId, "then", setH3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, setT3.nodeId, "ReturnValue", setH3.nodeId, "TimerHandle");
        BlueprintTestHelper.Connect(ctx, a3, setH3.nodeId, "then", f3.resultId, "execute");
        var onTick3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "OnTick", 600, 0, kind: "customevent");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, p3.nodeId, "InString", "t");
        BlueprintTestHelper.Connect(ctx, a3, onTick3.nodeId, "then", p3.nodeId, "execute");
        // K2_ClearAndInvalidateTimerHandle = "Clear and Invalidate Timer by Handle" (KismetSystemLibrary).
        // K2_ClearTimerHandle is deprecated; engine recommends this function. Pin real names:
        // WorldContextObject(WorldContext), Handle(ref).
        // Handle is UPARAM(ref) reference pin -- does not accept literal defaults, but accepts variable getter wiring.
        var stop3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Stop");
        var clear3 = BlueprintTestHelper.AddNode(ctx, a3, "Stop", "K2_ClearAndInvalidateTimerHandle", 300, 0, "KismetSystemLibrary");
        var getH3 = BlueprintTestHelper.AddNode(ctx, a3, "Stop", "TimerHandle", 100, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a3, getH3.nodeId, "TimerHandle", clear3.nodeId, "Handle");
        BlueprintTestHelper.Connect(ctx, a3, stop3.entryId, "then", clear3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, clear3.nodeId, "then", stop3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Timer_Clear_B");

        await Task.CompletedTask;
    }
}
