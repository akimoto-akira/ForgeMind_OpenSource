// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 27 - Blueprint Inherit (UE 5.0) - from Tasks/UE_5.0/blueprint-inherit.md
//
// Three subtasks share one pair of parent class assets (doc mentions "prerequisite parent Inherit_Base_A / _B"
// in three places with same name):
// Parent class built once with all three subtasks' required items (Val / override-able Foo / Editable Speed),
// each subtask only rebuilds child class in its own space; "two assets" refers to child class.
//
// Probe (collaboration spec point 6):
// - Child class creation: create_blueprint's parentClass passes parent blueprint generated class real name
//   (Inherit_Base_A_C; bridge create_blueprint -> ForgeMindResolveBlueprintClassByName
//   with _C fallback, same as Blueprint_ParentCall_Tests' BP_Base_C).
// - Override parent function: create_function_graph builds same-named function graph in child class
//   (GraphNameExists only checks this blueprint's graphs, not parent chain, so it allows),
//   body uses kind=callParentFunction node (UK2Node_CallParentFunction, name = overridden function's engine real name)
//   to call parent implementation.
// - Override parent variable default: add_blueprint_variable goes through
//   FBlueprintEditorUtils::AddMemberVariable; parent same-name variable will be rejected
//   (BlueprintEditorUtils.cpp L4776-4782 GetClassVariableList includes parent chain).
//   When engine modifies parent variable default in child class Class Defaults, it writes child class
//   generated class CDO's property value -- bridge set_blueprint_variable_default's CDO property
//   branch is exactly this semantics, so use it.
//   DSL side (2026-10-05 fix): the IR applier's add_blueprint_variable now takes the same route when
//   the engine refuses the name -- LookupBlueprintVariable identifies the inherited variable and the
//   value goes onto the child CDO (WritePropertyDefaultToClassCDO), reported as added +
//   inheritedOverride. Settings that belong to the declaring class (flags/category/replication)
//   fail there instead of being dropped when they contradict it.
// - DSL side: **[Override] not recognized by BlueprintTranslator** (method-level only recognizes [Function] /
//   [Event] / [Macro] / [ConstructionScript] / [InterfaceImplementation] /
//   [InputAction|InputAxis] / [Timeline]; [Override] would fall into "unrecognized attributed method" and skip).
//   Parent **function** override must fall into function graph for base.Foo() to reach parent implementation
//   (CollectMethodForDefinition's parent form correction comment suggests same), so child class
//   override method written directly as [Function]; [Override] noted as gap for pending support.
//   Additional note: [Function] void without out param defaults to event graph (custom event) on DSL side,
//   different from A-side "function graph" form -- this is DSL default preference, not this case's design.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) was inserted
// on the user's instruction (2026-10-05), after the case and its results were confirmed
// visually; snapshots cover the child assets, following Blueprint_ParentCall_Tests.
// =========================================================================

public sealed class Blueprint_Inherit : ITestCase
{
    public string Name => "Blueprint Inherit (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 27;

    public async Task Run(TestContext ctx)
    {
        // -- Shared parent class (built first in each space, prerequisite for three subtasks) --
        // A space: Inherit_Base_A (Extends Actor)
        //   Val : int (inherited variable for subtask 1)
        //   Speed : int (editable variable, Instance Editable, default 600)
        //   Foo() (parent implementation for subtask 1/2, prints "base")
        const string baseA = "/Game/Tests/UE5_0/Inherit_Base_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, baseA);
        BlueprintTestHelper.AddVariable(ctx, baseA, "Val", "int");
        // instanceEditable is an add_blueprint_variable field, BlueprintTestHelper's
        // AddVariable not exposed -- directly sends editor command (same as Blueprint_Variable_Tests'
        // MoveSpeed approach).
        var addSpeed = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_variable", new
        {
            path = baseA,
            name = "Speed",
            type = "int",
            @default = "600",
            instanceEditable = true,
        });
        ctx.AssertTrue(addSpeed["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_variable Speed @ {baseA}: "
            + $"{addSpeed["response"]?["reason"]?.GetValue<string>() ?? ""}");

        var fBase = BlueprintTestHelper.CreateFunction(ctx, baseA, "Foo");
        var pBase = BlueprintTestHelper.AddNode(ctx, baseA, "Foo", "PrintString", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, baseA, pBase.nodeId, "InString", "base");
        BlueprintTestHelper.Connect(ctx, baseA, fBase.entryId, "then", pBase.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, baseA, pBase.nodeId, "then", fBase.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, baseA);

        // B space: parent class Inherit_Base_B dispatched together with subtask 1's child class.
        // Parent class block built once with all three subtasks' prerequisites (doc task 1 parent block + task 3 Speed).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Inherit_Child_B");

        // -- Subtask 1: Child class adds new function --
        // MCP steps: Inherit_Child_A (Extends Inherit_Base_A_C)
        //         -> add function Extra: PrintString("child")
        const string a1 = "/Game/Tests/UE5_0/Inherit_Child_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a1, "Inherit_Base_A_C");
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Extra");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Extra", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "child");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // -- Subtask 2: Override parent function + call parent --
        // MCP steps: Inherit_Override_A (Extends Inherit_Base_A_C)
        //   Function Foo (overrides parent same-name function): Parent: Foo -> PrintString("overridden")
        const string a2 = "/Game/Tests/UE5_0/Inherit_Override_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a2, "Inherit_Base_A_C");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Foo");
        var parent2 = BlueprintTestHelper.AddNode(ctx, a2, "Foo", "Foo", 300, 0,
            kind: "callParentFunction");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Foo", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, p2.nodeId, "InString", "overridden");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", parent2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, parent2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc; only [Override] -> [Function], see file header notes)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Inherit_Override_B");

        // -- Subtask 3: Override parent variable default --
        // MCP steps: Inherit_Var_A (Extends Inherit_Base_A_C)
        //   -> change inherited Speed default from parent's 600 to this class's 300
        //   (set_blueprint_variable_default CDO property branch; parent Speed itself unchanged)
        const string a3 = "/Game/Tests/UE5_0/Inherit_Var_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a3, "Inherit_Base_A_C");
        var setSpeed = BlueprintTestHelper.EditorCall(ctx, "set_blueprint_variable_default", new
        {
            path = a3,
            name = "Speed",
            @default = "300",
        });
        ctx.AssertTrue(setSpeed["response"]?["updated"]?.GetValue<bool>() ?? false,
            $"set_blueprint_variable_default Speed=300 @ Inherit_Var_A: "
            + $"{setSpeed["response"]?["reason"]?.GetValue<string>() ?? ""}");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc; the DSL now reaches the same child-CDO override the
        // A side uses -- see the header note)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Inherit_Var_B");

        await Task.CompletedTask;
    }
}
