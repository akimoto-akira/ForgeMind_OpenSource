// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 98 - Material Static Bool (UE 5.0)
//        - from the UE 5.0 material task doc "material - static bool" (Tasks
//          material group; the doc file name is the Chinese phrase for that,
//          which cannot be written in source)
//
// UMaterialExpressionStaticBool, UMaterialExpressionStaticBoolParameter and
// UMaterialExpressionStaticComponentMaskParameter, used with
// UMaterialExpressionStaticSwitch, UMaterialExpressionConstant3Vector and
// UMaterialExpressionTextureSample (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on MaterialExpressionStaticBool / StaticBoolParameter /
//     StaticComponentMaskParameter / StaticSwitch / TextureSample / Constant3Vector ->
//     1 match each.
//   * Header facts that decide the shape of this case:
//       - StaticBool carries the authored bitfield Value and emits MCT_StaticBool
//         (MaterialExpressionStaticBool.h).
//       - StaticBoolParameter derives from UMaterialExpressionParameter (ParameterName) and
//         carries the authored bitfield DefaultValue (MaterialExpressionStaticBoolParameter.h).
//       - StaticSwitch owns an optional FExpressionInput named Value besides A and B, so a
//         static bool produced elsewhere can drive the switch - this is what makes the doc's
//         question 1 shape expressible at all. There is no UMaterialExpressionStaticBoolSwitch
//         in 5.0.3 (SpkTool "class" on it -> 0 matches), so the switch that takes a bool input
//         is the plain StaticSwitch.
//       - StaticComponentMaskParameter carries the input Input plus the authored bitfields
//         DefaultR / DefaultG / DefaultB / DefaultA (MaterialExpressionStaticComponentMaskParameter.h).
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 2's DSL spells the node "StaticBoolSwitchParam" and question 1's spells the
//     switch's authored value "Value". The engine has no StaticBoolSwitch class, and on
//     StaticSwitch the authored bool is DefaultValue while Value is the optional input pin -
//     so question 2 is built as StaticBoolParameter feeding a StaticSwitch.Value pin, which is
//     what the doc's step list (not its DSL sketch) describes, and matches question 1's shape.
//   * The doc's texture "/Game/T_Brick" does not exist in the project; the case uses the
//     project's own /Game/StarterContent/Textures/T_Brick_Clay_New_D, the substitution the
//     earlier material cases already made.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Here the snapshot additionally pins the switch's Value pin being fed by a bool node (questions 1
// and 2) and the texture wire through StaticComponentMaskParameter.Input (question 3) - a node
// count alone could not tell those apart from an unwired switch.
// =========================================================================

public sealed class Material_StaticBool : ITestCase
{
    public string Name => "Material Static Bool (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 98;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_StaticBoolDrivesSwitch(ctx);
        BuildQ2_StaticBoolParameterDrivesSwitch(ctx);
        BuildQ3_StaticComponentMaskParameter(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: StaticBool(true) -> StaticSwitch.Value, red / blue -> BaseColor
    // =====================================================================

    private static void BuildQ1_StaticBoolDrivesSwitch(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_SBool1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SBool1_A");

        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add Constant3Vector(1,0,0)");

        string blueId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=0,B=1)",
        }), "add Constant3Vector(0,0,1)");

        string boolId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionStaticBool",
            Value = true,
        }), "add MaterialExpressionStaticBool(Value=true)");

        string switchId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionStaticSwitch",
        }), "add MaterialExpressionStaticSwitch");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = boolId,
            fromPin = "",
            toExpression = switchId,
            toPin = "Value",
        }), "connected", "connect StaticBool -> StaticSwitch.Value");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = redId,
            fromPin = "",
            toExpression = switchId,
            toPin = "A",
        }), "connected", "connect red -> StaticSwitch.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = blueId,
            fromPin = "",
            toExpression = switchId,
            toPin = "B",
        }), "connected", "connect blue -> StaticSwitch.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = switchId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect StaticSwitch -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_SBool1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_SBool1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionStaticBool", 1),
            ("MaterialExpressionStaticSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, a1, "BaseColor", "MaterialExpressionStaticSwitch");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionStaticSwitch", "Value", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        const string b1 = "/Game/Tests/UE5_0/Mat_SBool1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_SBool1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionStaticBool", 1),
            ("MaterialExpressionStaticSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, b1, "BaseColor", "MaterialExpressionStaticSwitch");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionStaticSwitch", "Value", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: StaticBoolParameter(UseBright) -> StaticSwitch.Value -> BaseColor
    // =====================================================================

    private static void BuildQ2_StaticBoolParameterDrivesSwitch(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_SBoolP1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SBoolP1_A");

        string whiteId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=1,B=1)",
        }), "add Constant3Vector(1,1,1)");

        string greyId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.2,G=0.2,B=0.2)",
        }), "add Constant3Vector(0.2,0.2,0.2)");

        string paramId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionStaticBoolParameter",
            name = "UseBright",
            DefaultValue = true,
        }), "add MaterialExpressionStaticBoolParameter(UseBright)");

        string switchId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionStaticSwitch",
        }), "add MaterialExpressionStaticSwitch");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = paramId,
            fromPin = "",
            toExpression = switchId,
            toPin = "Value",
        }), "connected", "connect StaticBoolParameter -> StaticSwitch.Value");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = whiteId,
            fromPin = "",
            toExpression = switchId,
            toPin = "A",
        }), "connected", "connect white -> StaticSwitch.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = greyId,
            fromPin = "",
            toExpression = switchId,
            toPin = "B",
        }), "connected", "connect grey -> StaticSwitch.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = switchId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect StaticSwitch -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_SBoolP1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_SBoolP1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionStaticBoolParameter", 1),
            ("MaterialExpressionStaticSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, a2, "BaseColor", "MaterialExpressionStaticSwitch");
        ExpectExpressionInputLinks(ctx, a2, "MaterialExpressionStaticSwitch", "Value", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        const string b2 = "/Game/Tests/UE5_0/Mat_SBoolP1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_SBoolP1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionStaticBoolParameter", 1),
            ("MaterialExpressionStaticSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, b2, "BaseColor", "MaterialExpressionStaticSwitch");
        ExpectExpressionInputLinks(ctx, b2, "MaterialExpressionStaticSwitch", "Value", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: Tex.RGBA -> StaticComponentMaskParameter(Channels RG) -> BaseColor
    // =====================================================================

    private static void BuildQ3_StaticComponentMaskParameter(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_SMask1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SMask1_A");

        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Brick_Clay_New_D.T_Brick_Clay_New_D",
        }), "add MaterialExpressionTextureSample(T_Brick_Clay_New_D)");

        string maskId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionStaticComponentMaskParameter",
            name = "Channels",
            DefaultR = true,
            DefaultG = true,
            DefaultB = false,
            DefaultA = false,
        }), "add MaterialExpressionStaticComponentMaskParameter(Channels, RG)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = texId,
            fromPin = "RGBA",
            toExpression = maskId,
            toPin = "Input",
        }), "connected", "connect Tex.RGBA -> StaticComponentMaskParameter.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = maskId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect StaticComponentMaskParameter -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_SMask1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_SMask1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionStaticComponentMaskParameter", 1));
        ExpectOutputLink(ctx, a3, "BaseColor", "MaterialExpressionStaticComponentMaskParameter");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionStaticComponentMaskParameter", "Input");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        const string b3 = "/Game/Tests/UE5_0/Mat_SMask1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_SMask1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionStaticComponentMaskParameter", 1));
        ExpectOutputLink(ctx, b3, "BaseColor", "MaterialExpressionStaticComponentMaskParameter");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionStaticComponentMaskParameter", "Input");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
