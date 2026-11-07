// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 106 - Material Math Functions (UE 5.0)
//        - from the UE 5.0 material task doc "material - math functions"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - mathematics functions", which cannot be written in source)
//
// UMaterialExpressionTime, UMaterialExpressionSine, UMaterialExpressionTextureSample,
// UMaterialExpressionScalarParameter, UMaterialExpressionPower, UMaterialExpressionClamp,
// UMaterialExpressionFrac, UMaterialExpressionOneMinus, UMaterialExpressionSaturate
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" -> 1 match each for all nine classes above.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//   * Engine header facts (the engine form wins over the doc wording):
//       - Power declares FExpressionInput Base / Exponent, plus float ConstExponent; the
//         doc's "Exp" is not a property, so Gamma is wired to the Exponent input.
//       - Clamp declares FExpressionInput Input / Min / Max, plus float MinDefault /
//         MaxDefault (MaterialExpressionClamp.h:25-41). The doc's literal 0/1 are numbers,
//         not wires, so they are written to MinDefault / MaxDefault, the same substitution
//         No. 103 makes for DepthFade's FadeDistanceDefault.
//       - Frac / OneMinus / Saturate / Sine each take the single FExpressionInput Input.
//   * Texture note: the doc names /Game/T_Noise, which does not exist in this project.
//     Following the No. 74 ruling, both channels use the StarterContent perlin noise
//     /Game/StarterContent/Textures/T_Perlin_Noise_M, the asset No. 76 and No. 103 use.
//   * Asset names Mat_Sin1_* / Mat_PowClamp_* / Mat_Math3_* occur in no existing case
//     or baseline. Question 1 builds the same graph as No. 105 question 1 (Sine(Time) ->
//     EmissiveColor) but under its own asset names, as the two docs each ask for it.
//
// No IR snapshot (VerifyIrSnapshot) is taken: per collaboration spec section 9 a
// transcribed case must not add one on its own. Coverage is the step commands' business
// flags, the read-back count of the named expressions, and the output/input link checks.
// =========================================================================

public sealed class Material_MathFunctions : ITestCase
{
    public string Name => "Material Math Functions (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 106;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_SineOscillator(ctx);
        BuildQ2_PowerClamp(ctx);
        BuildQ3_SaturateOneMinusFrac(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Sine(Time) -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_SineOscillator(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Sin1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Sin1_A");

        string timeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTime",
        }), "add MaterialExpressionTime");

        string sineId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionSine",
        }), "add MaterialExpressionSine");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = timeId,
            fromPin = "",
            toExpression = sineId,
            toPin = "Input",
        }), "connected", "connect Time -> Sine.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = sineId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Sine -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Sin1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Sin1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionTime", 1),
            ("MaterialExpressionSine", 1));
        ExpectOutputLink(ctx, a1, "EmissiveColor", "MaterialExpressionSine");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionSine", "Input");

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_Sin1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_Sin1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionTime", 1),
            ("MaterialExpressionSine", 1));
        ExpectOutputLink(ctx, b1, "EmissiveColor", "MaterialExpressionSine");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionSine", "Input");
    }

    // =====================================================================
    // Q2: Clamp(Power(Tex.R, Gamma), 0, 1) -> BaseColor
    // =====================================================================

    private static void BuildQ2_PowerClamp(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_PowClamp_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_PowClamp_A");

        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Perlin_Noise_M",
        }), "add MaterialExpressionTextureSample(T_Noise)");

        string gammaId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Gamma",
            defaultValue = 2.0,
        }), "add MaterialExpressionScalarParameter(Gamma)");

        string powerId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionPower",
        }), "add MaterialExpressionPower");

        string clampId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionClamp",
            MinDefault = 0.0,
            MaxDefault = 1.0,
        }), "add MaterialExpressionClamp(MinDefault=0,MaxDefault=1)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = texId,
            fromPin = "R",
            toExpression = powerId,
            toPin = "Base",
        }), "connected", "connect Tex.R -> Power.Base");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = gammaId,
            fromPin = "",
            toExpression = powerId,
            toPin = "Exponent",
        }), "connected", "connect Gamma -> Power.Exponent");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = powerId,
            fromPin = "",
            toExpression = clampId,
            toPin = "Input",
        }), "connected", "connect Power -> Clamp.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = clampId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Clamp -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_PowClamp_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_PowClamp_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionPower", 1),
            ("MaterialExpressionClamp", 1));
        ExpectOutputLink(ctx, a2, "BaseColor", "MaterialExpressionClamp");
        ExpectExpressionInputLinks(ctx, a2, "MaterialExpressionPower", "Base", "Exponent");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_PowClamp_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_PowClamp_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionPower", 1),
            ("MaterialExpressionClamp", 1));
        ExpectOutputLink(ctx, b2, "BaseColor", "MaterialExpressionClamp");
        ExpectExpressionInputLinks(ctx, b2, "MaterialExpressionPower", "Base", "Exponent");
    }

    // =====================================================================
    // Q3: Saturate(OneMinus(Frac(Time))) -> EmissiveColor
    // =====================================================================

    private static void BuildQ3_SaturateOneMinusFrac(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Math3_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Math3_A");

        string timeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionTime",
        }), "add MaterialExpressionTime");

        string fracId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionFrac",
        }), "add MaterialExpressionFrac");

        string oneMinusId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionOneMinus",
        }), "add MaterialExpressionOneMinus");

        string saturateId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSaturate",
        }), "add MaterialExpressionSaturate");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = timeId,
            fromPin = "",
            toExpression = fracId,
            toPin = "Input",
        }), "connected", "connect Time -> Frac.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = fracId,
            fromPin = "",
            toExpression = oneMinusId,
            toPin = "Input",
        }), "connected", "connect Frac -> OneMinus.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = oneMinusId,
            fromPin = "",
            toExpression = saturateId,
            toPin = "Input",
        }), "connected", "connect OneMinus -> Saturate.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = saturateId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Saturate -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Math3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Math3_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionTime", 1),
            ("MaterialExpressionFrac", 1),
            ("MaterialExpressionOneMinus", 1),
            ("MaterialExpressionSaturate", 1));
        ExpectOutputLink(ctx, a3, "EmissiveColor", "MaterialExpressionSaturate");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionFrac", "Input");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionOneMinus", "Input");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionSaturate", "Input");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_Math3_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_Math3_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionTime", 1),
            ("MaterialExpressionFrac", 1),
            ("MaterialExpressionOneMinus", 1),
            ("MaterialExpressionSaturate", 1));
        ExpectOutputLink(ctx, b3, "EmissiveColor", "MaterialExpressionSaturate");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionFrac", "Input");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionOneMinus", "Input");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionSaturate", "Input");
    }
}
