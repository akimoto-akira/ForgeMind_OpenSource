// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 94 - Material Normal And Depth (UE 5.0)
//        - from the UE 5.0 material task doc "material - normal / depth"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - normal / depth", which cannot be written in source)
//
// UMaterialExpressionFresnel, UMaterialExpressionDepthFade, UMaterialExpressionDeriveNormalZ,
// UMaterialExpressionPixelNormalWS (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on Fresnel / DepthFade / DeriveNormalZ / PixelNormalWS /
//     Multiply / VectorParameter / Constant -> 1 match each.
//   * Header members (they override the doc where the wording differs):
//       - Fresnel: ExponentIn / Exponent(float) / BaseReflectFractionIn /
//         BaseReflectFraction(float) / Normal - the doc's two numbers are real property names.
//       - DepthFade: InOpacity, FadeDistance and FadeDistanceDefault, OpacityDefault. Note
//         "FadeDistance" is an FExpressionInput, so a plain number cannot be written to it;
//         the numeric form is FadeDistanceDefault - that is the key used here for 100.
//       - DeriveNormalZ: its only input is "InXY", not the doc's "Input".
//       - PixelNormalWS: no properties, one output.
//   * ForgeMind.SpkTool "translate" on the B-side DSL below -> 0 diagnostics.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 2 sends a fade distance number into what is actually a wire; the value lands on
//     the engine's own default property (FadeDistanceDefault) on both channels.
//   * Question 3 derives a normal from the pixel normal and routes the result to BaseColor.
//     Routing the raw PixelNormalWS straight to the Normal pin is the combination UE rejects
//     outright (HLSLMaterialTranslator errors while compiling MP_Normal - see the No.77 note),
//     and this question never touches that pin, so it compiles.
//   * Question 1 multiplies the Fresnel term by a rim colour parameter, matching the doc's
//     Mul(F, Rim) step: the multiplication is an explicit node on both channels.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// doc/engine divergences noted above, plus this snapshot layer.
// =========================================================================

public sealed class Material_NormalAndDepth : ITestCase
{
    public string Name => "Material Normal And Depth (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 94;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_FresnelRim(ctx);
        BuildQ2_DepthFadeOpacity(ctx);
        BuildQ3_DeriveNormalFromPixelNormal(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Fresnel(Exponent=3, BaseReflectFraction=0.04) * Rim -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_FresnelRim(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Fresnel1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Fresnel1_A");

        // Step 2: the Fresnel node with the doc's two numbers
        string fresnelId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionFresnel",
            Exponent = 3.0,
            BaseReflectFraction = 0.04,
        }), "add MaterialExpressionFresnel(Exponent=3, BaseReflectFraction=0.04)");

        // Step 3: the rim colour parameter and the multiply
        string rimId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionVectorParameter",
            name = "Rim",
            defaultValue = "1,0.5,0,1",
        }), "add MaterialExpressionVectorParameter(Rim)");

        string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = fresnelId,
            fromPin = "",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect Fresnel -> Multiply.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = rimId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect Rim -> Multiply.B");

        // Step 4: Multiply -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = mulId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Multiply -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Fresnel1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Fresnel1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionFresnel", 1),
            ("MaterialExpressionVectorParameter", 1),
            ("MaterialExpressionMultiply", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_Fresnel1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_Fresnel1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionFresnel", 1),
            ("MaterialExpressionVectorParameter", 1),
            ("MaterialExpressionMultiply", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: DepthFade (translucent soft particle) -> Opacity
    // =====================================================================

    private static void BuildQ2_DepthFadeOpacity(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_DepthFade1_A";

        // Step 1: create material in translucent mode (Opacity is only consumed then)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_DepthFade1_A");

        // Step 2: add DepthFade. The doc's FadeDistance=100 is a number, but the engine's
        // FadeDistance member is an input wire; the numeric slot is FadeDistanceDefault.
        string fadeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionDepthFade",
            FadeDistanceDefault = 100.0,
        }), "add MaterialExpressionDepthFade(FadeDistanceDefault=100)");

        // Step 3: link its output -> Opacity
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = fadeId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect DepthFade -> Material.Opacity");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_DepthFade1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_DepthFade1_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionDepthFade", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_DepthFade1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_DepthFade1_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionDepthFade", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: DeriveNormalZ(PixelNormalWS) -> BaseColor
    // =====================================================================

    private static void BuildQ3_DeriveNormalFromPixelNormal(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_PN1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_PN1_A");

        // Step 2: the pixel normal source
        string normalId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionPixelNormalWS",
        }), "add MaterialExpressionPixelNormalWS");

        // Step 3: DeriveNormalZ, whose only input the engine calls InXY
        string deriveId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDeriveNormalZ",
        }), "add MaterialExpressionDeriveNormalZ");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = normalId,
            fromPin = "",
            toExpression = deriveId,
            toPin = "InXY",
        }), "connected", "connect PixelNormalWS -> DeriveNormalZ.InXY");

        // Step 4: DeriveNormalZ -> BaseColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = deriveId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect DeriveNormalZ -> Material.BaseColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_PN1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_PN1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionPixelNormalWS", 1),
            ("MaterialExpressionDeriveNormalZ", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_PN1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_PN1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionPixelNormalWS", 1),
            ("MaterialExpressionDeriveNormalZ", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
