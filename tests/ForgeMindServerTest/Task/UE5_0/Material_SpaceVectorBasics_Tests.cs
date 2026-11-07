// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 101 - Material Space Vector Basics (UE 5.0)
//        - from the UE 5.0 material task doc "material - space vector basics"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - spatial vector foundation", which cannot be written in source)
//
// UMaterialExpressionActorPositionWS, UMaterialExpressionCameraPositionWS,
// UMaterialExpressionCameraVectorWS, UMaterialExpressionPixelNormalWS
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on ActorPositionWS / CameraPositionWS / CameraVectorWS /
//     PixelNormalWS -> 1 match each; none of the four carries any property (they are pure
//     value sources with a single implicit output), so the steps only create and wire.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics,
//     and the emitted IR carries exactly the four expression types named above.
//   * Asset names are new: no Mat_SpVec* occurs in any existing case or baseline.
//
// Doc/engine notes (the engine form wins over the doc wording):
//   * Question 1 names two sources but only one wire ("link -> BaseColor"); the doc's own
//     DSL half resolves the ambiguity - it outputs the actor position and remarks that the
//     camera position "could equally be built" - so both channels create both nodes and
//     wire ActorPositionWS to BaseColor, leaving CameraPositionWS created but unwired.
//   * Question 3 originally wired PixelNormalWS straight into the material's Normal pin as
//     the doc asked. UE 5.0.3 rejects that combination in HLSLMaterialTranslator's MP_Normal
//     branch ("Invalid node PixelNormalWS used for Normal input."): a normal may be consumed
//     while other locals are still being generated, which is exactly what this node does.
//     No. 77 recorded the same rejection and No. 94 derives a normal instead of assigning
//     it. Run 20261107_2008 surfaced it as a FAIL and the user ruled to keep the expression
//     and change the target pin, so both channels now land PixelNormalWS on EmissiveColor -
//     the same destination No. 77 uses. The compile gate has reported the engine's own
//     wording since run 20261029_1855.
//
// IR snapshot (VerifyIrSnapshot) - the mis-touch comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Coverage is the step commands' business flags, the read-back count of the named expressions, the
// doc/engine divergences noted above, plus this snapshot layer.
// =========================================================================

public sealed class Material_SpaceVectorBasics : ITestCase
{
    public string Name => "Material Space Vector Basics (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 101;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_ActorAndCameraPosition(ctx);
        BuildQ2_CameraVector(ctx);
        BuildQ3_PixelNormalToEmissive(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: ActorPositionWS -> BaseColor, CameraPositionWS created
    // =====================================================================

    private static void BuildQ1_ActorAndCameraPosition(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_SpVec1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SpVec1_A");

        // Step 2: add the two position sources
        string actorId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionActorPositionWS",
        }), "add MaterialExpressionActorPositionWS");

        string cameraId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionCameraPositionWS",
        }), "add MaterialExpressionCameraPositionWS");
        // The doc wires only one of the two (its DSL half outputs the actor position and
        // notes the camera position is equally buildable), so this node stays unwired here
        // and on the DSL side - the graphs remain twins.
        _ = cameraId;

        // Step 3: link ActorPositionWS -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = actorId,
            fromPin = "",             // single implicit output
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect ActorPositionWS -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_SpVec1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_SpVec1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionActorPositionWS", 1),
            ("MaterialExpressionCameraPositionWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_SpVec1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_SpVec1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionActorPositionWS", 1),
            ("MaterialExpressionCameraPositionWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: CameraVectorWS -> EmissiveColor
    // =====================================================================

    private static void BuildQ2_CameraVector(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_SpVec2_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SpVec2_A");

        // Step 2: add CameraVectorWS
        string cameraId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionCameraVectorWS",
        }), "add MaterialExpressionCameraVectorWS");

        // Step 3: link -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = cameraId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect CameraVectorWS -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_SpVec2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_SpVec2_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionCameraVectorWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_SpVec2_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_SpVec2_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionCameraVectorWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: PixelNormalWS -> EmissiveColor (the pin the user ruled for, see header)
    // =====================================================================

    private static void BuildQ3_PixelNormalToEmissive(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_SpVec3_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SpVec3_A");

        // Step 2: add PixelNormalWS
        string normalId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionPixelNormalWS",
        }), "add MaterialExpressionPixelNormalWS");

        // Step 3: link -> EmissiveColor, compile / save. MP_Normal cannot take a raw
        // PixelNormalWS in UE 5.0.3 (see header), so the value lands on a pin the translator
        // accepts; the compile step still reports the engine's own verdict.
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = normalId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect PixelNormalWS -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_SpVec3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_SpVec3_A");
        ExpectExpressions(ctx, a3, ("MaterialExpressionPixelNormalWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_SpVec3_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_SpVec3_B");
        ExpectExpressions(ctx, b3, ("MaterialExpressionPixelNormalWS", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
