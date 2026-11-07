// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 82 - Material Light Vector (UE 5.0)
//        - from the UE 5.0 material task doc "light vector / shading model"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - light vector", which cannot be written in source)
//
// Light direction and shading-model data-flow expressions of the material
// domain: UMaterialExpressionLightVector, UMaterialExpressionReflectionVectorWS,
// UMaterialExpressionShadingModel (UE 5.0.3 Materials/MaterialExpression*.h).
// LightVector and ReflectionVectorWS carry no properties and one output each;
// ShadingModel carries the UPROPERTY "ShadingModel" (EMaterialShadingModel,
// default MSM_DefaultLit) and one output. All class names were verified present
// in engine.spk via ForgeMind.SpkTool "class" before writing; the whole B-side
// DSL below was verified with "translate" (0 diagnostics).
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//
// DSL syntax note: the task doc sketches the material DSL in an attribute / call
// form ([LightVector] MaterialExpression L; [Output(Normal)] ... = L) that is not
// the documented material DSL surface. Per the standing decision for material
// task docs, the case uses the documented, translator-supported form:
// [Material(Path, Mode = Create)] + [MaterialSettings], "NodeType v = new
// NodeType();" declarations, and "OutputPin = node;" for the material output.
//
// Doc/engine divergence notes:
//   * Q1 domain and target pin: LightVector compiles ONLY in LightFunction or
//     DeferredDecal materials - FHLSLMaterialTranslator::LightVector errors out
//     otherwise (HLSLMaterialTranslator.cpp:4848-4851). Per user instruction both
//     channels create Mat_LightVec1_* with Domain=LightFunction; and because that
//     domain forces Unlit (Material.cpp:5224-5227), the root consumes only
//     EmissiveColor - the doc's Normal wire would compile but never take effect, so
//     both channels wire LightVector -> EmissiveColor (user ruling).
//     ReflectionVectorWS and ShadingModel have no domain restriction (Q2/Q3 stay
//     Surface).
//   * The doc's question 3 writes the shading-model property as Model=DefaultLit;
//     the engine UPROPERTY on UMaterialExpressionShadingModel is named
//     "ShadingModel" (MaterialExpressionShadingModel.h:34), so both channels use
//     that name.
//   * Q3 also has to switch the material itself: wiring the root node's ShadingModel pin is only
//     honored while UMaterial::ShadingModel == MSM_FromMaterialExpression -
//     IsShadingModelFromMaterialExpression() reads that property, not the connection
//     (Material.cpp:5235-5238), and RebuildShadingModelField() collects the models from the graph
//     expressions only in that case, on every recompile (Material.cpp:2112 -> 4027-4040); otherwise
//     the field stays the single property value and the old-graph conversion even injects a
//     ConstantShadingModel node to pin it (Material.cpp:2803-2815). A link without the switch is
//     therefore inert, and it is invisible because the node's own value here is DefaultLit, the same
//     as the default property. So both channels set ShadingModel = FromMaterialExpression and read
//     it back from materialSettings (the read side now reports the switch as well as the field).
//     The engine enum entry is MSM_FromMaterialExpression, DisplayName "From Material Expression"
//     (EngineTypes.h:593); the bridge resolves and reports the prefix-stripped short name.
//   * The doc header also lists LightmapUVs / ShadingPathSwitch, but no question
//     uses them, so they are not built here.
//
// Verification caveat (user note): a failed MATERIAL compile does not currently
// fail the test run - compile_material reports "pending" because shader
// compilation is asynchronous in the editor (the blueprint side already fails
// the run on a bad compile). A domain mistake here would therefore not surface
// as a test failure; it is caught by the engine-fact domain assignment above and
// by visual inspection, not by the compiled flag.
//
// IR snapshots (BlueprintTestHelper.VerifyIrSnapshot) are taken on user instruction:
// one point per asset, 6 in total (Q1-Q3, A/B each), placed right after each asset's
// save_asset and before the read-back assertions. The first run seeds the 6 baselines
// into TestRuns/5.0/ (a seed is not a pass); the second run must report all 6 as
// "IR snapshot match" for the case to be stable across runs. On a mismatch the
// baseline is kept untouched and the root-cause analysis goes to the user first.
// The step-command business flags and the ExpectExpressions / ExpectMaterialSetting
// checks stay in place alongside the snapshots.
// =========================================================================

public sealed class Material_LightVector : ITestCase
{
    public string Name => "Material Light Vector (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 82;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_LightVectorNormal(ctx);
        BuildQ2_ReflectionVectorBaseColor(ctx);
        BuildQ3_ShadingModelOutput(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: LightVector -> Normal
    // =====================================================================

    private static void BuildQ1_LightVectorNormal(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_LightVec1_A";

        // Step 1: create material. LightVector only compiles in LightFunction or
        // DeferredDecal materials (engine Errorf otherwise) - per user instruction the
        // domain is LightFunction on both channels.
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "LightFunction",
            blendMode = "Opaque",
        }), "created", "create_material Mat_LightVec1_A");

        // Step 2: add MaterialExpressionLightVector
        string lvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionLightVector",
        }), "add MaterialExpressionLightVector");

        // Step 3: link its output -> EmissiveColor. The doc says Normal, but in a
        // LightFunction material the root exposes only EmissiveColor (the domain forces
        // Unlit) - wiring Normal compiles yet never takes effect, so the wire lands on
        // the pin that is actually consumed (user ruling).
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = lvId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect LightVector -> Material.EmissiveColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_LightVec1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_LightVec1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison
        ExpectExpressions(ctx, a1, ("MaterialExpressionLightVector", 1));

        // -- B-side: DSL in the documented material syntax (LightFunction domain -
        // LightVector errors out in any other domain; and only EmissiveColor is
        // consumed there, so the wire lands on it, not the doc's Normal) --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_LightVec1_B" }),
            "saved", "save_asset Mat_LightVec1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_LightVec1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_LightVec1_B", ("MaterialExpressionLightVector", 1));
    }

    // =====================================================================
    // Q2: ReflectionVectorWS -> BaseColor
    // =====================================================================

    private static void BuildQ2_ReflectionVectorBaseColor(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_ReflVec1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ReflVec1_A");

        // Step 2: add MaterialExpressionReflectionVectorWS (its optional
        // CustomWorldNormal input stays unwired - the doc wires nothing into it)
        string rvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionReflectionVectorWS",
        }), "add MaterialExpressionReflectionVectorWS");

        // Step 3: link its output -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = rvId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect ReflectionVectorWS -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_ReflVec1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_ReflVec1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // per-asset self-comparison
        ExpectExpressions(ctx, a2, ("MaterialExpressionReflectionVectorWS", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ReflVec1_B" }),
            "saved", "save_asset Mat_ReflVec1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ReflVec1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_ReflVec1_B", ("MaterialExpressionReflectionVectorWS", 1));
    }

    // =====================================================================
    // Q3: ShadingModel (DefaultLit) -> material ShadingModel input
    // =====================================================================

    private static void BuildQ3_ShadingModelOutput(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_ShadingModel1_A";

        // Step 1: create material. ShadingModel = FromMaterialExpression is the switch that makes the
        // root pin effective at all (see the Q3 note above); without it the engine keeps using the
        // material's own shading-model value and the wire below is compiled but ignored.
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
            ShadingModel = "FromMaterialExpression",
        }), "created", "create_material Mat_ShadingModel1_A");

        // Step 2: add MaterialExpressionShadingModel (ShadingModel=DefaultLit - the
        // doc writes Model=, but the engine UPROPERTY is named ShadingModel)
        string smId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionShadingModel",
            shadingModel = "DefaultLit",
        }), "add MaterialExpressionShadingModel(DefaultLit)");

        // Step 3: link its output -> the material's ShadingModel input
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = smId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "ShadingModel",
        }), "connected", "connect ShadingModel -> Material.ShadingModel");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_ShadingModel1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_ShadingModel1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // per-asset self-comparison
        ExpectExpressions(ctx, a3, ("MaterialExpressionShadingModel", 1));
        ExpectMaterialSetting(ctx, a3, "ShadingModel", "FromMaterialExpression");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ShadingModel1_B" }),
            "saved", "save_asset Mat_ShadingModel1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ShadingModel1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_ShadingModel1_B", ("MaterialExpressionShadingModel", 1));
        ExpectMaterialSetting(ctx, "/Game/Tests/UE5_0/Mat_ShadingModel1_B", "ShadingModel", "FromMaterialExpression");
    }
}
