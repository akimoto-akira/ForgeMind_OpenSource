// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 81 - Material World Position (UE 5.0)
//        - from the UE 5.0 material task doc "world position and camera"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - world position", which cannot be written in source)
//
// Position / view data-flow expressions of the material domain:
//   UMaterialExpressionWorldPosition, UMaterialExpressionObjectPositionWS,
//   UMaterialExpressionObjectBounds, UMaterialExpressionCameraVectorWS,
//   UMaterialExpressionPixelNormalWS, UMaterialExpressionDotProduct.
// Each has exactly one output and is wired by its engine input names
// (DotProduct: A / B - UE 5.0.3 Materials/MaterialExpression*.h). All six were
// verified present in engine.spk via ForgeMind.SpkTool "class" before writing;
// the whole B-side DSL below was verified with "translate" (0 diagnostics) and
// produces exactly the IR the A-side steps build.
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//   Both channels build through the same Companion expression operations, and
//   get_asset_ir(level=graph) reads the graph back including pin connections.
//
// DSL syntax note: the task doc sketches the material DSL in an attribute / call
// form ([WorldPosition] MaterialExpression WP; [Output(BaseColor)] ... = Dot(a, b))
// that is not the documented material DSL surface and omits the mandatory Mode.
// Per the standing decision for material task docs, the case uses the documented,
// translator-supported form: [Material(Path, Mode = Create)] + [MaterialSettings],
// "NodeType v = new NodeType();" declarations, "NodeType n = new NodeType { A = x,
// B = y };" for wiring an input, and "OutputPin = node;" for the material output.
// Node type names are the ones the bridge actually resolves (DotProduct, not the
// doc's "Dot"; ObjectPositionWS / CameraVectorWS / PixelNormalWS as written).
//
// Topology note: task 2 lists ObjectBounds in its step 2 but wires only
// ObjectPositionWS (its step 3 says "simplified: feed ObjPos straight into BaseColor", and the
// Subtract operand it names is left unspecified), so the case adds the ObjectBounds
// node unwired on both channels - the A-side step list is authoritative and the
// DSL mirrors it. ObjectOrientation appears in the doc header's class list but in no
// task, so it is not built here.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) inserted on user instruction per
// collaboration spec section 9: one call per built asset, right after that asset's save -
// A side after compile_material + save_asset, B side after ApplyDsl + save_asset (material
// assets are not saved by the DSL apply, so the save stays explicit). First run seeds the
// six baselines under TestRuns/5.0/, the second run has to report them all as a match.
// =========================================================================

public sealed class Material_WorldPosition : ITestCase
{
    public string Name => "Material World Position (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 81;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_WorldPositionBaseColor(ctx);
        BuildQ2_ObjectPosition(ctx);
        BuildQ3_CameraVectorDotNormal(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: WorldPosition visualised through BaseColor
    // =====================================================================

    private static void BuildQ1_WorldPositionBaseColor(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_WP1_A";

        // Step 1: create material (the doc leaves the type at its default: Surface / Opaque)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_WP1_A");

        // Step 2: add MaterialExpressionWorldPosition
        string wpId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionWorldPosition",
        }), "add MaterialExpressionWorldPosition");

        // Step 3: link its output -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = wpId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect WorldPosition -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_WP1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_WP1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_WP1_B" }),
            "saved", "save_asset Mat_WP1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_WP1_B");
    }

    // =====================================================================
    // Q2: ObjectPositionWS (+ ObjectBounds, unwired per the doc's own steps)
    // =====================================================================

    private static void BuildQ2_ObjectPosition(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_ObjPos_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ObjPos_A");

        // Step 2a: add MaterialExpressionObjectPositionWS
        string objPosId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionObjectPositionWS",
        }), "add MaterialExpressionObjectPositionWS");

        // Step 2b: add MaterialExpressionObjectBounds. The doc lists it among the nodes to
        // add but its own step 3 wires only the object position (the Subtract operand it names
        // is left unspecified), so this node stays unwired on both channels.
        string boundsId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionObjectBounds",
        }), "add MaterialExpressionObjectBounds");

        // Step 3/4: link ObjectPositionWS -> BaseColor (the doc's simplified centre-offset view)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = objPosId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect ObjectPositionWS -> Material.BaseColor");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_ObjPos_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_ObjPos_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // -- B-side: DSL (ObjectBounds present but unwired, matching the A side) --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ObjPos_B" }),
            "saved", "save_asset Mat_ObjPos_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ObjPos_B");
    }

    // =====================================================================
    // Q3: Dot(CameraVectorWS, PixelNormalWS) -> EmissiveColor (facing term)
    // =====================================================================

    private static void BuildQ3_CameraVectorDotNormal(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_CamVec_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_CamVec_A");

        // Step 2a: add MaterialExpressionCameraVectorWS
        string camVecId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionCameraVectorWS",
        }), "add MaterialExpressionCameraVectorWS");

        // Step 2b: add MaterialExpressionPixelNormalWS
        string normalId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionPixelNormalWS",
        }), "add MaterialExpressionPixelNormalWS");

        // Step 3: add MaterialExpressionDotProduct
        string dotId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDotProduct",
        }), "add MaterialExpressionDotProduct");

        // Step 3: A = CamVec
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = camVecId,
            fromPin = "",
            toExpression = dotId,
            toPin = "A",
        }), "connected", "connect CameraVectorWS -> Dot.A");

        // Step 3: B = Normal
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = normalId,
            fromPin = "",
            toExpression = dotId,
            toPin = "B",
        }), "connected", "connect PixelNormalWS -> Dot.B");

        // Step 4: link Dot -> EmissiveColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = dotId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Dot -> Material.EmissiveColor");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_CamVec_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_CamVec_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_CamVec_B" }),
            "saved", "save_asset Mat_CamVec_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_CamVec_B");
    }

    // The six assets are verified by the BlueprintTestHelper.VerifyIrSnapshot calls placed after
    // each save; the step commands are checked through the shared StepOk / ExpressionId helpers.
}
