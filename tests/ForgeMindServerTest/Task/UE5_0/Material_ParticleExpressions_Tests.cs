// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 102 - Material Particle Expressions (UE 5.0)
//        - from the UE 5.0 material task doc "material - particle expressions"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - particle", which cannot be written in source)
//
// UMaterialExpressionParticleColor, UMaterialExpressionParticlePositionWS and
// UMaterialExpressionSphericalParticleOpacity (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on ParticleColor / ParticlePositionWS /
//     SphericalParticleOpacity -> 1 match each.
//   * Header and constructor facts:
//       - UMaterialExpressionParticleColor declares its own output list and the first entry
//         is the colour itself (MaterialExpressions.cpp ctor: Outputs.Reset(), then five
//         FExpressionOutput rows - colour, then the R / G / B / A masks). The doc wires "its
//         output", so the first declared output is taken (index 0) on both channels.
//       - UMaterialExpressionParticlePositionWS carries no properties and no output list.
//       - UMaterialExpressionSphericalParticleOpacity has "Density" (FExpressionInput) and
//         "ConstantDensity" (float); the doc names neither, so nothing is set and the node
//         keeps its default density.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//   * Asset names Mat_Particle_Color_* / Mat_Particle_Pos_* / Mat_Particle_Op* occur in no
//     existing case or baseline.
//
// Doc/engine divergence (the engine form wins over the doc wording):
//   * The doc's header asks Domain=Particle. UE 5.0.3 has no particle material domain:
//     EMaterialDomain (MaterialShared.h:244-262) declares Surface / DeferredDecal /
//     LightFunction / Volume / PostProcess / UI / RuntimeVirtualTexture only - the particle
//     domain was dropped from the engine long ago. This follows the decision already made in
//     No. 86 question 1 for the same impossible key: both channels ask for Surface and keep
//     the BlendMode the doc names (Additive / Additive / Translucent) plus every node, so the
//     subject of each question - the particle expression itself - stays under test.
//     The translator carries no domain guard for these three nodes (a search of
//     HLSLMaterialTranslator.cpp for particle-related rejections returns nothing), so nothing
//     here is asserted to succeed on behaviour: what the engine says is read from the compile
//     step, which reports its errors since run 20261029_1855.
//
// IR snapshot (VerifyIrSnapshot) - the mis-touch comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// doc/engine divergences noted above, plus this snapshot layer.
// =========================================================================

public sealed class Material_ParticleExpressions : ITestCase
{
    public string Name => "Material Particle Expressions (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 102;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_ParticleColor(ctx);
        BuildQ2_ParticlePosition(ctx);
        BuildQ3_SphericalOpacity(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: ParticleColor (first declared output) -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_ParticleColor(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Particle_Color_A";

        // Step 1: create material (the doc's Particle domain does not exist, see header)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Additive",
        }), "created", "create_material Mat_Particle_Color_A");

        // Step 2: add ParticleColor
        string colorId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionParticleColor",
        }), "add MaterialExpressionParticleColor");

        // Step 3: link its first declared output -> EmissiveColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = colorId,
            fromPin = "",             // first declared output (the colour)
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect ParticleColor -> Material.EmissiveColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Particle_Color_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Particle_Color_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionParticleColor", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_Particle_Color_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_Particle_Color_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionParticleColor", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: ParticlePositionWS -> BaseColor
    // =====================================================================

    private static void BuildQ2_ParticlePosition(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Particle_Pos_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Additive",
        }), "created", "create_material Mat_Particle_Pos_A");

        // Step 2: add ParticlePositionWS
        string posId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionParticlePositionWS",
        }), "add MaterialExpressionParticlePositionWS");

        // Step 3: link -> BaseColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = posId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect ParticlePositionWS -> Material.BaseColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Particle_Pos_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Particle_Pos_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionParticlePositionWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_Particle_Pos_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_Particle_Pos_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionParticlePositionWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: SphericalParticleOpacity -> Opacity
    // =====================================================================

    private static void BuildQ3_SphericalOpacity(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Particle_OpA";

        // Step 1: create material in translucent mode (Opacity is only consumed then)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Particle_OpA");

        // Step 2: add SphericalParticleOpacity, default density (the doc names none)
        string opId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSphericalParticleOpacity",
        }), "add MaterialExpressionSphericalParticleOpacity");

        // Step 3: link -> Opacity, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = opId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect SphericalParticleOpacity -> Material.Opacity");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Particle_OpA");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Particle_OpA");
        ExpectExpressions(ctx, a3, ("MaterialExpressionSphericalParticleOpacity", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_Particle_OpB";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_Particle_OpB");
        ExpectExpressions(ctx, b3, ("MaterialExpressionSphericalParticleOpacity", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
