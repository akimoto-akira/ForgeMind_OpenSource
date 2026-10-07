// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 7 · Blueprint Events (UE 5.0)  — from Tasks/UE_5.0/Blueprint-Events.md
// =========================================================================

public sealed class Blueprint_Event : ITestCase
{
    public string Name => "Blueprint Events (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 7;

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: Event BeginPlay ──
        // Engine real name: BeginPlay's UFunction is ReceiveBeginPlay (AActor virtual function;
        // BeginPlay itself is not a UFunction); the event's exec-out is "then".
        const string a1 = "/Game/Tests/UE5_0/Event_Begin_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var ev1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "Begin");
        BlueprintTestHelper.Connect(ctx, a1, ev1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Event_Begin_B");

        // ── Sub-task 2: Event Tick ──
        const string a2 = "/Game/Tests/UE5_0/Event_Tick_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var ev2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveTick", 0, 0);
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "DeltaSeconds", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Event_Tick_B");

        // ── Sub-task 3: Custom Event ──
        // Custom events are created via kind:"customevent"; the event name uses the 'name' field
        // (the CustomEvent node has no "EventName" input pin to set a default value).
        const string a3 = "/Game/Tests/UE5_0/Event_Custom_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "OnTrigger", 0, 0, kind: "customevent");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, p3.nodeId, "InString", "Triggered");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Event_Custom_B");

        await Task.CompletedTask;
    }
}
