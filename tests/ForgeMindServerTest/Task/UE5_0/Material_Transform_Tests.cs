// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 77 - Material Transform (UE 5.0) - from Tasks/UE_5.0/Material/Material_Transform.md
//
// MaterialExpressionTransform (UMaterialExpressionTransform: FExpressionInput Input,
// plus the TransformSourceType / TransformType pair of EMaterialVectorCoordTransform*
// enums) converts a direction vector between tangent / local / world / view / camera
// space; MaterialExpressionTransformPosition (TransformSourceType / TransformType of
// EMaterialPositionTransformSource) converts a position. UE 5.0.3 source:
// Classes/Materials/MaterialExpressionTransform.h, MaterialExpressionTransformPosition.h.
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//
// Property naming: the doc's DSL calls the two space selectors Source / Dest. Those are
// the editor's DisplayName labels, not the UPROPERTY names, so both channels address the
// engine real names TransformSourceType / TransformType (section 8: resolve by what is
// actually found). The values are the bare space words ("World", "Tangent", "View",
// "Local"), which the Companion resolves through the enum's TRANSFORM*_ prefix tail.
//
// Capability verification and completion: Transform and TransformPosition have
// declarations in the UE 5.0 engine index, but the position source the doc's Q2 feeds
// into TransformPosition - MaterialExpressionLocalPosition - does not: FindClassesFlexible
// returns 0 matches and 5.0.3 has no such header (its position expressions are WorldPosition
// / ObjectPositionWS / ActorPositionWS / CameraPositionWS / ScreenPosition / PreSkinnedPosition).
// Per user decision 2026-10-25 ("assume implemented, transcribe as named") Q2 was transcribed
// as the doc names it, and the first run failed exactly at that add step - which per spec
// section 6 makes the gap the requirement to close. UMaterialExpressionLocalPosition is now
// a real expression class provided by the ForgeMindForUE plugin
// (ForgeMindMaterialPositionExpressions.h): the shaded point's position transformed into the
// local basis through the same legacy compiler primitives the TransformPosition node uses
// (WorldPosition(WPT_Default) -> TransformPosition(MCB_World, MCB_Local)), so the doc's Q2
// graph compiles on the default translator. add_material_expression resolves it through live
// reflection; no other node is substituted.
//
// DSL syntax note: the doc sketches the material DSL in an attribute form ([Transform(...)],
// T.Input(C), [CameraVectorWS]); that is not the surface the material DSL syntax spec defines
// and MaterialTranslator lowers, so the B side uses the documented form - new NodeType
// { Pin = source } initializers with bare "Pin = node;" output assignments - the same
// substitution already applied to Material Constant (No. 75) and Material If (No. 74).
//
// Topology note: the doc's Q1 step list adds a WorldPosition node that its DSL never uses
// and never wires anywhere. The A-side step list is authoritative (the rule set for
// Material If), so the B side declares the same unconnected WorldPosition node and the two
// graphs hold the same expressions.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) inserted on user instruction
// per collaboration spec point 9 (2026-10-26): one snapshot per asset, six in total
// (Q1-Q3, each A-side after compile_material + save_asset, each B-side after
// ApplyDsl + save_asset). The first run after this seeds six baselines into
// TestRuns/5.0/ (no failure); the run after that must match all six. Until the
// snapshot request, the doc's "no execution failure, no verification error" was
// covered by asserting every step command's business flag (including the add step's
// propertyErrors) and by reading the finished material back to check that each
// expression the doc names landed with the space selectors written onto it - those
// checks stay in place alongside the snapshots.
// =========================================================================

public sealed class Material_Transform : ITestCase
{
    public string Name => "Material Transform (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 77;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_WorldToTangent(ctx);
        BuildQ2_LocalToWorldPosition(ctx);
        BuildQ3_WorldToViewNormal(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Transform world -> tangent (camera vector into Normal)
    // =====================================================================

    private static void BuildQ1_WorldToTangent(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Trans1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Trans1_A");

        // Step 2: add WorldPosition and CameraVectorWS (the doc adds both; only the
        // camera vector feeds the transform)
        ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionWorldPosition",
        }), "add MaterialExpressionWorldPosition");

        string camId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionCameraVectorWS",
        }), "add MaterialExpressionCameraVectorWS");

        // Step 3: add MaterialExpressionTransform: Input = CamVec, Source = World, Destination = Tangent
        string transId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTransform",
            transformSourceType = "World",      // per task spec (engine property TransformSourceType)
            transformType = "Tangent",          // per task spec (engine property TransformType)
        }), "add MaterialExpressionTransform(World->Tangent)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = camId,
            fromPin = "",
            toExpression = transId,
            toPin = "Input",
        }), "connected", "connect CameraVectorWS -> Transform.Input");

        // Step 4: link -> Normal, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = transId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Normal",           // per task spec
        }), "connected", "connect Transform -> Material.Normal");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Trans1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Trans1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionWorldPosition", 1),
            ("MaterialExpressionCameraVectorWS", 1),
            ("MaterialExpressionTransform", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Trans1_B" }),
            "saved", "save_asset Mat_Trans1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Trans1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Trans1_B",
            ("MaterialExpressionWorldPosition", 1),
            ("MaterialExpressionCameraVectorWS", 1),
            ("MaterialExpressionTransform", 1));
    }

    // =====================================================================
    // Q2: TransformPosition local -> world (into WorldPositionOffset)
    // =====================================================================

    private static void BuildQ2_LocalToWorldPosition(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_TransP1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_TransP1_A");

        // Step 2: add LocalPosition, then MaterialExpressionTransformPosition
        // (Input = LocalPosition, Source = Local, Destination = World)
        string posId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionLocalPosition",
        }), "add MaterialExpressionLocalPosition");

        string tpId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionTransformPosition",
            transformSourceType = "Local",      // per task spec
            transformType = "World",            // per task spec
        }), "add MaterialExpressionTransformPosition(Local->World)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = posId,
            fromPin = "",
            toExpression = tpId,
            toPin = "Input",
        }), "connected", "connect LocalPosition -> TransformPosition.Input");

        // Step 3: link -> WorldPositionOffset, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = tpId,
            fromPin = "",
            toExpression = "Material",
            toPin = "WorldPositionOffset",  // per task spec
        }), "connected", "connect TransformPosition -> Material.WorldPositionOffset");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_TransP1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_TransP1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionLocalPosition", 1),
            ("MaterialExpressionTransformPosition", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_TransP1_B" }),
            "saved", "save_asset Mat_TransP1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_TransP1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_TransP1_B",
            ("MaterialExpressionLocalPosition", 1),
            ("MaterialExpressionTransformPosition", 1));
    }

    // =====================================================================
    // Q3: Transform world -> view (pixel normal rebuilt in view space)
    // =====================================================================

    private static void BuildQ3_WorldToViewNormal(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_TransN1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_TransN1_A");

        // Step 2: add PixelNormalWS
        string normalId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionPixelNormalWS",
        }), "add MaterialExpressionPixelNormalWS");

        // Step 3: add Transform: Source = World, Dest = View
        string transId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionTransform",
            transformSourceType = "World",      // per task spec
            transformType = "View",             // per task spec
        }), "add MaterialExpressionTransform(World->View)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = normalId,
            fromPin = "",
            toExpression = transId,
            toPin = "Input",
        }), "connected", "connect PixelNormalWS -> Transform.Input");

        // Step 4: link -> EmissiveColor, then compile / save. The doc's step list says
        // Normal, but 5.0 rejects any PixelNormalWS upstream of the Normal pin at compile
        // time (the Normal input wants a tangent-space normal; user ruling 2026-10-26),
        // so the view-space result lands on EmissiveColor instead - task doc annotated.
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = transId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",    // Normal was engine-invalid; user ruling 2026-10-26
        }), "connected", "connect Transform -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_TransN1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_TransN1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionPixelNormalWS", 1),
            ("MaterialExpressionTransform", 1));

        // -- B-side: DSL (EmissiveColor: the engine-invalid Normal feed, user ruling 2026-10-26) --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_TransN1_B" }),
            "saved", "save_asset Mat_TransN1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_TransN1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_TransN1_B",
            ("MaterialExpressionPixelNormalWS", 1),
            ("MaterialExpressionTransform", 1));
    }

    // The step commands and the per-asset content checks run through the shared
    // BlueprintTestHelper helpers: StepOk / ExpressionId (which also fails an add whose
    // payload keys did not all land - the space selectors of a transform are exactly what
    // this case is about) and ExpectExpressions (the live read-back count check).
}
