// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 99 - Material Distance Field (UE 5.0)
//        - from the UE 5.0 material task doc "material - distance field" (Tasks
//          material group; the doc file name is the Chinese phrase for that,
//          which cannot be written in source)
//
// UMaterialExpressionDistanceToNearestSurface, UMaterialExpressionDistanceFieldGradient and
// UMaterialExpressionDistanceFieldsRenderingSwitch, with UMaterialExpressionConstant3Vector
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on MaterialExpressionDistanceToNearestSurface /
//     DistanceFieldGradient / DistanceFieldsRenderingSwitch / Constant3Vector -> 1 match each.
//   * Header facts that decide the shape of this case:
//       - DistanceToNearestSurface and DistanceFieldGradient each carry the single optional
//         input Position and one output (MaterialExpressionDistanceToNearestSurface.h,
//         MaterialExpressionDistanceFieldGradient.h). The doc wires no position, so none is
//         sent and the node samples at the current pixel position.
//       - DistanceFieldsRenderingSwitch carries the inputs No and Yes and one output
//         (MaterialExpressionDistanceFieldsRenderingSwitch.h) - the engine's names for the
//         two paths, see the divergence note below.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 3 speaks of the pins "DistanceField" and "Standard". The node has neither: its
//     two inputs are named Yes (rendering with distance fields) and No (the standard path), so
//     the doc's DistanceField colour goes to Yes and its Standard colour to No. The branch the
//     question asks for is unchanged.
//   * The doc notes question 1 needs distance fields enabled. That is a project/scene setting
//     outside the material asset, so nothing here turns it on; the question verifies the node is
//     built and the material compiles with it feeding EmissiveColor.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// For question 3 that parity is the whole point: the two wires sit on pins the engine calls Yes
// and No, not the doc's DistanceField / Standard, so the snapshot records which pin really got fed.
// =========================================================================

public sealed class Material_DistanceField : ITestCase
{
    public string Name => "Material Distance Field (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 99;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_DistanceToNearestSurface(ctx);
        BuildQ2_DistanceFieldGradient(ctx);
        BuildQ3_DistanceFieldsRenderingSwitch(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: DistanceToNearestSurface -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_DistanceToNearestSurface(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_DF1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_DF1_A");

        string distId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionDistanceToNearestSurface",
        }), "add MaterialExpressionDistanceToNearestSurface");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = distId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect DistanceToNearestSurface -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_DF1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_DF1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionDistanceToNearestSurface", 1));
        ExpectOutputLink(ctx, a1, "EmissiveColor", "MaterialExpressionDistanceToNearestSurface");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        const string b1 = "/Game/Tests/UE5_0/Mat_DF1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_DF1_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionDistanceToNearestSurface", 1));
        ExpectOutputLink(ctx, b1, "EmissiveColor", "MaterialExpressionDistanceToNearestSurface");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: DistanceFieldGradient -> Normal
    // =====================================================================

    private static void BuildQ2_DistanceFieldGradient(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_DF2_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_DF2_A");

        string gradId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionDistanceFieldGradient",
        }), "add MaterialExpressionDistanceFieldGradient");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = gradId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Normal",
        }), "connected", "connect DistanceFieldGradient -> Material.Normal");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_DF2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_DF2_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionDistanceFieldGradient", 1));
        ExpectOutputLink(ctx, a2, "Normal", "MaterialExpressionDistanceFieldGradient");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        const string b2 = "/Game/Tests/UE5_0/Mat_DF2_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_DF2_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionDistanceFieldGradient", 1));
        ExpectOutputLink(ctx, b2, "Normal", "MaterialExpressionDistanceFieldGradient");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: DistanceFieldsRenderingSwitch (Yes = red, No = black) -> BaseColor
    // =====================================================================

    private static void BuildQ3_DistanceFieldsRenderingSwitch(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_DF3_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_DF3_A");

        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",       // the distance-field path
        }), "add Constant3Vector(1,0,0)");

        string blackId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=0,B=0)",       // the standard path
        }), "add Constant3Vector(0,0,0)");

        string swId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDistanceFieldsRenderingSwitch",
        }), "add MaterialExpressionDistanceFieldsRenderingSwitch");

        // The engine's pin names are Yes / No, not the doc's DistanceField / Standard
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = redId,
            fromPin = "",
            toExpression = swId,
            toPin = "Yes",
        }), "connected", "connect red -> DistanceFieldsRenderingSwitch.Yes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = blackId,
            fromPin = "",
            toExpression = swId,
            toPin = "No",
        }), "connected", "connect black -> DistanceFieldsRenderingSwitch.No");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = swId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect DistanceFieldsRenderingSwitch -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_DF3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_DF3_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionDistanceFieldsRenderingSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, a3, "BaseColor", "MaterialExpressionDistanceFieldsRenderingSwitch");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionDistanceFieldsRenderingSwitch", "Yes", "No");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        const string b3 = "/Game/Tests/UE5_0/Mat_DF3_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_DF3_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionDistanceFieldsRenderingSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, b3, "BaseColor", "MaterialExpressionDistanceFieldsRenderingSwitch");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionDistanceFieldsRenderingSwitch", "Yes", "No");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
