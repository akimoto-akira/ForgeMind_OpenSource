// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 19 - Blueprint Construction Script (UE 5.0) - from Tasks/UE_5.0/blueprint-construction-script.md
// Probe (spec point 6): Construction script graph is supported in DSL translator ([ConstructionScript]
// attribute -> type "constructionScript", see BlueprintTranslator.cs line 593-596 / 808-810).
// Components added to SCS via AddComponent (AddComponent confirmed supported). So this group is more
// stable than timer/material groups, but some pin names (SetScale/SetRelativeLocation/SetVisibility
// real-name params) pending bridge review against engine real names.
//
// 2026-10-03 Tested (TestRuns/20261003_0146) finalized:
//   MCP side construction script graph real name is **"UserConstructionScript"**, not "ConstructionScript".
//   Bridge test error: No graph named 'ConstructionScript'; available graphs:
//   EventGraph, UserConstructionScript -- see add_blueprint_node Scale.
//   Note distinction with DSL side: DSL [ConstructionScript] takes a different path via dsl/apply_asset_ir
//   (translator NewGraph uses method name + type "constructionScript"), the two sides have different graph names.
// Note: diff comparison (VerifyIrSnapshot) separately inserted per user 2026-10-03 instruction, covering
// A-side (Ctor_Var_A / Ctor_Loop_A / Ctor_Branch_A) and B-side
// (Ctor_Var_B / Ctor_Loop_B / Ctor_Branch_B) all assets.
// =========================================================================

public sealed class Blueprint_ConstructionScript : ITestCase
{
    public string Name => "Blueprint Construction Script (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 19;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Set property from variable --
        // MCP steps: Ctor_Var_A (Extends Actor) + variable Scale:int(default 2)
        //   + Construction Script: Set Mesh's Scale to Scale
        const string a1 = "/Game/Tests/UE5_0/Ctor_Var_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddComponent(ctx, a1, "Mesh", "StaticMeshComponent");
        BlueprintTestHelper.AddVariable(ctx, a1, "Scale", "int", defaultValue: "2");
        // Construction script graph real name "UserConstructionScript" (2026-10-03 tested, see file header).
        var getScale1 = BlueprintTestHelper.AddNode(ctx, a1, "UserConstructionScript", "Scale", 100, 0, kind: "getter");
        // UE 5.0 no longer has USceneComponent::SetScale (engine index 5.0 has no such real name, the created
        // "Set Scale" node has empty pins); real name is SetRelativeScale3D (SceneComponent,
        // param NewScale3D : FVector). int Scale composes into Vector via MakeVector,
        // int->float implicit conversion handled by engine.
        var mkVec1 = BlueprintTestHelper.AddNode(ctx, a1, "UserConstructionScript", "MakeVector", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, getScale1.nodeId, "Scale", mkVec1.nodeId, "X");
        BlueprintTestHelper.Connect(ctx, a1, getScale1.nodeId, "Scale", mkVec1.nodeId, "Y");
        BlueprintTestHelper.Connect(ctx, a1, getScale1.nodeId, "Scale", mkVec1.nodeId, "Z");
        var setScale1 = BlueprintTestHelper.AddNode(ctx, a1, "UserConstructionScript", "SetRelativeScale3D", 500, 0, "SceneComponent");
        var getMesh1 = BlueprintTestHelper.AddNode(ctx, a1, "UserConstructionScript", "Mesh", 350, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a1, getMesh1.nodeId, "Mesh", setScale1.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a1, mkVec1.nodeId, "ReturnValue", setScale1.nodeId, "NewScale3D");
        // Construction script entry (engine built-in, not built by DSL/IR) -> first execution node;
        // missing connection shows "function header not linked to subsequent nodes"
        // (2026-10-03 user visual feedback).
        var entry1 = BlueprintTestHelper.FindGraphEntryNode(ctx, a1, "UserConstructionScript");
        BlueprintTestHelper.Connect(ctx, a1, entry1, "then", setScale1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ctor_Var_B");

        // -- Subtask 2: Generate components in loop --
        // MCP steps: Ctor_Loop_A (Extends Actor) + inside UserConstructionScript
        //   For Loop(0..2) each iteration adds StaticMeshComponent
        // Note: markdown original intent "set position by Index" not yet restored -- DSL AddComponent
        //   is **statement form** (doesn't bind return value), can't get component reference to call
        //   SetRelativeLocation; and new Vector(containing loop variable) goes through literal
        //   resolution path which is unreliable. Both sides use default transform (FTransform=0).
        //   Pending confirmation of loop variable participating in Vector construction lowering.
        const string a2 = "/Game/Tests/UE5_0/Ctor_Loop_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var fl2 = BlueprintTestHelper.AddNode(ctx, a2, "UserConstructionScript", "ForLoop", 100, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, a2, fl2.nodeId, "LastIndex", "2");
        // AddComponent's component class goes through node targetClass field (same form as DSL translator
        // LowerAddComponent) -- **not** Class pin (tested "Add Actor Component"
        // node has no such pin: has no pin named 'Class').
        var addC2 = BlueprintTestHelper.AddNode(ctx, a2, "UserConstructionScript", "AddComponent", 400, 0,
            targetClass: "/Script/Engine.StaticMeshComponent");
        // RelativeTransform pin has no default: supplement default identity transform MakeTransform(Location=0,0,0).
        var mkTf2 = BlueprintTestHelper.AddNode(ctx, a2, "UserConstructionScript", "MakeTransform", 250, 120, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, mkTf2.nodeId, "Location", "0,0,0");
        BlueprintTestHelper.Connect(ctx, a2, mkTf2.nodeId, "ReturnValue", addC2.nodeId, "RelativeTransform");
        BlueprintTestHelper.Connect(ctx, a2, fl2.nodeId, "LoopBody", addC2.nodeId, "execute");
        // Construction script entry -> ForLoop (first execution node).
        var entry2 = BlueprintTestHelper.FindGraphEntryNode(ctx, a2, "UserConstructionScript");
        BlueprintTestHelper.Connect(ctx, a2, entry2, "then", fl2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL form one of two: typeof arg + default transform (FTransform=0).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ctor_Loop_B");

        // -- Subtask 3: With Branch condition --
        // MCP steps: Ctor_Branch_A (Extends Actor) + variable Enabled:bool(default true)
        //   + Construction Script: Branch(Enabled) True only then Add StaticMeshComponent
        const string a3 = "/Game/Tests/UE5_0/Ctor_Branch_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddComponent(ctx, a3, "Mesh", "StaticMeshComponent");
        BlueprintTestHelper.AddVariable(ctx, a3, "Enabled", "bool", defaultValue: "true");
        var getEn3 = BlueprintTestHelper.AddNode(ctx, a3, "UserConstructionScript", "Enabled", 100, 0, kind: "getter");
        var br3 = BlueprintTestHelper.AddNode(ctx, a3, "UserConstructionScript", "Branch", 300, 0);
        BlueprintTestHelper.Connect(ctx, a3, getEn3.nodeId, "Enabled", br3.nodeId, "Condition");
        // True branch: Mesh.SetVisibility(true). Param real name bNewVisibility
        // (USceneComponent::SetVisibility(bool bNewVisibility, ...); tested
        // "NewVisibility" reports has no pin named 'NewVisibility').
        var setVis3 = BlueprintTestHelper.AddNode(ctx, a3, "UserConstructionScript", "SetVisibility", 600, 0, "SceneComponent");
        var getMesh3 = BlueprintTestHelper.AddNode(ctx, a3, "UserConstructionScript", "Mesh", 450, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a3, getMesh3.nodeId, "Mesh", setVis3.nodeId, "__target");
        BlueprintTestHelper.SetPinDefault(ctx, a3, setVis3.nodeId, "bNewVisibility", "true");
        BlueprintTestHelper.Connect(ctx, a3, br3.nodeId, "then", setVis3.nodeId, "execute");
        // Construction script entry -> Branch (first execution node).
        var entry3 = BlueprintTestHelper.FindGraphEntryNode(ctx, a3, "UserConstructionScript");
        BlueprintTestHelper.Connect(ctx, a3, entry3, "then", br3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Ctor_Branch_B");

        await Task.CompletedTask;
    }
}
