// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 108 - Material Speed Tree (UE 5.0)
//        - from the UE 5.0 material task doc "material - speed tree"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - speed tree decal", which cannot be written in source)
//
// UMaterialExpressionSpeedTree (UE 5.0.3 Materials/MaterialExpressionSpeedTree.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" MaterialExpressionSpeedTree -> 1 match.
//   * ForgeMind.SpkTool "translate" on the B-side DSL below -> 0 diagnostics; the
//     emitted IR carries exactly MaterialExpressionSpeedTree.
//   * Header facts (the engine form wins over the doc wording):
//       - ESpeedTreeLODType declares STLOD_Pop / STLOD_Smooth only; the doc's
//         "SpeedTree_LOD" is not an enum value. The constructor already sets
//         LODType = STLOD_Pop (MaterialExpressions.cpp:18292-18296), so both channels
//         write that real name and keep the doc's intent of choosing an LOD type.
//       - GeometryInput / WindInput / LODInput / ExtraBendWS are all declared
//         RequiredInput = "false" (MaterialExpressionSpeedTree.h:45-55); Compile falls
//         back to the GeometryType / WindType / LODType constants when they are unwired
//         (MaterialExpressions.cpp:18306-18311), so the doc's "hook up Billboard/Geometry
//         inputs" is satisfied by the engine's defaults on both channels.
//       - The node declares no named output; UMaterialExpression's constructor seeds the
//         single blank output (MaterialExpressions.cpp:697-707). The doc's ".Color" read
//         therefore lands on the DSL swizzle table's Color entry -> output index 0, the
//         same output the A side asks for with an empty fromPin.
//   * Asset names Mat_SpeedTree1_* occur in no existing case or baseline. The doc's
//     change log removed its questions 2 and 3 (they shared Mat_Decal1_*/Mat_Decal2_*
//     with the already transcribed No. 84), so only question 1 is transcribed here.
//
// Engine-stage caveat, recorded rather than worked around (spec section 6):
//   FHLSLMaterialTranslator::SpeedTree returns NonVertexShaderExpressionError() when
//   ShaderFrequency != SF_Vertex (HLSLMaterialTranslator.cpp:10417-10420), while
//   MP_BaseColor is a pixel-stage property (MaterialShared.cpp). Wiring the node straight
//   to BaseColor, as the doc asks, is therefore a combination the engine refuses at
//   compile time - the same shape as No. 101 question 3 and No. 104 question 1. This case
//   is transcribed verbatim so the compile gate reports the engine's own verdict: whether
//   to keep the request as documentation of an impossible combination, or to change the
//   target (a vertex-stage pin, or a vertex-to-pixel bridge), is the user's decision.
//   No exception is caught and no assertion is skipped inside the case.
//
// No IR snapshot (VerifyIrSnapshot) is taken: per collaboration spec section 9 a
// transcribed case must not add one on its own. Coverage is the step commands' business
// flags, the read-back count of the named expression, and the output link check.
// =========================================================================

public sealed class Material_SpeedTree : ITestCase
{
    public string Name => "Material Speed Tree (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 108;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_SpeedTreeColor(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: SpeedTree -> BaseColor
    // =====================================================================

    private static void BuildQ1_SpeedTreeColor(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_SpeedTree1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SpeedTree1_A");

        string speedTreeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionSpeedTree",
            LODType = "STLOD_Pop",
        }), "add MaterialExpressionSpeedTree(LODType=STLOD_Pop)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = speedTreeId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect SpeedTree -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_SpeedTree1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_SpeedTree1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionSpeedTree", 1));
        ExpectOutputLink(ctx, a1, "BaseColor", "MaterialExpressionSpeedTree");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_SpeedTree1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_SpeedTree1_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionSpeedTree", 1));
        ExpectOutputLink(ctx, b1, "BaseColor", "MaterialExpressionSpeedTree");
    }
}
