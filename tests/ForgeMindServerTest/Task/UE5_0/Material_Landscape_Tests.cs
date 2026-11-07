// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 92 - Material Landscape (UE 5.0)
//        - from the UE 5.0 material task doc "material - landscape"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - landscape", which cannot be written in source)
//
// UMaterialExpressionLandscapeLayerBlend, UMaterialExpressionLandscapeLayerCoords and
// UMaterialExpressionLandscapeVisibilityMask. They are NOT under
// Runtime/Engine/Classes/Materials: the headers live in
// Runtime/Landscape/Classes/Materials/MaterialExpressionLandscape*.h (found by an engine-wide
// file search, which is also why a Materials/-only lookup reports them missing).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on LandscapeLayerBlend / LandscapeLayerCoords /
//     LandscapeVisibilityMask / Constant3Vector -> 1 match each.
//   * Header facts: LandscapeLayerBlend carries a dynamic "Layers" array of FLayerBlendInput
//     { LayerName (FName), BlendType, LayerInput (FExpressionInput), HeightInput, PreviewWeight };
//     LandscapeLayerCoords carries MappingType (TEnumAsByte<ETerrainCoordMappingType>),
//     CustomUVType, MappingScale, MappingRotation, MappingPanU, MappingPanV; LandscapeVisibilityMask
//     carries no properties.
//   * Bridge capability used here (added together with this case): GetExpressionInput resolves a
//     LandscapeLayerBlend pin through its Layers array by LayerName, appending a row that carries
//     that LayerName when the name is new - the same rule the editor's details panel applies, and
//     the same shape as the Custom node's InputName lookup. Because both channels go through that
//     one resolution, the B side reaches the same multi-layer shape by naming the layer as a pin in
//     the node initializer ("{ Grass = x, Rock = y }"), which the grammar already allows for input
//     pins - no layer-list syntax is needed, and the layer rows get the same LayerName on both sides.
//     The initializer can only address input pins: a node-level scalar such as a row's BlendType
//     emits as a wire with a null source (SpkTool reports one diagnostic for it), so per-row blend
//     types stay at their engine default on both channels.
//   * ForgeMind.SpkTool "translate" on the B-side DSL below -> 0 diagnostics.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 1 asks for "Layer0 = the upper layer, Layer1 = the lower layer" (the doc's
//     wording, in Chinese) without naming them; the DSL half of the
//     question does name them Grass and Rock, so the two layer rows are created under exactly
//     those names (the engine keys a layer by its LayerName) and the constants feed them on both
//     sides. The layer variables are named GrassC / RockC on the B side because a node variable
//     cannot share its name with a pin it assigns in the same initializer.
//   * Question 2's MappingType="XY" has no counterpart: the engine's ETerrainCoordMappingType
//     values are the auto / world / local family, not an "XY" entry. The key is therefore not
//     sent (writing it would either fail or hit an unrelated property); the node keeps its
//     default mapping and its output still drives BaseColor as the doc asks.
//   * Question 3 pairs Opacity with Translucent, which is the combination the engine consumes.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// per-layer wire check added for question 1, plus this snapshot layer.
// =========================================================================

public sealed class Material_Landscape : ITestCase
{
    public string Name => "Material Landscape (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 92;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_LayerBlend(ctx);
        BuildQ2_LayerCoords(ctx);
        BuildQ3_VisibilityMask(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: two constants -> LandscapeLayerBlend (Grass / Rock) -> BaseColor
    // =====================================================================

    private static void BuildQ1_LayerBlend(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Land1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Land1_A");

        // Step 2: the two layer colours
        string grassId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.4,G=0.3,B=0.2)",
        }), "add MaterialExpressionConstant3Vector(0.4,0.3,0.2)");

        string rockId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.1,G=0.4,B=0.1)",
        }), "add MaterialExpressionConstant3Vector(0.1,0.4,0.1)");

        // Step 3: the blend node
        string blendId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionLandscapeLayerBlend",
        }), "add MaterialExpressionLandscapeLayerBlend");

        // Step 4: one row per layer, addressed by the layer name the doc's DSL gives them
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = grassId,
            fromPin = "",
            toExpression = blendId,
            toPin = "Grass",
        }), "connected", "connect Grass -> LandscapeLayerBlend layer Grass");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = rockId,
            fromPin = "",
            toExpression = blendId,
            toPin = "Rock",
        }), "connected", "connect Rock -> LandscapeLayerBlend layer Rock");

        // Step 5: blend output -> BaseColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = blendId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect LandscapeLayerBlend -> Material.BaseColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Land1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Land1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionLandscapeLayerBlend", 1),
            ("MaterialExpressionConstant3Vector", 2));
        // The blend node's output must reach BaseColor, and both layer rows must be fed - the layer
        // wires are what makes this a multi-layer blend, a node count alone cannot tell them apart
        ExpectOutputLink(ctx, a1, "BaseColor", "MaterialExpressionLandscapeLayerBlend");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionLandscapeLayerBlend", "Grass", "Rock");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        // A layer is addressed by its name as a pin (the bridge resolves and appends the Layers row
        // keyed by that LayerName), so the B side builds the same two-layer blend.
        const string b1 = "/Game/Tests/UE5_0/Mat_Land1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_Land1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionLandscapeLayerBlend", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, b1, "BaseColor", "MaterialExpressionLandscapeLayerBlend");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionLandscapeLayerBlend", "Grass", "Rock");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: LandscapeLayerCoords -> BaseColor
    // =====================================================================

    private static void BuildQ2_LayerCoords(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Land2_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Land2_A");

        // Step 2: add LandscapeLayerCoords. The doc's MappingType="XY" is not one of the
        // engine's ETerrainCoordMappingType entries, so no such key is sent and the node keeps
        // its default mapping (header note).
        string coordsId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionLandscapeLayerCoords",
        }), "add MaterialExpressionLandscapeLayerCoords");

        // Step 3: link -> BaseColor (the doc wires the sample result to BaseColor for the
        // sake of the example, in Chinese wording)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = coordsId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect LandscapeLayerCoords -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Land2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Land2_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionLandscapeLayerCoords", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_Land2_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_Land2_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionLandscapeLayerCoords", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: LandscapeVisibilityMask -> Opacity
    // =====================================================================

    private static void BuildQ3_VisibilityMask(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Land3_A";

        // Step 1: create material (Translucent so Opacity is consumed)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Land3_A");

        // Step 2: add LandscapeVisibilityMask
        string maskId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionLandscapeVisibilityMask",
        }), "add MaterialExpressionLandscapeVisibilityMask");

        // Step 3: link -> Opacity, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = maskId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect LandscapeVisibilityMask -> Material.Opacity");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Land3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Land3_A");
        ExpectExpressions(ctx, a3, ("MaterialExpressionLandscapeVisibilityMask", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_Land3_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_Land3_B");
        ExpectExpressions(ctx, b3, ("MaterialExpressionLandscapeVisibilityMask", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
