// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 17 - Blueprint Dynamic Material (UE 5.0) - from Tasks/UE_5.0/blueprint-dynamic-material.md
// Probe (spec point 6): CreateDynamicMaterialInstance / SetScalarParameterValue /
// SetVectorParameterValue have no implemented lowering evidence in bridge/DSL translator (only
// KismetMaterialLibrary function names mentioned in comments). MCP step node/pin names confirmed
// against engine source (KismetMaterialLibrary.h / PrimitiveComponent.h / MaterialInstanceDynamic.h).
// Known convention: components added to SCS via AddComponent; component member calls follow external
// member node pattern (ownerClass=component class + "__target" connection hits component variable,
// see blueprint-save-game.md).
// Note: diff comparison (VerifyIrSnapshot) inserted per user instruction, covering all A-side and B-side assets.
// =========================================================================

public sealed class Blueprint_DynamicMaterial : ITestCase
{
    public string Name => "Blueprint Dynamic Material (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 17;

    public async Task Run(TestContext ctx)
    {
        // Prerequisite: BaseMat material asset (parent of dynamic material instance). GroupLifecycle
        // clears Content/Tests each run, so recreate (if bridge supports material asset creation).
        const string baseMat = "/Game/Tests/UE5_0/BaseMat";
        // Placeholder: actual material asset creation command (create_material) pending bridge confirmation;
        // path recorded here only.

        // -- Subtask 1: CreateDynamicMaterialInstance --
        // MCP steps: MatInst_Make_A (Extends Actor, with Mesh component) + function Make:
        //   CreateDynamicMaterialInstance(Parent=BaseMat) -> SetMaterial(0, result)
        const string a1 = "/Game/Tests/UE5_0/MatInst_Make_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddComponent(ctx, a1, "Mesh", "StaticMeshComponent");
        BlueprintTestHelper.AddVariable(ctx, a1, "BaseMat", "MaterialInterface");
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Make");
        // Node name "CreateDynamicMaterialInstance" (KismetMaterialLibrary), pins
        // "Parent"/"ReturnValue" pending bridge confirmation.
        var mk1 = BlueprintTestHelper.AddNode(ctx, a1, "Make", "CreateDynamicMaterialInstance", 300, 0, "KismetMaterialLibrary");
        var getBase1 = BlueprintTestHelper.AddNode(ctx, a1, "Make", "BaseMat", 100, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a1, getBase1.nodeId, "BaseMat", mk1.nodeId, "Parent");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", mk1.nodeId, "execute");
        // Mesh.SetMaterial(0, m): external member call, ownerClass=StaticMeshComponent,
        // "__target" connects Mesh component variable; pin real names ElementIndex / Material
        // (UPrimitiveComponent::SetMaterial(int32 ElementIndex, UMaterialInterface* Material)).
        var setMat1 = BlueprintTestHelper.AddNode(ctx, a1, "Make", "SetMaterial", 600, 0, "StaticMeshComponent");
        var getMesh1 = BlueprintTestHelper.AddNode(ctx, a1, "Make", "Mesh", 450, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a1, getMesh1.nodeId, "Mesh", setMat1.nodeId, "__target");
        BlueprintTestHelper.SetPinDefault(ctx, a1, setMat1.nodeId, "ElementIndex", "0");
        BlueprintTestHelper.Connect(ctx, a1, mk1.nodeId, "ReturnValue", setMat1.nodeId, "Material");
        BlueprintTestHelper.Connect(ctx, a1, mk1.nodeId, "then", setMat1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, setMat1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/MatInst_Make_B");

        // -- Subtask 2: SetScalarParameterValue --
        // MCP steps: reuse m (MaterialInstanceDynamic variable) + function SetOp ->
        //   m.SetScalarParameterValue(Param="Opacity", Value=0.5)
        const string a2 = "/Game/Tests/UE5_0/MatInst_Scalar_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "m", "MaterialInstanceDynamic");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "SetOp");
        // Node name "SetScalarParameterValue" (UMaterialInstanceDynamic), pin real
        // names ParameterName / Value (MaterialInstanceDynamic.h); "__target" connects m variable.
        var setS2 = BlueprintTestHelper.AddNode(ctx, a2, "SetOp", "SetScalarParameterValue", 300, 0, "MaterialInstanceDynamic");
        var getM2 = BlueprintTestHelper.AddNode(ctx, a2, "SetOp", "m", 100, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a2, getM2.nodeId, "m", setS2.nodeId, "__target");
        BlueprintTestHelper.SetPinDefault(ctx, a2, setS2.nodeId, "ParameterName", "Opacity");
        BlueprintTestHelper.SetPinDefault(ctx, a2, setS2.nodeId, "Value", "0.5");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", setS2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, setS2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/MatInst_Scalar_B");

        // -- Subtask 3: SetVectorParameterValue --
        // MCP steps: reuse m + function SetCol ->
        //   m.SetVectorParameterValue(Param="Tint", Value=(1,0,0))
        const string a3 = "/Game/Tests/UE5_0/MatInst_Vec_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "m", "MaterialInstanceDynamic");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "SetCol");
        // Node name "SetVectorParameterValue", pin real names ParameterName / Value
        // (MaterialInstanceDynamic.h: FLinearColor Value); default value format "(R=1,G=0,B=0,A=1)"
        // (FLinearColor ImportText with parentheses, same convention as DebugDraw LineColor).
        var setV3 = BlueprintTestHelper.AddNode(ctx, a3, "SetCol", "SetVectorParameterValue", 300, 0, "MaterialInstanceDynamic");
        var getM3 = BlueprintTestHelper.AddNode(ctx, a3, "SetCol", "m", 100, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a3, getM3.nodeId, "m", setV3.nodeId, "__target");
        BlueprintTestHelper.SetPinDefault(ctx, a3, setV3.nodeId, "ParameterName", "Tint");
        BlueprintTestHelper.SetPinDefault(ctx, a3, setV3.nodeId, "Value", "(R=1,G=0,B=0,A=1)");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", setV3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, setV3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/MatInst_Vec_B");

        await Task.CompletedTask;
    }
}
