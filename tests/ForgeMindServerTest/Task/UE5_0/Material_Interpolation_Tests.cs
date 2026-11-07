// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 88 - Material Interpolation (UE 5.0)
//        - from the UE 5.0 material task doc "material - interpolation"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - interpolation", which cannot be written in source)
//
// UMaterialExpressionLinearInterpolate (UE 5.0.3 Materials/MaterialExpression
// LinearInterpolate.h): three FExpressionInput pins A / B / Alpha plus the
// ConstA / ConstB / ConstAlpha fallbacks. Per-pixel data flow, not control flow.
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on MaterialExpressionLinearInterpolate /
//     VectorParameter / ScalarParameter / Constant3Vector / Constant -> 1 match each.
//   * ForgeMind.SpkTool "translate" on all three B-side DSL below -> 0 diagnostics.
//   * Pin names A / B / Alpha are the engine UPROPERTY names, so the step command and
//     the DSL address the same pins; the doc's Const* fallbacks are not needed because
//     every pin is wired.
//
// Doc/engine divergences (written per the doc's step lists, in the documented DSL form):
//   * The doc's B side uses an attribute style ([ScalarParam], [Const3]) and a Lerp(a,b,t)
//     call - neither is part of the material DSL grammar document (requirements, Chinese
//     file name) nor lowered by MaterialTranslator.
//     Per the standing ruling (rewrite the case into the documented shape, do not extend the
//     translator) the B side uses [Parameter] fields plus new LinearInterpolate { A=.., B=..,
//     Alpha=.. } and a bare BaseColor = L; output assignment.
//   * A LinearColor parameter default is carried as the component text the bridge's value
//     writer parses ("1,0,0,1"), because a parenthesised tuple is not a C# expression the
//     DSL parser can read. The A side sends the same component form for the same reason, so
//     both channels hand the bridge identical text.
//   * Question 3 asks for three constants and a second Lerp whose A input is the first
//     Lerp's output - both channels build exactly that chain (L1 -> L2 -> EmissiveColor).
//
// Multi-output convention (user ruling, see Material_Decal_Tests.cs): Lerp has a single
// declared output, so every wire leaves it at the default index; no named-output form is
// needed in this case.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) inserted on user instruction per
// collaboration spec section 9: one call per built asset, right after that asset's save -
// A side after compile_material + save_asset, B side after ApplyDsl + save_asset (material
// assets are not saved by the DSL apply, so the save stays explicit). First run seeds the
// six baselines under TestRuns/5.0/, the second run has to report them all as a match.
// =========================================================================

public sealed class Material_Interpolation : ITestCase
{
    public string Name => "Material Interpolation (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 88;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_ParameterDrivenLerp(ctx);
        BuildQ2_ConstantLerp(ctx);
        BuildQ3_CascadedLerp(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: ColorA / ColorB parameters + T alpha -> Lerp -> BaseColor
    // =====================================================================

    private static void BuildQ1_ParameterDrivenLerp(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Lerp1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Lerp1_A");

        // Step 2: the two colour parameters
        string colorAId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionVectorParameter",
            name = "ColorA",
            defaultValue = "1,0,0,1",
        }), "add MaterialExpressionVectorParameter(ColorA)");

        string colorBId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionVectorParameter",
            name = "ColorB",
            defaultValue = "0,0,1,1",
        }), "add MaterialExpressionVectorParameter(ColorB)");

        // Step 3: the alpha parameter
        string tId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "T",
            defaultValue = 0.5,
        }), "add MaterialExpressionScalarParameter(T)");

        // Step 4: the Lerp itself
        string lerpId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionLinearInterpolate",
        }), "add MaterialExpressionLinearInterpolate");

        // Step 5: wire A / B / Alpha (engine pin names), then the Lerp -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = colorAId,
            fromPin = "",
            toExpression = lerpId,
            toPin = "A",
        }), "connected", "connect ColorA -> Lerp.A");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = colorBId,
            fromPin = "",
            toExpression = lerpId,
            toPin = "B",
        }), "connected", "connect ColorB -> Lerp.B");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = tId,
            fromPin = "",
            toExpression = lerpId,
            toPin = "Alpha",
        }), "connected", "connect T -> Lerp.Alpha");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = lerpId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Lerp -> Material.BaseColor");

        // Step 6: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Lerp1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Lerp1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionVectorParameter", 2),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionLinearInterpolate", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Lerp1_B" }),
            "saved", "save_asset Mat_Lerp1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Lerp1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Lerp1_B",
            ("MaterialExpressionVectorParameter", 2),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionLinearInterpolate", 1));
    }

    // =====================================================================
    // Q2: two constants + a scalar constant alpha -> Lerp -> BaseColor
    // =====================================================================

    private static void BuildQ2_ConstantLerp(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Lerp2_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Lerp2_A");

        // Step 2: the two colour constants
        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add MaterialExpressionConstant3Vector(1,0,0)");

        string blueId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=0,B=1)",
        }), "add MaterialExpressionConstant3Vector(0,0,1)");

        // Step 3: the alpha constant. MaterialExpressionConstant carries a single float
        // named R (MaterialExpressionConstant.h), so that is the key that has to land.
        string tId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant",
            R = 0.3,
        }), "add MaterialExpressionConstant(0.3)");

        // Step 4: the Lerp and its three inputs
        string lerpId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionLinearInterpolate",
        }), "add MaterialExpressionLinearInterpolate");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = redId,
            fromPin = "",
            toExpression = lerpId,
            toPin = "A",
        }), "connected", "connect Red -> Lerp.A");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = blueId,
            fromPin = "",
            toExpression = lerpId,
            toPin = "B",
        }), "connected", "connect Blue -> Lerp.B");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = tId,
            fromPin = "",
            toExpression = lerpId,
            toPin = "Alpha",
        }), "connected", "connect T -> Lerp.Alpha");

        // Step 5: Lerp -> BaseColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = lerpId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Lerp -> Material.BaseColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Lerp2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Lerp2_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionLinearInterpolate", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Lerp2_B" }),
            "saved", "save_asset Mat_Lerp2_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Lerp2_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Lerp2_B",
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionLinearInterpolate", 1));
    }

    // =====================================================================
    // Q3: cascaded Lerp (black -> red -> white) -> EmissiveColor
    // =====================================================================

    private static void BuildQ3_CascadedLerp(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Lerp3_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Lerp3_A");

        // Step 2: the shared alpha parameter
        string vId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "V",
            defaultValue = 0.5,
        }), "add MaterialExpressionScalarParameter(V)");

        // Step 3: three colour constants and the first Lerp
        string blackId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=0,B=0)",
        }), "add MaterialExpressionConstant3Vector(0,0,0)");

        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add MaterialExpressionConstant3Vector(1,0,0)");

        string whiteId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=1,B=1)",
        }), "add MaterialExpressionConstant3Vector(1,1,1)");

        string lerp1Id = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionLinearInterpolate",
        }), "add MaterialExpressionLinearInterpolate (Lerp_1)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = blackId,
            fromPin = "",
            toExpression = lerp1Id,
            toPin = "A",
        }), "connected", "connect Black -> Lerp_1.A");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = redId,
            fromPin = "",
            toExpression = lerp1Id,
            toPin = "B",
        }), "connected", "connect Red -> Lerp_1.B");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = vId,
            fromPin = "",
            toExpression = lerp1Id,
            toPin = "Alpha",
        }), "connected", "connect V -> Lerp_1.Alpha");

        // Step 4: the second Lerp takes Lerp_1's output as its A input
        string lerp2Id = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionLinearInterpolate",
        }), "add MaterialExpressionLinearInterpolate (Lerp_2)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = lerp1Id,
            fromPin = "",
            toExpression = lerp2Id,
            toPin = "A",
        }), "connected", "connect Lerp_1 -> Lerp_2.A");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = whiteId,
            fromPin = "",
            toExpression = lerp2Id,
            toPin = "B",
        }), "connected", "connect White -> Lerp_2.B");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = vId,
            fromPin = "",
            toExpression = lerp2Id,
            toPin = "Alpha",
        }), "connected", "connect V -> Lerp_2.Alpha");

        // Step 5: Lerp_2 -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = lerp2Id,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Lerp_2 -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Lerp3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Lerp3_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionConstant3Vector", 3),
            ("MaterialExpressionLinearInterpolate", 2));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Lerp3_B" }),
            "saved", "save_asset Mat_Lerp3_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Lerp3_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Lerp3_B",
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionConstant3Vector", 3),
            ("MaterialExpressionLinearInterpolate", 2));
    }
}
