// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 103 - Material Particle Extra (UE 5.0)
//        - from the UE 5.0 material task doc "material - particle supplement"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - particle supplement", which cannot be written in source)
//
// UMaterialExpressionParticleSubUV, UMaterialExpressionParticleSpeed,
// UMaterialExpressionParticleRadius, UMaterialExpressionParticleRelativeTime plus
// UMaterialExpressionMultiply (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on ParticleSubUV / ParticleSpeed / ParticleRadius /
//     ParticleRelativeTime / Multiply -> 1 match each. ParticleSubUV's entry resolves to
//     UMaterialExpressionParticleSubUV, super = UMaterialExpressionTextureSample.
//   * Header facts: ParticleSpeed / ParticleRadius / ParticleRelativeTime are pure value
//     sources (their Compile() just returns the compiler's value), and Multiply's pins are
//     the familiar A / B. ParticleSubUV is different: it is a texture-family expression and
//     its Compile() errors with "Missing ParticleSubUV input texture" when Texture is unset
//     (MaterialExpressions.cpp:9388-9401), so Texture is a required input, not an optional
//     property. Frame index and flip flags stay default because the doc names none.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//   * Asset names Mat_PSubUV1_* / Mat_PSpeed1_* / Mat_PTime1_* occur in no existing case or
//     baseline.
//
// Doc/engine divergence (the engine form wins over the doc wording):
//   * The doc's headers ask Domain=Particle. UE 5.0.3 has no particle material domain -
//     EMaterialDomain (MaterialShared.h:244-262) lists Surface / DeferredDecal / LightFunction
//     / Volume / PostProcess / UI / RuntimeVirtualTexture only - so both channels build in
//     Surface while keeping each question's BlendMode (Additive / Additive / Translucent) and
//     every node. This is the same decision already made for No. 86 question 1 and No. 102.
//   * Question 1's step list never assigns a texture, but the engine rejects the expression
//     without one (see above). Run 20261107_2114 surfaced it as a FAIL on compile_material
//     Mat_PSubUV1_A. Per engine fact both channels assign the project's T_Perlin_Noise_M to
//     the node's Texture property - the same asset No. 76 uses - and the doc's wiring target
//     (EmissiveColor) is unchanged. This is the same engine-fact addition No. 85 makes for
//     the AR node's required Coordinates input.
//
// Doc coverage note: the doc's own specialty-syntax list also names ParticleSize, ParticleRandom,
// ParticleDirection and ParticleMotionBlurFade, but no question builds them, so they are not
// transcribed here (a question, not a name list, is what a case verifies).
//
// IR snapshot (VerifyIrSnapshot) - the mis-touch comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// doc/engine divergences noted above, plus this snapshot layer.
// =========================================================================

public sealed class Material_ParticleExtra : ITestCase
{
    public string Name => "Material Particle Extra (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 103;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_ParticleSubUV(ctx);
        BuildQ2_SpeedTimesRadius(ctx);
        BuildQ3_RelativeTimeOpacity(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: ParticleSubUV -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_ParticleSubUV(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_PSubUV1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Additive",
        }), "created", "create_material Mat_PSubUV1_A");

        // Step 2: add ParticleSubUV with its required Texture (see header). Without it the
        // engine's Compile() refuses the expression, so compile_material reports compiled=false.
        string subUvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionParticleSubUV",
            texture = "/Game/StarterContent/Textures/T_Perlin_Noise_M",
        }), "add MaterialExpressionParticleSubUV(T_Perlin_Noise_M)");

        // Step 3: link -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = subUvId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect ParticleSubUV -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_PSubUV1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_PSubUV1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionParticleSubUV", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_PSubUV1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_PSubUV1_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionParticleSubUV", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: Multiply(ParticleSpeed, ParticleRadius) -> EmissiveColor
    // =====================================================================

    private static void BuildQ2_SpeedTimesRadius(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_PSpeed1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Additive",
        }), "created", "create_material Mat_PSpeed1_A");

        // Step 2: the two particle sources
        string speedId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionParticleSpeed",
        }), "add MaterialExpressionParticleSpeed");

        string radiusId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionParticleRadius",
        }), "add MaterialExpressionParticleRadius");

        // Step 3: the multiply, fed by both
        string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = speedId,
            fromPin = "",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect ParticleSpeed -> Multiply.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = radiusId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect ParticleRadius -> Multiply.B");

        // Step 4: multiply -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = mulId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Multiply -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_PSpeed1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_PSpeed1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionParticleSpeed", 1),
            ("MaterialExpressionParticleRadius", 1),
            ("MaterialExpressionMultiply", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_PSpeed1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_PSpeed1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionParticleSpeed", 1),
            ("MaterialExpressionParticleRadius", 1),
            ("MaterialExpressionMultiply", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: ParticleRelativeTime -> Opacity
    // =====================================================================

    private static void BuildQ3_RelativeTimeOpacity(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_PTime1_A";

        // Step 1: create material in translucent mode (the doc's fade needs Opacity)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_PTime1_A");

        // Step 2: add ParticleRelativeTime
        string timeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionParticleRelativeTime",
        }), "add MaterialExpressionParticleRelativeTime");

        // Step 3: link -> Opacity, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = timeId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect ParticleRelativeTime -> Material.Opacity");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_PTime1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_PTime1_A");
        ExpectExpressions(ctx, a3, ("MaterialExpressionParticleRelativeTime", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_PTime1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_PTime1_B");
        ExpectExpressions(ctx, b3, ("MaterialExpressionParticleRelativeTime", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
