// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 51 - Blueprint Delegate Binding (UE 5.0) - from Tasks/UE_5.0/blueprint-delegate-bind.md
//
// Scope: binding to an ALREADY-EXISTING delegate variable (distinct from
// Blueprint-Event-Dispatcher.md which declares/broadcasts Event Dispatchers).
//
// Design (2026-10-15, resolves the TestRuns/20261015_2013 #51 FM1102 bug): a
// bindable delegate-typed blueprint variable needs a backing signature, and the
// only bridge machinery that produces one is HandleAddEventDispatcher (it makes
// a PC_MCDelegate property + a signature graph). So a delegate variable is
// treated as an event dispatcher end-to-end - both A-side (MCP) and B-side (DSL)
// reuse that pipeline plus the AddDelegate node. No new C++ node types or
// delegate type-string mappings were added.
//
// Blueprint delegates are MULTICAST-ONLY (finalized): UE has no bindable
// singlecast delegate variable in a blueprint, so the DSL does not advertise a
// SingleCast marker - every delegate variable is a dynamic multicast one.
//
// Binding = Bind, and it connects DIRECTLY: a void [Function]/[Event] handler is
// lowered into the EventGraph as a custom event, and a UK2Node_CustomEvent
// (: UK2Node_Event) exposes an OutputDelegate output pin (the "red square" on
// the event node) that wires straight to the Add node's Delegate pin - exactly
// what the engine's own "bind event" action does (EdGraphSchema_K2_Actions.cpp).
// No "Create Event" (CreateDelegate) relay node. Only a value-returning function
// handler (no event pin) falls back to CreateDelegate.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Assign Delegate (OnDone.Bind(this.OnCb))
//   Task 2 - Bind Event to Delegate (OnTickM.Bind(this.OnTick))
//   Task 3 - Create Dynamic Delegate callback (OnEvt.Bind(this.OnEvtFired))
//
// DSL (B-side) syntax: a delegate-typed variable is declared as a [Variable]
// delegate declaration (`[Variable] delegate void OnDone();`), NOT a field
// (`[Variable] Delegate OnDone;`). The delegate signature comes from the
// parameter list. This routes the receiver into the validator's dispatcher set
// (skips member lookup, no FM1102) and lets the translator emit it through the
// event-dispatcher pipeline. Binding uses Bind(handlerRef) and lowers to an
// AddDelegate wired directly to the handler event's OutputDelegate pin.
// =========================================================================

public sealed class Blueprint_DelegateBind : ITestCase
{
    public string Name => "Blueprint Delegate Binding (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 51;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    private static void BuildTask1(TestContext ctx)
    {
        // A-side (MCP): delegate variable = event dispatcher; binder and handler
        // are custom events in the EventGraph (B-side void [Function]/[Event] all
        // land in the EventGraph), so the handler's OutputDelegate pin binds
        // directly to the Add node - no CreateEvent node (mirrors the B-side IR).
        const string a1 = "/Game/Tests/UE5_0/Delegate_Assign_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddEventDispatcher(ctx, a1, "OnDone", System.Array.Empty<object>());
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        var init1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "Init", 0, 0, kind: "customevent");
        var onCb1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "OnCb", 0, 200, kind: "customevent");
        var ps1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 260, 200, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, ps1.nodeId, "InString", "done");
        BlueprintTestHelper.Connect(ctx, a1, onCb1.nodeId, "then", ps1.nodeId, "execute");
        var bind1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "OnDone", 300, 0, kind: "addDelegate");
        BlueprintTestHelper.Connect(ctx, a1, init1.nodeId, "then", bind1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, onCb1.nodeId, "OutputDelegate", bind1.nodeId, "Delegate");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Delegate_Assign_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        const string a2 = "/Game/Tests/UE5_0/Delegate_Bind_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddEventDispatcher(ctx, a2, "OnTickM", System.Array.Empty<object>());
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        // OnTick handler custom event (event -> PrintString("tick"))
        var tick2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "OnTick", 0, 200, kind: "customevent");
        var ps2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 260, 200, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, ps2.nodeId, "InString", "tick");
        BlueprintTestHelper.Connect(ctx, a2, tick2.nodeId, "then", ps2.nodeId, "execute");
        // Sub custom event: then -> Add; OnTick.OutputDelegate -> Add.Delegate (direct)
        var sub2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "Sub", 0, 0, kind: "customevent");
        var bind2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "OnTickM", 300, 0, kind: "addDelegate");
        BlueprintTestHelper.Connect(ctx, a2, sub2.nodeId, "then", bind2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, tick2.nodeId, "OutputDelegate", bind2.nodeId, "Delegate");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Delegate_Bind_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        const string a3 = "/Game/Tests/UE5_0/Delegate_Custom_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddEventDispatcher(ctx, a3, "OnEvt", System.Array.Empty<object>());
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        // OnEvtFired handler custom event (event -> PrintString("fired"))
        var fired3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "OnEvtFired", 0, 200, kind: "customevent");
        var ps3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 260, 200, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, ps3.nodeId, "InString", "fired");
        BlueprintTestHelper.Connect(ctx, a3, fired3.nodeId, "then", ps3.nodeId, "execute");
        // Run custom event: then -> Add; OnEvtFired.OutputDelegate -> Add.Delegate (direct)
        var run3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Run", 0, 0, kind: "customevent");
        var bind3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "OnEvt", 300, 0, kind: "addDelegate");
        BlueprintTestHelper.Connect(ctx, a3, run3.nodeId, "then", bind3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, fired3.nodeId, "OutputDelegate", bind3.nodeId, "Delegate");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Delegate_Custom_B");
    }
}
