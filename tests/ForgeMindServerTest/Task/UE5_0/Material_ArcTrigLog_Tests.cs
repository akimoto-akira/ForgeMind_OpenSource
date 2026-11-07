// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 96 - Material Arc Trig And Log (UE 5.0)
//        - from the UE 5.0 material task doc "material - arc trig / logarithm /
//          tool math" (Tasks material group; the doc file name is the Chinese
//          phrase for that, which cannot be written in source)
//
// UMaterialExpressionSine, UMaterialExpressionArcsine, UMaterialExpressionArctangent2,
// UMaterialExpressionLogarithm2, UMaterialExpressionDesaturation, plus
// UMaterialExpressionScalarParameter, UMaterialExpressionTextureSample and
// UMaterialExpressionConstant (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on MaterialExpressionArcsine / Arccosine / Arctangent /
//     Arctangent2 / ArctangentFast / Logarithm2 / Logarithm10 / Desaturation / Distance /
//     Truncate -> 1 match each.
//   * Header facts that decide the shape of this case:
//       - Arcsine carries one input named Input (MaterialExpressionArcsine.h).
//       - Arctangent2 carries Y and X (MaterialExpressionArctangent2.h) - the doc's Y/X
//         wording matches the engine.
//       - Sine carries Input plus the float Period (MaterialExpressionSine.h); the doc does
//         not set a period, so none is sent and the engine default stands.
//       - ScalarParameter: DefaultValue on the class, ParameterName from
//         UMaterialExpressionParameter - written through the step command's name /
//         defaultValue fields.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 3 feeds "Logarithm2: Input=Tex.R", but the engine's Logarithm2 input is
//     named X (MaterialExpressionLogarithm2.h carries FExpressionInput X and no Input).
//     The wire is made against X on both channels; the doc's intent - log2 of the texture's
//     red channel - is unchanged.
//   * Question 3 also writes "Desaturation=0.8" as if the node carried a authored number.
//     UMaterialExpressionDesaturation has only the pins Input and Fraction plus the
//     FLinearColor LuminanceFactors (MaterialExpressionDesaturation.h) - there is no float
//     to set - so the 0.8 becomes a Constant node wired into the Fraction pin, which is
//     how the editor supplies a fixed fraction for this node.
//   * The doc's texture "/Game/T_Brick" does not exist in the project. The case uses the
//     project's own /Game/StarterContent/Textures/T_Brick_Clay_New_D, the same substitution
//     the earlier material cases made (an asset reference must resolve, and a null
//     texture would only surface later, at compile time).
//   * The doc lists Arccosine / Arctangent / ArctangentFast / Logarithm10 / Distance /
//     Truncate in its header but asks no question against them; they are left for the
//     doc's owner rather than invented into a question here.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// root output link, the per-node input wires of each question, plus this snapshot layer.
// =========================================================================

public sealed class Material_ArcTrigLog : ITestCase
{
    public string Name => "Material Arc Trig And Log (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 96;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_SineArcsineRoundTrip(ctx);
        BuildQ2_Arctangent2(ctx);
        BuildQ3_Logarithm2Desaturation(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: ScalarParameter V -> Sine -> Arcsine -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_SineArcsineRoundTrip(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_ASin1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ASin1_A");

        string vId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "V",
            defaultValue = 0.5,
        }), "add MaterialExpressionScalarParameter(V)");

        string sineId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionSine",
        }), "add MaterialExpressionSine");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = vId,
            fromPin = "",
            toExpression = sineId,
            toPin = "Input",
        }), "connected", "connect V -> Sine.Input");

        string asinId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionArcsine",
        }), "add MaterialExpressionArcsine");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = sineId,
            fromPin = "",
            toExpression = asinId,
            toPin = "Input",
        }), "connected", "connect Sine -> Arcsine.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = asinId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Arcsine -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_ASin1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_ASin1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionSine", 1),
            ("MaterialExpressionArcsine", 1));
        ExpectOutputLink(ctx, a1, "EmissiveColor", "MaterialExpressionArcsine");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionSine", "Input");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionArcsine", "Input");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        const string b1 = "/Game/Tests/UE5_0/Mat_ASin1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_ASin1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionSine", 1),
            ("MaterialExpressionArcsine", 1));
        ExpectOutputLink(ctx, b1, "EmissiveColor", "MaterialExpressionArcsine");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionSine", "Input");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionArcsine", "Input");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: ScalarParameter Y / X -> Arctangent2 -> EmissiveColor
    // =====================================================================

    private static void BuildQ2_Arctangent2(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_ATan2_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ATan2_A");

        string yId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Y",
            defaultValue = 1.0,
        }), "add MaterialExpressionScalarParameter(Y)");

        string xId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "X",
            defaultValue = 0.5,
        }), "add MaterialExpressionScalarParameter(X)");

        string atan2Id = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionArctangent2",
        }), "add MaterialExpressionArctangent2");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = yId,
            fromPin = "",
            toExpression = atan2Id,
            toPin = "Y",
        }), "connected", "connect Y -> Arctangent2.Y");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = xId,
            fromPin = "",
            toExpression = atan2Id,
            toPin = "X",
        }), "connected", "connect X -> Arctangent2.X");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = atan2Id,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Arctangent2 -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_ATan2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_ATan2_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionArctangent2", 1));
        ExpectOutputLink(ctx, a2, "EmissiveColor", "MaterialExpressionArctangent2");
        ExpectExpressionInputLinks(ctx, a2, "MaterialExpressionArctangent2", "Y", "X");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        const string b2 = "/Game/Tests/UE5_0/Mat_ATan2_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_ATan2_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionArctangent2", 1));
        ExpectOutputLink(ctx, b2, "EmissiveColor", "MaterialExpressionArctangent2");
        ExpectExpressionInputLinks(ctx, b2, "MaterialExpressionArctangent2", "Y", "X");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: Tex.R -> Logarithm2 -> Desaturation (Fraction = 0.8) -> BaseColor
    // =====================================================================

    private static void BuildQ3_Logarithm2Desaturation(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_LogDesat_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_LogDesat_A");

        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Brick_Clay_New_D.T_Brick_Clay_New_D",
        }), "add MaterialExpressionTextureSample(T_Brick_Clay_New_D)");

        string logId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionLogarithm2",
        }), "add MaterialExpressionLogarithm2");

        // The engine's Logarithm2 input pin is X, not the doc's "Input" (header note)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = texId,
            fromPin = "R",
            toExpression = logId,
            toPin = "X",
        }), "connected", "connect Tex.R -> Logarithm2.X");

        string desatId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDesaturation",
        }), "add MaterialExpressionDesaturation");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = logId,
            fromPin = "",
            toExpression = desatId,
            toPin = "Input",
        }), "connected", "connect Logarithm2 -> Desaturation.Input");

        // The doc's "Desaturation=0.8" has no authored property on this node, so the
        // fraction arrives as a Constant wired into the Fraction pin (header note)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            constant = "0.8",
            toExpression = desatId,
            toPin = "Fraction",
        }), "connected", "connect Constant(0.8) -> Desaturation.Fraction");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = desatId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Desaturation -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_LogDesat_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_LogDesat_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionLogarithm2", 1),
            ("MaterialExpressionDesaturation", 1));
        ExpectOutputLink(ctx, a3, "BaseColor", "MaterialExpressionDesaturation");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionLogarithm2", "X");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionDesaturation", "Input", "Fraction");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        const string b3 = "/Game/Tests/UE5_0/Mat_LogDesat_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_LogDesat_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionLogarithm2", 1),
            ("MaterialExpressionDesaturation", 1));
        ExpectOutputLink(ctx, b3, "BaseColor", "MaterialExpressionDesaturation");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionLogarithm2", "X");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionDesaturation", "Input", "Fraction");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
