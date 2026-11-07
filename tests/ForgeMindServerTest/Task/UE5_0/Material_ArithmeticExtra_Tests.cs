// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 109 - Material Arithmetic Extra (UE 5.0)
//        - from the UE 5.0 material task doc "material - arithmetic supplement"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - arithmetic supplement", which cannot be written in source)
//
// UMaterialExpressionMax, UMaterialExpressionMin, UMaterialExpressionTextureSample,
// UMaterialExpressionConstant, UMaterialExpressionTextureCoordinate,
// UMaterialExpressionConstant2Vector, UMaterialExpressionFmod, UMaterialExpressionFrac,
// UMaterialExpressionScalarParameter, UMaterialExpressionAbs, UMaterialExpressionSquareRoot
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" -> 1 match each for all eleven classes above. The doc's
//     "Modulo" is not a class: the engine's real node is MaterialExpressionFmod, whose
//     inputs are A / B (MaterialExpressionFmod.h:17-21). Per spec section 8 the case
//     uses the name that was actually found.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics;
//     the IR carries MaterialExpressionFmod and the RGBA named output at index 5.
//   * Header facts: Max / Min / Fmod are binary with A / B inputs; Frac / Abs /
//     SquareRoot take the single Input; Constant2Vector carries the two components as a
//     node-level constant; TextureCoordinate's index property is CoordinateIndex (the
//     same property No. 90 question 3 writes).
//   * Texture note: the doc names /Game/T_Noise, which does not exist in this project.
//     Following the No. 74 ruling, both channels use the StarterContent perlin noise
//     /Game/StarterContent/Textures/T_Perlin_Noise_M, the asset No. 76 and No. 103 use.
//   * Asset names Mat_MaxMin1_* / Mat_ModFrac1_* / Mat_AbsSqrt1_* occur in no existing
//     case or baseline.
//
// No IR snapshot (VerifyIrSnapshot) is taken: per collaboration spec section 9 a
// transcribed case must not add one on its own. Coverage is the step commands' business
// flags, the read-back count of the named expressions, and the output/input link checks.
// =========================================================================

public sealed class Material_ArithmeticExtra : ITestCase
{
    public string Name => "Material Arithmetic Extra (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 109;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_MaxMin(ctx);
        BuildQ2_FmodFrac(ctx);
        BuildQ3_AbsSquareRoot(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Min(Max(Tex.R, 0.3), 0.8) -> BaseColor
    // =====================================================================

    private static void BuildQ1_MaxMin(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_MaxMin1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_MaxMin1_A");

        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Perlin_Noise_M",
        }), "add MaterialExpressionTextureSample(T_Noise)");

        string loId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant",
            R = 0.3,
        }), "add Constant(0.3)");

        string hiId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant",
            R = 0.8,
        }), "add Constant(0.8)");

        string maxId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionMax",
        }), "add MaterialExpressionMax");

        string minId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionMin",
        }), "add MaterialExpressionMin");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = texId,
            fromPin = "R",
            toExpression = maxId,
            toPin = "A",
        }), "connected", "connect Tex.R -> Max.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = loId,
            fromPin = "",
            toExpression = maxId,
            toPin = "B",
        }), "connected", "connect Lo -> Max.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = maxId,
            fromPin = "",
            toExpression = minId,
            toPin = "A",
        }), "connected", "connect Max -> Min.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = hiId,
            fromPin = "",
            toExpression = minId,
            toPin = "B",
        }), "connected", "connect Hi -> Min.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = minId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Min -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_MaxMin1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_MaxMin1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionConstant", 2),
            ("MaterialExpressionMax", 1),
            ("MaterialExpressionMin", 1));
        ExpectOutputLink(ctx, a1, "BaseColor", "MaterialExpressionMin");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionMax", "A", "B");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionMin", "A", "B");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_MaxMin1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_MaxMin1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionConstant", 2),
            ("MaterialExpressionMax", 1),
            ("MaterialExpressionMin", 1));
        ExpectOutputLink(ctx, b1, "BaseColor", "MaterialExpressionMin");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionMax", "A", "B");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionMin", "A", "B");
    }

    // =====================================================================
    // Q2: Frac(Fmod(UV, (2,2))) -> BaseColor
    // =====================================================================

    private static void BuildQ2_FmodFrac(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_ModFrac1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ModFrac1_A");

        string uvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionTextureCoordinate",
            CoordinateIndex = 0,
        }), "add MaterialExpressionTextureCoordinate(CoordinateIndex=0)");

        string periodId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant2Vector",
            constant = "(X=2,Y=2)",
        }), "add Constant2Vector(2,2)");

        string fmodId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionFmod",
        }), "add MaterialExpressionFmod");

        string fracId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionFrac",
        }), "add MaterialExpressionFrac");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = uvId,
            fromPin = "",
            toExpression = fmodId,
            toPin = "A",
        }), "connected", "connect UV -> Fmod.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = periodId,
            fromPin = "",
            toExpression = fmodId,
            toPin = "B",
        }), "connected", "connect Period -> Fmod.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = fmodId,
            fromPin = "",
            toExpression = fracId,
            toPin = "Input",
        }), "connected", "connect Fmod -> Frac.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = fracId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Frac -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_ModFrac1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_ModFrac1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionTextureCoordinate", 1),
            ("MaterialExpressionConstant2Vector", 1),
            ("MaterialExpressionFmod", 1),
            ("MaterialExpressionFrac", 1));
        ExpectOutputLink(ctx, a2, "BaseColor", "MaterialExpressionFrac");
        ExpectExpressionInputLinks(ctx, a2, "MaterialExpressionFmod", "A", "B");
        ExpectExpressionInputLinks(ctx, a2, "MaterialExpressionFrac", "Input");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_ModFrac1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_ModFrac1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionTextureCoordinate", 1),
            ("MaterialExpressionConstant2Vector", 1),
            ("MaterialExpressionFmod", 1),
            ("MaterialExpressionFrac", 1));
        ExpectOutputLink(ctx, b2, "BaseColor", "MaterialExpressionFrac");
        ExpectExpressionInputLinks(ctx, b2, "MaterialExpressionFmod", "A", "B");
        ExpectExpressionInputLinks(ctx, b2, "MaterialExpressionFrac", "Input");
    }

    // =====================================================================
    // Q3: SquareRoot(Abs(V)) -> EmissiveColor
    // =====================================================================

    private static void BuildQ3_AbsSquareRoot(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_AbsSqrt1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_AbsSqrt1_A");

        string vId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "V",
            defaultValue = -0.25,
        }), "add MaterialExpressionScalarParameter(V)");

        string absId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionAbs",
        }), "add MaterialExpressionAbs");

        string sqrtId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSquareRoot",
        }), "add MaterialExpressionSquareRoot");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = vId,
            fromPin = "",
            toExpression = absId,
            toPin = "Input",
        }), "connected", "connect V -> Abs.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = absId,
            fromPin = "",
            toExpression = sqrtId,
            toPin = "Input",
        }), "connected", "connect Abs -> SquareRoot.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = sqrtId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect SquareRoot -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_AbsSqrt1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_AbsSqrt1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionAbs", 1),
            ("MaterialExpressionSquareRoot", 1));
        ExpectOutputLink(ctx, a3, "EmissiveColor", "MaterialExpressionSquareRoot");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionAbs", "Input");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionSquareRoot", "Input");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_AbsSqrt1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_AbsSqrt1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionAbs", 1),
            ("MaterialExpressionSquareRoot", 1));
        ExpectOutputLink(ctx, b3, "EmissiveColor", "MaterialExpressionSquareRoot");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionAbs", "Input");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionSquareRoot", "Input");
    }
}
