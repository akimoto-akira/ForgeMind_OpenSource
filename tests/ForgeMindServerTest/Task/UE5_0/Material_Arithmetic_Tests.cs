// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 110 - Material Arithmetic (UE 5.0)
//        - from the UE 5.0 material task doc "material - arithmetic operations"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - arithmetic operations", which cannot be written in source)
//
// UMaterialExpressionAdd, UMaterialExpressionSubtract, UMaterialExpressionMultiply,
// UMaterialExpressionDivide, UMaterialExpressionConstant, UMaterialExpressionConstant3Vector,
// UMaterialExpressionTextureSample, UMaterialExpressionScalarParameter
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" -> 1 match each for all eight classes above.
//   * ForgeMind.SpkTool "translate" on the four B-side DSL blocks below -> 0 diagnostics;
//     the IR carries the four binary nodes with A / B inputs, and the TextureSample
//     RGBA named output resolves to output index 5 (not a channel swizzle).
//   * Header facts: Add / Subtract / Multiply / Divide derive from UMaterialExpressionBinaryOp
//     with the two FExpressionInputs A / B; the DSL spells them with their engine class names
//     (the doc's "Mul" / "Sub" / "Div" are shorthand, and the doc's [Const3(...)] attribute
//     form is not the material DSL surface - see the No. 75 note on this design).
//   * Texture note: the doc names /Game/T_Brick, /Game/T_A and /Game/T_B, none of which exist
//     in this project. Following the No. 74 ruling, both channels use StarterContent assets:
//     T_Brick -> /Game/StarterContent/Textures/T_Brick_Clay_New_D (No. 76 / No. 98),
//     T_A -> /Game/StarterContent/Textures/T_Rock_Basalt_D and
//     T_B -> /Game/StarterContent/Textures/T_Ground_Grass_D (No. 74 question 3).
//   * Asset names Mat_Add1_* / Mat_Mul1_* / Mat_SubDiv_* / Mat_Nest_* occur in no existing
//     case or baseline (the No. 79 case uses Mat_NestFn_*, a different pair).
//
// No IR snapshot (VerifyIrSnapshot) is taken: per collaboration spec section 9 a
// transcribed case must not add one on its own. Coverage is the step commands' business
// flags, the read-back count of the named expressions, and the output/input link checks.
// =========================================================================

public sealed class Material_Arithmetic : ITestCase
{
    public string Name => "Material Arithmetic (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 110;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_AddTwoConstants(ctx);
        BuildQ2_MultiplyTextureByScalar(ctx);
        BuildQ3_SubtractThenDivide(ctx);
        BuildQ4_NestedArithmetic(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Add(two Constant3Vector) -> BaseColor
    // =====================================================================

    private static void BuildQ1_AddTwoConstants(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Add1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Add1_A");

        string firstId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.2,G=0.2,B=0.2)",
        }), "add Constant3Vector(0.2,0.2,0.2)");

        string secondId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.3,G=0.3,B=0.3)",
        }), "add Constant3Vector(0.3,0.3,0.3)");

        string addId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionAdd",
        }), "add MaterialExpressionAdd");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = firstId,
            fromPin = "",
            toExpression = addId,
            toPin = "A",
        }), "connected", "connect first -> Add.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = secondId,
            fromPin = "",
            toExpression = addId,
            toPin = "B",
        }), "connected", "connect second -> Add.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = addId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Add -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Add1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Add1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionAdd", 1));
        ExpectOutputLink(ctx, a1, "BaseColor", "MaterialExpressionAdd");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionAdd", "A", "B");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_Add1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_Add1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionAdd", 1));
        ExpectOutputLink(ctx, b1, "BaseColor", "MaterialExpressionAdd");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionAdd", "A", "B");
    }

    // =====================================================================
    // Q2: Multiply(Tex.RGBA, Scale) -> EmissiveColor
    // =====================================================================

    private static void BuildQ2_MultiplyTextureByScalar(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Mul1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Mul1_A");

        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Brick_Clay_New_D",
        }), "add MaterialExpressionTextureSample(T_Brick)");

        string scaleId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Scale",
            defaultValue = 2.0,
        }), "add MaterialExpressionScalarParameter(Scale)");

        string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = texId,
            fromPin = "RGBA",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect Tex.RGBA -> Multiply.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = scaleId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect Scale -> Multiply.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = mulId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Multiply -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Mul1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Mul1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionMultiply", 1));
        ExpectOutputLink(ctx, a2, "EmissiveColor", "MaterialExpressionMultiply");
        ExpectExpressionInputLinks(ctx, a2, "MaterialExpressionMultiply", "A", "B");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_Mul1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_Mul1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionMultiply", 1));
        ExpectOutputLink(ctx, b2, "EmissiveColor", "MaterialExpressionMultiply");
        ExpectExpressionInputLinks(ctx, b2, "MaterialExpressionMultiply", "A", "B");
    }

    // =====================================================================
    // Q3: Divide(Subtract(A, B), 2.0) -> Roughness
    // =====================================================================

    private static void BuildQ3_SubtractThenDivide(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_SubDiv_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SubDiv_A");

        string aId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "A",
            defaultValue = 1.0,
        }), "add MaterialExpressionScalarParameter(A)");

        string bId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "B",
            defaultValue = 0.3,
        }), "add MaterialExpressionScalarParameter(B)");

        string twoId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant",
            R = 2.0,
        }), "add Constant(2.0)");

        string subId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSubtract",
        }), "add MaterialExpressionSubtract");

        string divId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDivide",
        }), "add MaterialExpressionDivide");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = aId,
            fromPin = "",
            toExpression = subId,
            toPin = "A",
        }), "connected", "connect A -> Subtract.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = bId,
            fromPin = "",
            toExpression = subId,
            toPin = "B",
        }), "connected", "connect B -> Subtract.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = subId,
            fromPin = "",
            toExpression = divId,
            toPin = "A",
        }), "connected", "connect Subtract -> Divide.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = twoId,
            fromPin = "",
            toExpression = divId,
            toPin = "B",
        }), "connected", "connect Two -> Divide.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = divId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Roughness",
        }), "connected", "connect Divide -> Material.Roughness");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_SubDiv_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_SubDiv_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionSubtract", 1),
            ("MaterialExpressionDivide", 1));
        ExpectOutputLink(ctx, a3, "Roughness", "MaterialExpressionDivide");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionSubtract", "A", "B");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionDivide", "A", "B");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_SubDiv_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_SubDiv_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionSubtract", 1),
            ("MaterialExpressionDivide", 1));
        ExpectOutputLink(ctx, b3, "Roughness", "MaterialExpressionDivide");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionSubtract", "A", "B");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionDivide", "A", "B");
    }

    // =====================================================================
    // Q4: Add(Multiply(TexA, K1), Multiply(TexB, K2)) -> BaseColor
    // =====================================================================

    private static void BuildQ4_NestedArithmetic(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a4 = "/Game/Tests/UE5_0/Mat_Nest_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a4,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Nest_A");

        string texAId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Rock_Basalt_D",
        }), "add MaterialExpressionTextureSample(TexA)");

        string texBId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Ground_Grass_D",
        }), "add MaterialExpressionTextureSample(TexB)");

        string k1Id = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "K1",
            defaultValue = 0.3,
        }), "add MaterialExpressionScalarParameter(K1)");

        string k2Id = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "K2",
            defaultValue = 0.7,
        }), "add MaterialExpressionScalarParameter(K2)");

        string mul1Id = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply(M1)");

        string mul2Id = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply(M2)");

        string addId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionAdd",
        }), "add MaterialExpressionAdd");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = texAId,
            fromPin = "RGBA",
            toExpression = mul1Id,
            toPin = "A",
        }), "connected", "connect TexA.RGBA -> M1.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = k1Id,
            fromPin = "",
            toExpression = mul1Id,
            toPin = "B",
        }), "connected", "connect K1 -> M1.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = texBId,
            fromPin = "RGBA",
            toExpression = mul2Id,
            toPin = "A",
        }), "connected", "connect TexB.RGBA -> M2.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = k2Id,
            fromPin = "",
            toExpression = mul2Id,
            toPin = "B",
        }), "connected", "connect K2 -> M2.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = mul1Id,
            fromPin = "",
            toExpression = addId,
            toPin = "A",
        }), "connected", "connect M1 -> Add.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = mul2Id,
            fromPin = "",
            toExpression = addId,
            toPin = "B",
        }), "connected", "connect M2 -> Add.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = addId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Add -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a4 }),
            "compiled", "compile_material Mat_Nest_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a4 }),
            "saved", "save_asset Mat_Nest_A");
        ExpectExpressions(ctx, a4,
            ("MaterialExpressionTextureSample", 2),
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionMultiply", 2),
            ("MaterialExpressionAdd", 1));
        ExpectOutputLink(ctx, a4, "BaseColor", "MaterialExpressionAdd");
        ExpectExpressionInputLinks(ctx, a4, "MaterialExpressionAdd", "A", "B");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b4 = "/Game/Tests/UE5_0/Mat_Nest_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b4 }),
            "saved", "save_asset Mat_Nest_B");
        ExpectExpressions(ctx, b4,
            ("MaterialExpressionTextureSample", 2),
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionMultiply", 2),
            ("MaterialExpressionAdd", 1));
        ExpectOutputLink(ctx, b4, "BaseColor", "MaterialExpressionAdd");
        ExpectExpressionInputLinks(ctx, b4, "MaterialExpressionAdd", "A", "B");
    }
}
