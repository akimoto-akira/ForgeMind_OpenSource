// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 45 - Blueprint Data Asset (UE 5.0) - from Tasks/UE_5.0/blueprint-dataasset.md
//
// Engine constraints (user correction 2026-10-14, engine DataAsset.h):
// - UDataAsset is UCLASS(abstract) — CanCreateBlueprintOfClass refuses abstract
//   classes, so a data-asset blueprint can only inherit "PrimaryDataAsset" or a
//   subclass of it (UPrimaryDataAsset carries Blueprintable).
// - UPrimaryDataAsset exposes NO inheritable events or functions — a data-asset
//   blueprint holds variables only (no event/function nodes are added here).
// - A data-asset class variable shows up in the Data Asset editor only when it is
//   editable — AddVariable passes instanceEditable:true (clears CPF_DisableEditOnInstance,
//   AddMemberVariable's default) and the DSL side marks [Variable(Editable)].
//
// Probe (collaboration spec point 6):
// - create_blueprint resolves parentClass via FindObject(ANY_PACKAGE) on loaded objects.
//   "PrimaryDataAsset" is /Script/Engine.PrimaryDataAsset; bare-name resolution at editor
//   startup is unverified (same caveat as AddNode's A/U-prefix note) - rejection = gap.
// - Blueprint-typed variable (task 2): A-side type string uses the generated class name
//   "DA_Class_A_C" (CreateBlueprint generated-class convention, BP_Base -> BP_Base_C).
// - External member get (Asset.Value): expressed as getter with ownerClass = DA generated
//   class, target fed via "__target" (same convention as member call nodes in
//   Blueprint_DynamicMaterial_Tests) - pin name assumption noted, test is spec.
// - DSL side: [Extends(typeof(PrimaryDataAsset))] parent resolution, [Variable(Editable)]
//   and [Variable] DA_Class_B Asset (blueprint-type variable) are unverified forms.
//
// Mis-touch comparison (VerifyIrSnapshot): each A-side and DSL-side asset builds its
// own IR baseline on first run (no failure); subsequent runs diff against it, catching
// later changes that accidentally damage already-tested asset structures.
// =========================================================================

public sealed class Blueprint_DataAsset : ITestCase
{
    public string Name => "Blueprint Data Asset (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 45;

    public async Task Run(TestContext ctx)
    {
        // -- Task 1: Declare a DataAsset class --
        // MCP steps: DA_Class_A (parent PrimaryDataAsset - UDataAsset itself is
        // abstract and cannot be a blueprint parent) + editable variable Value : int.
        const string a1 = "/Game/Tests/UE5_0/DA_Class_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a1, "PrimaryDataAsset");
        BlueprintTestHelper.AddVariable(ctx, a1, "Value", "int", "0",
            instanceEditable: true);
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (parent corrected to PrimaryDataAsset; [Variable(Editable)] marks
        // the variable discoverable in the Data Asset editor)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/DA_Class_B");

        // -- Task 2: Read DataAsset fields --
        // MCP steps: DA_Read_A (Extends Actor) + variable Asset : DA_Class_A
        //   (type string = generated class name "DA_Class_A_C"); function R:
        //   Asset -> Get Value -> PrintString.
        const string a2 = "/Game/Tests/UE5_0/DA_Read_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "Asset", "DA_Class_A_C");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "R");
        var getAsset2 = BlueprintTestHelper.AddNode(ctx, a2, "R", "Asset", 100, 100,
            kind: "getter");
        // External member get: getter with ownerClass = DA generated class, target via
        // "__target" (pin name assumption, see file header) - test is spec.
        var getValue2 = BlueprintTestHelper.AddNode(ctx, a2, "R", "Value", 300, 0,
            "DA_Class_A_C", kind: "getter");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "R", "PrintString", 550, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getAsset2.nodeId, "Asset", getValue2.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a2, getValue2.nodeId, "Value", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc, Mode = Create added; [Variable] DA_Class_B Asset
        // blueprint-type variable kept verbatim - unverified form, test is spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/DA_Read_B");

        // -- Task 3: PrimaryDataAsset (asset identity) --
        // MCP steps: DA_Prim_A (parent PrimaryDataAsset) + editable variable Id : string.
        const string a3 = "/Game/Tests/UE5_0/DA_Prim_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a3, "PrimaryDataAsset");
        BlueprintTestHelper.AddVariable(ctx, a3, "Id", "string", "Asset_001",
            instanceEditable: true);
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path ([Variable(Editable)] marks the variable discoverable in the
        // Data Asset editor)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/DA_Prim_B");

        await Task.CompletedTask;
    }
}
