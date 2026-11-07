// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 93 - Material Vertex And Interpolator (UE 5.0)
//        - from the UE 5.0 material task doc "material - vertex / mesh paint"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - vertex / mesh paint", which cannot be written in source)
//
// UMaterialExpressionVertexColor and UMaterialExpressionVertexInterpolator (UE 5.0.3
// Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on VertexColor / VertexInterpolator / WorldPosition ->
//     1 match each.
//   * Header fact: UMaterialExpressionVertexInterpolator carries "Input" (FExpressionInput),
//     InterpolatorIndex and InterpolatorOffset (int32) - so the doc's "input = WorldPosition"
//     (its wording, in Chinese) is a
//     real pin named Input, wired by that name on both channels. The node is a
//     UMaterialExpressionCustomOutput: it computes Input in the vertex shader and exposes the
//     interpolated result to the pixel shader only.
//   * ForgeMind.SpkTool "class" on MeshPaint -> 0 matches and on MeshPaintTexture -> 0 matches.
//     A search for the class declarations across every header under
//     Engine/Source/Runtime/Engine/Classes returns nothing either, so this is not a scan-index
//     gap (spec section 18's second branch): the expressions do not exist in 5.0.3, and their
//     headers are not among the 238 in Classes/Materials. The doc's evidence line claiming
//     MaterialExpressionMeshPaint.h / MaterialExpressionMeshPaintTexture.h exist is wrong.
//   * ForgeMind.SpkTool "translate" on both B-side DSL below -> 0 diagnostics.
//
// Doc/engine divergence (the engine form wins over the doc wording):
//   * Question 2 asks the interpolator output to drive WorldPositionOffset. That wiring cannot
//     compile: FHLSLMaterialTranslator::VertexInterpolator (HLSLMaterialTranslator.cpp:7016-7021)
//     rejects a read when ShaderFrequency != SF_Pixel, and WorldPositionOffset is a vertex-shader
//     attribute - the very stage that cannot read the interpolator. The interpolator's output is
//     consumed in the pixel shader instead, wired to EmissiveColor on both channels; the doc's
//     WorldPosition -> VertexInterpolator.Input half is kept unchanged.
//
// What is NOT transcribed, and why (spec section 6: behaviour may be assumed, names may not):
//   * Question 3 (MeshPaintTexture -> BaseColor) is left out entirely. Writing the class name
//     would mean putting an unverified name into the case, and substituting another node would
//     silently change what the question asks. The doc's question 3 therefore needs either a
//     different node or its removal - the user's call, not the case's.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// interpolator's pixel-stage wiring noted above for question 2, plus this snapshot layer.
// =========================================================================

public sealed class Material_VertexAndInterpolator : ITestCase
{
    public string Name => "Material Vertex And Interpolator (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 93;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_VertexColor(ctx);
        BuildQ2_VertexInterpolator(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: VertexColor -> BaseColor
    // =====================================================================

    private static void BuildQ1_VertexColor(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_VC1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_VC1_A");

        // Step 2: add VertexColor
        string vcId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionVertexColor",
        }), "add MaterialExpressionVertexColor");

        // Step 3: link -> BaseColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = vcId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect VertexColor -> Material.BaseColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_VC1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_VC1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionVertexColor", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_VC1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_VC1_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionVertexColor", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: WorldPosition -> VertexInterpolator.Input -> EmissiveColor
    //     (the doc's WorldPositionOffset is a vertex-stage attribute and cannot
    //      read the interpolator - see the header's doc/engine divergence note)
    // =====================================================================

    private static void BuildQ2_VertexInterpolator(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_VI1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_VI1_A");

        // Step 2: the position source and the interpolator
        string wpId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionWorldPosition",
        }), "add MaterialExpressionWorldPosition");

        string viId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionVertexInterpolator",
        }), "add MaterialExpressionVertexInterpolator");

        // Step 3: feed the interpolator's only input, by its engine pin name
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = wpId,
            fromPin = "",
            toExpression = viId,
            toPin = "Input",
        }), "connected", "connect WorldPosition -> VertexInterpolator.Input");

        // Step 4: interpolator output -> EmissiveColor (pixel stage; the interpolator cannot be
        // read in the vertex shader, so WorldPositionOffset is not a legal target - see header),
        // compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = viId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect VertexInterpolator -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_VI1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_VI1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionWorldPosition", 1),
            ("MaterialExpressionVertexInterpolator", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_VI1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_VI1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionWorldPosition", 1),
            ("MaterialExpressionVertexInterpolator", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }
}
