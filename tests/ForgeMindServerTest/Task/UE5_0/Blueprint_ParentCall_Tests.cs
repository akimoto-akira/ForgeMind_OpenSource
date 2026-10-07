// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 15 · Blueprint Call Parent Function (UE 5.0)  — from Tasks/UE_5.0/Blueprint-CallParent.md
// =========================================================================

public sealed class Blueprint_ParentCall : ITestCase
{
    public string Name => "Blueprint Call Parent Function (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 15;

    public async Task Run(TestContext ctx)
    {
        // ── Support matrix probe conclusion (Collaboration Spec Point 6) ──
        // Engine semantic verification (UE 5.0.3 source UK2Node_CallParentFunction):
        // - "Parent: FunctionName" node = UK2Node_CallParentFunction; in child blueprint overridden
        //   event/function body, executes parent implementation first then continues child logic.
        //   Bridge add_blueprint_node's callParentFunction kind not implemented — tests as specs;
        //   written per existing kind naming convention (name = member name, kind selects node family,
        //   same as callDelegate/addDelegate convention).
        // - DSL `base.X()` (BaseAccessExpressionSyntax) translation not implemented — tests as specs.
        // - Parent class asset BP_Base (Extends Actor) after creation and compilation in the same session,
        //   its generated class BP_Base_C is already in memory — MCP child class creation passes
        //   parentClass as "BP_Base_C"; DSL [Extends(typeof(BP_Base))] bare class name resolution
        //   (create_blueprint's class name resolution chain has no "_C" fallback) is a gap to be
        //   completed, driven by this test.

        // ── Sub-task 1: Parent's BeginPlay ──
        // Shared parent class BP_Base (docs workspace A step 1, shared between both spaces):
        // Extends Actor, BeginPlay prints "base"
        const string parentBp = "/Game/Tests/UE5_0/BP_Base";
        BlueprintTestHelper.CreateBlueprintActor(ctx, parentBp);
        var bev = BlueprintTestHelper.AddNode(ctx, parentBp, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var bprint = BlueprintTestHelper.AddNode(ctx, parentBp, "EventGraph", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, parentBp, bprint.nodeId, "InString", "base");
        BlueprintTestHelper.Connect(ctx, parentBp, bev.nodeId, "then", bprint.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, parentBp);

        // MCP step: Child class Parent_Begin_A overriding BeginPlay —
        // Event BeginPlay → Parent: ReceiveBeginPlay → PrintString("child")
        const string a1 = "/Game/Tests/UE5_0/Parent_Begin_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a1, "BP_Base_C");
        var ev1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var parent1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 300, 0,
            kind: "callParentFunction");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "child");
        BlueprintTestHelper.Connect(ctx, a1, ev1.nodeId, "then", parent1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, parent1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (from task docs; base.BeginPlay() to be implemented — tests as specs)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Parent_Begin_B");

        // ── Sub-task 2: Parent custom function Greet ──
        // Parent class BP_Base supplements overridable function Greet() (prints "hi")
        var fg = BlueprintTestHelper.CreateFunction(ctx, parentBp, "Greet");
        var gprint = BlueprintTestHelper.AddNode(ctx, parentBp, "Greet", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, parentBp, gprint.nodeId, "InString", "hi");
        BlueprintTestHelper.Connect(ctx, parentBp, fg.entryId, "then", gprint.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, parentBp, gprint.nodeId, "then", fg.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, parentBp);

        // MCP step: Child class Parent_Func_A overriding Greet —
        // Function Greet: Parent: Greet → PrintString("+child")
        const string a2 = "/Game/Tests/UE5_0/Parent_Func_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a2, "BP_Base_C");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Greet");
        var parent2 = BlueprintTestHelper.AddNode(ctx, a2, "Greet", "Greet", 300, 0,
            kind: "callParentFunction");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Greet", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, p2.nodeId, "InString", "+child");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", parent2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, parent2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (from task docs; base.Greet() to be implemented — tests as specs)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Parent_Func_B");

        // ── Sub-task 3: Parent Tick ──
        // MCP step: Child class Parent_Tick_A overriding Tick —
        // Event Tick → Parent: ReceiveTick (transparently passes DeltaSeconds) → PrintString(DeltaSeconds)
        const string a3 = "/Game/Tests/UE5_0/Parent_Tick_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a3, "BP_Base_C");
        var ev3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveTick", 0, 0);
        var parent3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveTick", 300, 0,
            kind: "callParentFunction");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "then", parent3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "DeltaSeconds", parent3.nodeId, "DeltaSeconds");
        BlueprintTestHelper.Connect(ctx, a3, parent3.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, ev3.nodeId, "DeltaSeconds", p3.nodeId, "InString");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (from task docs; base.Tick(DeltaSeconds) to be implemented — tests as specs)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Parent_Tick_B");

        await Task.CompletedTask;
    }
}
