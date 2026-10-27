// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 65 - Blueprint Reference (UE 5.0) - from Tasks/UE_5.0/Blueprint_Reference.md
//
// Probe (collaboration spec point 6):
// - Hard object reference: add_blueprint_variable's type parser (ForgeMindBlueprintUtils.cpp
//   BaseTypeTextToPinType) has no keyword for "Actor", so it falls through to the UClass-name
//   branch and builds PC_Object + AActor (SetObjectPinType). Same form Blueprint_BindInstanceEvent
//   already relies on ("Target", "Actor").
// - Soft reference: two bridge spellings reach the same parser. The editor's own type
//   texts "Soft Object Reference" / "Soft Class Reference" (:1710-1719) are the generic
//   (UObject-base) forms Blueprint_SoftRef uses; the doc's "Soft Actor Reference"
//   (TSoftObjectPtr<AActor>) is spelled with the template form "TSoftObjectPtr<Actor>"
//   — the same text the DSL side's IR sends, resolved by PinTypeTextToPinType's
//   tsoftobjectptr<X> branch (inner resolved as a base type, then re-categorised
//   PC_SoftObject KEEPING the sub-object, so the variable reads "Soft Actor Reference").
//   Subtask 2's A side passes the template text directly (user verdict 2026-10-21: the
//   generic form was misread as plain object vs the B side's TSoftObjectPtr<Actor>).
// - Weak reference (FWeakObjectProperty) is never surfaced as a blueprint variable type by the
//   editor (no type-tree entry), so it is out of scope for a blueprint-level case.
// - Function parameters: create_function_graph's 'params' carries the type text the same way,
//   so `Actor t` becomes an entry-node output pin "t" (same form as Blueprint_Particle's
//   `Vector loc`). Writing it back into a variable is a variable-setter node (kind "setter"),
//   connect-by-name on the "Target" pin (same form as Blueprint_Variable's temp setter).
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison).
// =========================================================================

public sealed class Blueprint_Reference : ITestCase
{
    public string Name => "Blueprint Reference (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 65;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: hard object reference variable --
        // MCP steps: Ref_Hard_A (Extends Actor) + Target : Actor
        const string a1 = "/Game/Tests/UE5_0/Ref_Hard_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        // Type text is a bare class name -> PC_Object with AActor as the sub-category object.
        BlueprintTestHelper.AddVariable(ctx, a1, "Target", "Actor");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ref_Hard_B");

        // -- Subtask 2: soft object reference variable --
        // MCP steps: Ref_Soft_A (Extends Actor) + Target : Soft Actor Reference
        const string a2 = "/Game/Tests/UE5_0/Ref_Soft_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        // Template spelling TSoftObjectPtr<Actor> — the same text the B side's IR sends;
        // the generic "Soft Object Reference" text builds a UObject-base variable instead
        // (misread as plain object in the editor, user verdict 2026-10-21).
        BlueprintTestHelper.AddVariable(ctx, a2, "Target", "TSoftObjectPtr<Actor>");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ref_Soft_B");

        // -- Subtask 3: reference as a function parameter --
        // MCP steps: Ref_Param_A (Extends Actor) + Target : Actor
        //   function SetTarget(Actor t) -> Target = t
        const string a3 = "/Game/Tests/UE5_0/Ref_Param_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Target", "Actor");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "SetTarget",
            new[] { new { name = "t", type = "Actor" } });
        var setTarget3 = BlueprintTestHelper.AddNode(ctx, a3, "SetTarget", "Target", 300, 0,
            kind: "setter");
        // Entry-node parameter output pin -> setter's variable pin (both named after the
        // declared identifiers).
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "t", setTarget3.nodeId, "Target");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", setTarget3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, setTarget3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ref_Param_B");

        await Task.CompletedTask;
    }
}
