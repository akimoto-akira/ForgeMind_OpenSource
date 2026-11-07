// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 104 - Material Pre-Skinned Sources (UE 5.0)
//        - from the UE 5.0 material task doc "material - pre-skinned / deformed"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - skinning pre-deformation", which cannot be written in source)
//
// UMaterialExpressionPreSkinnedNormal, UMaterialExpressionPreSkinnedLocalBounds,
// UMaterialExpressionPreSkinnedPosition plus UMaterialExpressionWorldPosition and
// UMaterialExpressionSubtract (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on PreSkinnedNormal / PreSkinnedLocalBounds /
//     PreSkinnedPosition / WorldPosition / Subtract -> 1 match each. The Q1 bridge
//     MaterialExpressionVertexInterpolator -> 1 match
//     (super = UMaterialExpressionCustomOutput), and its Input pin plus the B-side DSL form
//     are the ones No. 93 already compiles.
//   * Constructor fact that decides question 2: UMaterialExpressionPreSkinnedLocalBounds
//     declares four outputs in this order (MaterialExpressions.cpp ctor)
//         0 "Half Extents", 1 "Extents", 2 "Min", 3 "Max"
//     so "Min" is a named output, index 2 - not the first pin. The doc asks for Min/Max, which
//     the multi-output convention says must then be requested by its declared name: the A side
//     passes fromPin "Min", the B side writes "B.Min". Both land on the named-output channel
//     (IR field sourceOutputName), never on a channel swizzle, which would insert an unrelated
//     ComponentMask (see the No. 74 / No. 85 notes on this design).
//   * The other two pre-skinned sources carry no properties and no output list.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics,
//     and the IR for question 2 carries "sourceOutputName":"Min".
//   * Asset names Mat_PreSkinN1_* / Mat_PreSkinB1_* / Mat_PreSkinP1_* occur in no existing
//     case or baseline.
//
// Doc/engine notes (the engine form wins over the doc wording):
//   * Question 1's step list wires PreSkinnedNormal straight into the material's Normal pin,
//     but the two live in different shader stages. UMaterialExpressionPreSkinnedNormal::Compile
//     calls FHLSLMaterialTranslator::PreSkinnedNormal, which errors unless
//     ShaderFrequency == SF_Vertex (HLSLMaterialTranslator.cpp:7006-7014); MP_Normal is
//     compiled in the pixel shader, so the direct wire cannot compile. Run 20261107_2141
//     surfaced the engine's own wording and its own remedy ("pass through custom interpolators
//     if needed"). Both channels therefore feed the normal into a VertexInterpolator: its
//     Input is compiled in SF_Vertex (GatherCustomVertexInterpolators, 702-712) and its
//     output is read back in SF_Pixel (VertexInterpolator, 7016-7021), so the doc's Normal
//     target is unchanged. This is the same engine-fact bridge No. 85 adds for the AR node's
//     required Coordinates input. The earlier note claiming the translator had no rejection
//     for this pair was wrong: the rejection is by shader stage, not by expression/pin name.
//   * Question 3 keeps the doc's subtraction of the current world position from the rest-pose
//     position and routes it to WorldPositionOffset - a vertex-stage input fed by vertex-stage
//     sources, which is the legal direction (contrast No. 93 question 2, where a pixel-only
//     interpolator output was pointed at this same pin and the engine refused).
//
// IR snapshot (VerifyIrSnapshot) - the mis-touch comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// doc/engine divergences noted above, plus this snapshot layer.
// =========================================================================

public sealed class Material_PreSkinnedSources : ITestCase
{
    public string Name => "Material Pre-Skinned Sources (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 104;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_PreSkinnedNormal(ctx);
        BuildQ2_PreSkinnedLocalBoundsMin(ctx);
        BuildQ3_RestPoseOffset(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: PreSkinnedNormal -> VertexInterpolator -> Normal
    // =====================================================================

    private static void BuildQ1_PreSkinnedNormal(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_PreSkinN1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_PreSkinN1_A");

        // Step 2: add PreSkinnedNormal and the vertex-to-pixel bridge (see header). The
        // expression only exists in SF_Vertex, so its value has to cross through a custom
        // interpolator before a pixel-stage input such as Normal can read it.
        string normalId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionPreSkinnedNormal",
        }), "add MaterialExpressionPreSkinnedNormal");

        string viId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionVertexInterpolator",
        }), "add MaterialExpressionVertexInterpolator");

        // Step 3: link through the interpolator -> Normal, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = normalId,
            fromPin = "",
            toExpression = viId,
            toPin = "Input",
        }), "connected", "connect PreSkinnedNormal -> VertexInterpolator.Input");
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = viId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Normal",
        }), "connected", "connect VertexInterpolator -> Material.Normal");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_PreSkinN1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_PreSkinN1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionPreSkinnedNormal", 1),
            ("MaterialExpressionVertexInterpolator", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_PreSkinN1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_PreSkinN1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionPreSkinnedNormal", 1),
            ("MaterialExpressionVertexInterpolator", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: PreSkinnedLocalBounds.Min -> BaseColor (named output, index 2)
    // =====================================================================

    private static void BuildQ2_PreSkinnedLocalBoundsMin(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_PreSkinB1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_PreSkinB1_A");

        // Step 2: add PreSkinnedLocalBounds
        string boundsId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionPreSkinnedLocalBounds",
        }), "add MaterialExpressionPreSkinnedLocalBounds");

        // Step 3: link its declared "Min" output -> BaseColor. The name is the engine's own
        // (the ctor lists Half Extents / Extents / Min / Max), so it is asked for by name
        // rather than by an index that would mean a component swizzle.
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = boundsId,
            fromPin = "Min",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect PreSkinnedLocalBounds.Min -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_PreSkinB1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_PreSkinB1_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionPreSkinnedLocalBounds", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_PreSkinB1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_PreSkinB1_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionPreSkinnedLocalBounds", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: Subtract(PreSkinnedPosition, WorldPosition) -> WorldPositionOffset
    // =====================================================================

    private static void BuildQ3_RestPoseOffset(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_PreSkinP1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_PreSkinP1_A");

        // Step 2: the two position sources
        string preSkinnedId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionPreSkinnedPosition",
        }), "add MaterialExpressionPreSkinnedPosition");

        string worldId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionWorldPosition",
        }), "add MaterialExpressionWorldPosition");

        // Step 3: the subtraction
        string subId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSubtract",
        }), "add MaterialExpressionSubtract");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = preSkinnedId,
            fromPin = "",
            toExpression = subId,
            toPin = "A",
        }), "connected", "connect PreSkinnedPosition -> Subtract.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = worldId,
            fromPin = "",
            toExpression = subId,
            toPin = "B",
        }), "connected", "connect WorldPosition -> Subtract.B");

        // Step 4: subtract -> WorldPositionOffset, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = subId,
            fromPin = "",
            toExpression = "Material",
            toPin = "WorldPositionOffset",
        }), "connected", "connect Subtract -> Material.WorldPositionOffset");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_PreSkinP1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_PreSkinP1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionPreSkinnedPosition", 1),
            ("MaterialExpressionWorldPosition", 1),
            ("MaterialExpressionSubtract", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_PreSkinP1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_PreSkinP1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionPreSkinnedPosition", 1),
            ("MaterialExpressionWorldPosition", 1),
            ("MaterialExpressionSubtract", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
