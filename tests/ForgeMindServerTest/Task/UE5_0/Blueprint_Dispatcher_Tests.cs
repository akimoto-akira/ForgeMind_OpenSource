// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 8 · Blueprint Event Dispatcher (UE 5.0)  — from Tasks/UE_5.0/Blueprint-Dispatcher.md
// =========================================================================

public sealed class Blueprint_Dispatcher : ITestCase
{
    public string Name => "Blueprint Event Dispatcher (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 8;

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: Define and Broadcast ──
        // MCP step: Create blueprint → Add Event Dispatcher OnDamage(int Amount)
        //         → ReceiveBeginPlay → CallDelegate OnDamage → argument Amount = 10
        const string a1 = "/Game/Tests/UE5_0/Disp_Call_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        // Add Event Dispatcher OnDamage(int Amount)
        BlueprintTestHelper.AddEventDispatcher(ctx, a1, "OnDamage",
            new[] { new { name = "Amount", type = "int" } });
        // Compile so the dispatcher property takes effect
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        // ReceiveBeginPlay → Broadcast OnDamage(10)
        var ev1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var bc1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "OnDamage", 300, 0, kind: "callDelegate");
        BlueprintTestHelper.SetPinDefault(ctx, a1, bc1.nodeId, "Amount", "10");
        BlueprintTestHelper.Connect(ctx, a1, ev1.nodeId, "then", bc1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Disp_Call_B");

        // ── Sub-task 2: Bind to handler function ──
        // MCP step: Create blueprint → Add Dispatcher OnDamage(int Amount)
        //         → ReceiveBeginPlay → AddDelegate OnDamage → CustomEvent Handle → PrintString
        const string a2 = "/Game/Tests/UE5_0/Disp_Bind_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddEventDispatcher(ctx, a2, "OnDamage",
            new[] { new { name = "Amount", type = "int" } });
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        // ReceiveBeginPlay → Bind OnDamage → Handle
        var ev2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var bind2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "OnDamage", 300, 0, kind: "addDelegate");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "then", bind2.nodeId, "execute");
        // Custom Event Handle(int Amount) → PrintString(Amount)
        var handle2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "Handle", 600, 0,
            kind: "customevent", eventParams: new[] { new { name = "Amount", type = "int" } });
        // Binding: CustomEvent has no delegate output pin; CreateDelegate relay is needed to connect
        // to AddDelegate's "Delegate" pin (engine real name, display name "Event").
        var create2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "CreateDelegate", 450, 120,
            function: "Handle");
        BlueprintTestHelper.Connect(ctx, a2, create2.nodeId, "ReturnValue", bind2.nodeId, "Delegate");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, handle2.nodeId, "Amount", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, handle2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Disp_Bind_B");

        // ── Sub-task 3: Unbind ──
        // MCP step: Create blueprint → Add Dispatcher OnDamage → Function Stop → RemoveDelegate
        // Note: In UE, RemoveDelegate's Delegate input needs to connect an event (delegate value),
        // not pass the object directly (this); it is symmetric with AddDelegate. Therefore, a
        // CreateDelegate relay node is needed to wrap the event as a delegate value and connect it
        // to RemoveDelegate.Delegate.
        const string a3 = "/Game/Tests/UE5_0/Disp_Unbind_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddEventDispatcher(ctx, a3, "OnDamage",
            new[] { new { name = "Amount", type = "int" } });
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // Stop function → RemoveDelegate OnDamage
        // Handle custom event, providing the target for CreateDelegate (signature matches OnDamage)
        var unbindA_handle = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "Handle", 600, 0,
            kind: "customevent", eventParams: new[] { new { name = "Amount", type = "int" } });
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Stop");
        var unbind3 = BlueprintTestHelper.AddNode(ctx, a3, "Stop", "OnDamage", 300, 0, kind: "removeDelegate");
        var create3 = BlueprintTestHelper.AddNode(ctx, a3, "Stop", "CreateDelegate", 150, 120,
            function: "Handle");
        BlueprintTestHelper.Connect(ctx, a3, create3.nodeId, "ReturnValue", unbind3.nodeId, "Delegate");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", unbind3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, unbind3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Disp_Unbind_B");

        await Task.CompletedTask;
    }
}
