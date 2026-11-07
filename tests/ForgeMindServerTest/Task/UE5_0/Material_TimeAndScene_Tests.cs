// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 105 - Material Time And Scene Sampling (UE 5.0)
//        - from the UE 5.0 material task doc "material - time / scene sampling"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - time system", which cannot be written in source)
//
// UMaterialExpressionTime, UMaterialExpressionSine, UMaterialExpressionScreenPosition,
// UMaterialExpressionComponentMask, UMaterialExpressionSceneDepth,
// UMaterialExpressionSceneColor, UMaterialExpressionSubtract
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on Time / Sine / ScreenPosition / ComponentMask / SceneDepth /
//     SceneColor / Subtract -> 1 match each.
//   * Header members actually used:
//       - Time: "Period" (float). Sine: "Input" (FExpressionInput) + "Period" (float).
//       - ComponentMask: "Input" plus the four single-bit flags R / G / B / A
//         (uint32 R:1 ... materialised as boolean properties).
//       - ScreenPosition declares its own output list: 0 "ViewportUV", 1 "PixelPosition"
//         (MaterialExpressions.cpp ctor). The doc takes the screen position without naming a
//         pin, so the first declared output (ViewportUV) is used on both channels.
//       - SceneColor: "InputMode" (EMaterialSceneAttributeInputMode), "Input"
//         (FExpressionInput), "ConstInput" (FVector2D) and a deprecated offset pin. The doc
//         names none of them, so the node is created with its defaults and no coordinates wire.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//   * Asset names Mat_Time1_* / Mat_Screen1_* / Mat_Scene1_* occur in no existing case or
//     baseline. SceneTexture / SceneDepth sampling as a family is already covered by No. 89;
//     these questions build different graphs (a sine oscillator, a screen-uv mask, a colour
//     minus depth subtraction), so no construction is duplicated.
//
// Doc/engine divergence (the engine form wins over the doc wording):
//   * Question 3's example header writes BlendMode=Opaque while the graph reads scene depth.
//     UE rejects that outright: the translator guards it with
//         "Only transparent or postprocess materials can read from scene depth."
//     (HLSLMaterialTranslator.cpp:990-994, on bUsesSceneDepth && Domain != MD_PostProcess &&
//     !IsTranslucentBlendMode(BlendMode)). Both channels therefore build the graph as
//     Translucent - the same kind of correction No. 83 accepted for the Volume domain - and
//     the nodes, the subtraction and the EmissiveColor target stay exactly as the doc names
//     them. SceneColor itself is legal in the Surface domain (the translator only rejects it
//     outside Surface, HLSLMaterialTranslator.cpp:6511-6519), which this question keeps.
//   * Question 3 also reads the colour's red channel ("A=SceneColor.R"). A channel read is a
//     component extraction, not a named output: the A side asks for it with fromPin "R" and the
//     bridge inserts the ComponentMask the editor would show, and the B side spells that mask
//     out as its own node. Both channels therefore hold exactly one ComponentMask.
//
// IR snapshot (VerifyIrSnapshot) - the mis-touch comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// doc/engine divergences noted above, plus this snapshot layer.
// =========================================================================

public sealed class Material_TimeAndScene : ITestCase
{
    public string Name => "Material Time And Scene (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 105;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_TimeSineOscillator(ctx);
        BuildQ2_ScreenPositionMask(ctx);
        BuildQ3_SceneColorMinusDepth(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Sine(Time) -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_TimeSineOscillator(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Time1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Time1_A");

        // Step 2: add Time
        string timeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTime",
        }), "add MaterialExpressionTime");

        // Step 3: add Sine and feed it from Time
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

        // Step 4: Sine -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = sineId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Sine -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Time1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Time1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionTime", 1),
            ("MaterialExpressionSine", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_Time1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_Time1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionTime", 1),
            ("MaterialExpressionSine", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: ComponentMask(R,G) fed by ScreenPosition -> BaseColor
    // =====================================================================

    private static void BuildQ2_ScreenPositionMask(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Screen1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Screen1_A");

        // Step 2: add ScreenPosition (its first declared output is ViewportUV)
        string screenId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScreenPosition",
        }), "add MaterialExpressionScreenPosition");

        // Step 3: add the ComponentMask with the doc's two channels on
        string maskId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionComponentMask",
            R = true,
            G = true,
            B = false,
            A = false,
        }), "add MaterialExpressionComponentMask(R,G)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = screenId,
            fromPin = "",             // first declared output: ViewportUV
            toExpression = maskId,
            toPin = "Input",
        }), "connected", "connect ScreenPosition -> ComponentMask.Input");

        // Step 4: mask -> BaseColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = maskId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect ComponentMask -> Material.BaseColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Screen1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Screen1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionScreenPosition", 1),
            ("MaterialExpressionComponentMask", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_Screen1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_Screen1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionScreenPosition", 1),
            ("MaterialExpressionComponentMask", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: Subtract(SceneColor.R, SceneDepth) -> EmissiveColor
    // =====================================================================

    private static void BuildQ3_SceneColorMinusDepth(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Scene1_A";

        // Step 1: create material. The doc's example header says Opaque, but reading scene
        // depth in an opaque material is refused by the translator (header note), so the graph
        // is built Translucent on both channels.
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Scene1_A");

        // Step 2: the two scene sources
        string depthId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSceneDepth",
        }), "add MaterialExpressionSceneDepth");

        string colorId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSceneColor",
        }), "add MaterialExpressionSceneColor");

        // Step 3: the subtraction. A = the colour's red channel: naming a channel makes the
        // bridge insert the ComponentMask the editor would show, which is the same node the
        // DSL side spells out, so both graphs hold one mask.
        string subId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSubtract",
        }), "add MaterialExpressionSubtract");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = colorId,
            fromPin = "R",            // channel read -> inserted ComponentMask
            toExpression = subId,
            toPin = "A",
        }), "connected", "connect SceneColor.R -> Subtract.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = depthId,
            fromPin = "",
            toExpression = subId,
            toPin = "B",
        }), "connected", "connect SceneDepth -> Subtract.B");

        // Step 4: subtract -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = subId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Subtract -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Scene1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Scene1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionSceneDepth", 1),
            ("MaterialExpressionSceneColor", 1),
            ("MaterialExpressionComponentMask", 1),
            ("MaterialExpressionSubtract", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_Scene1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_Scene1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionSceneDepth", 1),
            ("MaterialExpressionSceneColor", 1),
            ("MaterialExpressionComponentMask", 1),
            ("MaterialExpressionSubtract", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
