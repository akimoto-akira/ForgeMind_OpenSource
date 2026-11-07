// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 83 - Material Volume Cloud (UE 5.0)
//        - from the UE 5.0 material task doc "volume / cloud"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - volumetric cloud", which cannot be written in source)
//
// Volumetric rendering output expressions of the material domain:
//   UMaterialExpressionVolumetricAdvancedMaterialOutput,
//   UMaterialExpressionCloudSampleAttribute,
//   UMaterialExpressionAbsorptionMediumMaterialOutput
// (UE 5.0.3 Materials/MaterialExpression*.h). The two *Output classes derive
// from UMaterialExpressionCustomOutput: placing one in the graph IS the
// volumetric output - it has no output wire of its own, so the doc's "link ->
// the material's volume output" step is realized by the node's presence plus
// wiring the node's inputs. All class names were verified present in engine.spk
// via ForgeMind.SpkTool "class" before writing; the whole B-side DSL below was
// verified with "translate" (0 diagnostics).
//
// Doc/engine divergences (ruled by the user, then written per engine fact):
//   * Question 2 names a "CloudLayer" node (LayerName="CloudLayer0"). UE 5.0.3
//     has no UMaterialExpressionCloudLayer class - MaterialExpressionCloudLayer.h
//     defines only UMaterialExpressionCloudSampleAttribute (four named outputs:
//     Altitude / AltitudeInLayer / NormAltitudeInLayer / ShadowSampleDistance, no
//     properties, no inputs). Per user ruling the case builds
//     CloudSampleAttribute and wires its default output (Altitude) - the doc's
//     "cloud layer attribute output" intent - to BaseColor. There is no LayerName
//     property to set, so that key is dropped.
//   * Question 1 wires the scattering Const3 into a "Scattering" pin of
//     VolumetricAdvancedMaterialOutput. The class has no such pin (real optional
//     inputs: PhaseG / PhaseG2 / PhaseBlend / MultiScattering* /
//     ConservativeDensity). Per user ruling the constant lands on
//     ConservativeDensity (the float3 input, matching the Const3 shape).
//   * Question 3 names no inputs for AbsorptionMediumMaterialOutput (its only
//     input is the optional TransmittanceColor); per the doc's own step list the
//     node is created unwired on both channels.
//
// All three materials are created with Domain=Volume per the doc's DSL headers.
//
//   * The doc asks for BlendMode="Opaque" on every Volume material, but the engine
//     rejects that combination outright: FHLSLMaterialTranslator's domain/blend
//     validation errors out with "Volume materials must use an Additive blend mode."
//     whenever Domain == MD_Volume and BlendMode != BLEND_Additive
//     (UE 5.0.3 HLSLMaterialTranslator.cpp:1021-1024) - a SM5 compile error, visible
//     in the editor while compile_material still reports success. Blend mode is
//     therefore Additive on both channels (user-reported error, fixed per engine
//     fact); the doc's Opaque cannot be realized in a Volume material at all.
//     Task-doc revision for this line is left to the user.
//
// IR snapshots (BlueprintTestHelper.VerifyIrSnapshot) are taken on user instruction:
// one point per asset, 6 in total (Q1-Q3, A/B each), placed right after each asset's
// save_asset and before the read-back assertions. The first run seeds the 6 baselines
// into TestRuns/5.0/ (a seed is not a pass); the second run must report all 6 as
// "IR snapshot match" for the case to be stable across runs. On a mismatch the
// baseline stays untouched and the root-cause analysis goes to the user first.
// The step-command business flags and the ExpectExpressions checks remain alongside
// the snapshots.
// =========================================================================

public sealed class Material_VolumeCloud : ITestCase
{
    public string Name => "Material Volume Cloud (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 83;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_VolumetricAdvancedOutput(ctx);
        BuildQ2_CloudSampleAttribute(ctx);
        BuildQ3_AbsorptionMediumOutput(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Const3 scattering -> VolumetricAdvancedMaterialOutput.ConservativeDensity
    // =====================================================================

    private static void BuildQ1_VolumetricAdvancedOutput(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Vol1_A";

        // Step 1: create material (Domain=Volume per the doc's DSL header)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Volume",
            blendMode = "Additive",   // engine rule: Volume requires BLEND_Additive
        }), "created", "create_material Mat_Vol1_A");

        // Step 2: add Const3(0.1,0.1,0.1) - the doc's scattering constant
        string scatId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.1,G=0.1,B=0.1)",
        }), "add MaterialExpressionConstant3Vector(0.1,0.1,0.1)");

        // Step 3: add VolumetricAdvancedMaterialOutput
        string volId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionVolumetricAdvancedMaterialOutput",
        }), "add MaterialExpressionVolumetricAdvancedMaterialOutput");

        // Step 4: wire the constant into ConservativeDensity (user ruling: the doc's
        // "Scattering" pin does not exist on this class). The output node itself is the
        // volume output - being in the graph is what "link -> volume output" means here.
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = scatId,
            fromPin = "",
            toExpression = volId,
            toPin = "ConservativeDensity",
        }), "connected", "connect Const3 -> VolumetricAdvanced.ConservativeDensity");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Vol1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Vol1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionVolumetricAdvancedMaterialOutput", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Vol1_B" }),
            "saved", "save_asset Mat_Vol1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Vol1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Vol1_B",
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionVolumetricAdvancedMaterialOutput", 1));
    }

    // =====================================================================
    // Q2: CloudSampleAttribute (the doc's "CloudLayer" - user ruling) -> BaseColor
    // =====================================================================

    private static void BuildQ2_CloudSampleAttribute(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Cloud1_A";

        // Step 1: create material (Domain=Volume)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Volume",
            blendMode = "Additive",   // engine rule: Volume requires BLEND_Additive
        }), "created", "create_material Mat_Cloud1_A");

        // Step 2: add MaterialExpressionCloudSampleAttribute. The doc names a
        // "CloudLayer" node with LayerName="CloudLayer0"; UE 5.0.3 has no such class -
        // the same header defines only CloudSampleAttribute (user ruling: build it;
        // it carries no LayerName property, so that key is dropped).
        string cloudId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionCloudSampleAttribute",
        }), "add MaterialExpressionCloudSampleAttribute");

        // Step 3: link its attribute output (default output 0 = Altitude) -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = cloudId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect CloudSampleAttribute -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Cloud1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Cloud1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // per-asset self-comparison
        ExpectExpressions(ctx, a2, ("MaterialExpressionCloudSampleAttribute", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Cloud1_B" }),
            "saved", "save_asset Mat_Cloud1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Cloud1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Cloud1_B", ("MaterialExpressionCloudSampleAttribute", 1));
    }

    // =====================================================================
    // Q3: AbsorptionMediumMaterialOutput (created unwired per the doc's steps)
    // =====================================================================

    private static void BuildQ3_AbsorptionMediumOutput(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Absorb1_A";

        // Step 1: create material (Domain=Volume)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Volume",
            blendMode = "Additive",   // engine rule: Volume requires BLEND_Additive
        }), "created", "create_material Mat_Absorb1_A");

        // Step 2: add AbsorptionMediumMaterialOutput. The doc names no input wires
        // (its parenthetical "absorption/scattering/emission" describes the node, not
        // a step), and as a custom output node its presence in the graph IS the volume
        // output - its only input (the optional TransmittanceColor) stays unwired.
        string absId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionAbsorptionMediumMaterialOutput",
        }), "add MaterialExpressionAbsorptionMediumMaterialOutput");
        _ = absId;

        // Step 3: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Absorb1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Absorb1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // per-asset self-comparison
        ExpectExpressions(ctx, a3, ("MaterialExpressionAbsorptionMediumMaterialOutput", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Absorb1_B" }),
            "saved", "save_asset Mat_Absorb1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Absorb1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Absorb1_B", ("MaterialExpressionAbsorptionMediumMaterialOutput", 1));
    }
}
