// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 85 - Material AR Camera (UE 5.0)
//        - from the UE 5.0 material task doc "AR camera / atmospheric light
//          vector" (Tasks material group; the doc file name is the Chinese
//          phrase for "material - AR camera", which cannot be written in source)
//
// AR passthrough camera and atmospheric light expressions of the material
// domain: UMaterialExpressionMapARPassthroughCameraUV,
// UMaterialExpressionAtmosphericLightVector (UE 5.0.3 Materials/
// MaterialExpression*.h), combined through UMaterialExpressionDotProduct.
// AtmosphericLightVector carries no properties and one output; the AR mapping
// node carries the single required input "Coordinates". All class names were
// verified present in engine.spk via ForgeMind.SpkTool "class" before writing;
// the whole B-side DSL below was verified with "translate" (0 diagnostics).
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//
// Doc/engine divergence note: the doc never feeds the AR node's Coordinates
// input, but the engine marks it RequiredInput="true" and its Compile errors
// with "UV input missing" when unwired (MaterialExpressions.cpp:19475-19478).
// Per engine fact both channels feed it a TextureCoordinate - the standard UV
// source - and the doc's wiring target (BaseColor in Q1, DotProduct in Q3) is
// unchanged.
//
// IR snapshots (BlueprintTestHelper.VerifyIrSnapshot) are taken on user instruction:
// one point per asset, 6 in total (Q1-Q3, A/B each), placed right after each asset's
// save_asset and before the read-back assertions. The first run seeds the 6 baselines
// into TestRuns/5.0/ (a seed is not a pass); the second run must report all 6 as
// "IR snapshot match" for the case to be stable across runs. On a mismatch the
// baseline stays untouched and the root-cause analysis goes to the user first.
// The step-command business flags and the ExpectExpressions checks remain alongside
// the snapshots.
// =========================================================================

public sealed class Material_ARCamera : ITestCase
{
    public string Name => "Material AR Camera (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 85;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_ARPassthroughUV(ctx);
        BuildQ2_AtmosphericLightVector(ctx);
        BuildQ3_ARAtmosphericDot(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: MapARPassthroughCameraUV -> BaseColor
    // =====================================================================

    private static void BuildQ1_ARPassthroughUV(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_AR1_A";

        // Step 1: create material (the doc leaves the type at its default: Surface / Opaque)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_AR1_A");

        // Step 2a: add MaterialExpressionMapARPassthroughCameraUV
        string arId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionMapARPassthroughCameraUV",
        }), "add MaterialExpressionMapARPassthroughCameraUV");

        // Step 2b: add MaterialExpressionTextureCoordinate and feed the AR node's
        // required Coordinates input (the doc omits this wire; the engine errors
        // without it - see the header note)
        string uvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTextureCoordinate",
        }), "add MaterialExpressionTextureCoordinate");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = uvId,
            fromPin = "",
            toExpression = arId,
            toPin = "Coordinates",
        }), "connected", "connect TextureCoordinate -> MapARPassthroughCameraUV.Coordinates");

        // Step 3: link the AR node's output -> BaseColor (the doc's camera-feed base)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = arId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect MapARPassthroughCameraUV -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_AR1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_AR1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionMapARPassthroughCameraUV", 1),
            ("MaterialExpressionTextureCoordinate", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_AR1_B" }),
            "saved", "save_asset Mat_AR1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_AR1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_AR1_B",
            ("MaterialExpressionMapARPassthroughCameraUV", 1),
            ("MaterialExpressionTextureCoordinate", 1));
    }

    // =====================================================================
    // Q2: AtmosphericLightVector -> Normal
    // =====================================================================

    private static void BuildQ2_AtmosphericLightVector(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_ALV1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ALV1_A");

        // Step 2: add AtmosphericLightVector (no properties, one output)
        string alvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionAtmosphericLightVector",
        }), "add MaterialExpressionAtmosphericLightVector");

        // Step 3: link its output -> Normal
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = alvId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Normal",
        }), "connected", "connect AtmosphericLightVector -> Material.Normal");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_ALV1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_ALV1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // per-asset self-comparison
        ExpectExpressions(ctx, a2, ("MaterialExpressionAtmosphericLightVector", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ALV1_B" }),
            "saved", "save_asset Mat_ALV1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ALV1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_ALV1_B", ("MaterialExpressionAtmosphericLightVector", 1));
    }

    // =====================================================================
    // Q3: Dot(MapAR(TexCoord), AtmosphericLightVector) -> EmissiveColor
    // =====================================================================

    private static void BuildQ3_ARAtmosphericDot(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_ARALV1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ARALV1_A");

        // Step 2: add MapARPassthroughCameraUV and AtmosphericLightVector
        string arId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMapARPassthroughCameraUV",
        }), "add MaterialExpressionMapARPassthroughCameraUV");

        string alvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionAtmosphericLightVector",
        }), "add MaterialExpressionAtmosphericLightVector");

        // Feed the AR node's required Coordinates input (same engine-fact addition as Q1)
        string uvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionTextureCoordinate",
        }), "add MaterialExpressionTextureCoordinate");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = uvId,
            fromPin = "",
            toExpression = arId,
            toPin = "Coordinates",
        }), "connected", "connect TextureCoordinate -> MapARPassthroughCameraUV.Coordinates");

        // Step 3: add DotProduct, A = AR, B = atmospheric light vector
        string dotId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDotProduct",
        }), "add MaterialExpressionDotProduct");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = arId,
            fromPin = "",
            toExpression = dotId,
            toPin = "A",
        }), "connected", "connect MapARPassthroughCameraUV -> Dot.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = alvId,
            fromPin = "",
            toExpression = dotId,
            toPin = "B",
        }), "connected", "connect AtmosphericLightVector -> Dot.B");

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
            "compiled", "compile_material Mat_ARALV1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_ARALV1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // per-asset self-comparison
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionMapARPassthroughCameraUV", 1),
            ("MaterialExpressionAtmosphericLightVector", 1),
            ("MaterialExpressionTextureCoordinate", 1),
            ("MaterialExpressionDotProduct", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ARALV1_B" }),
            "saved", "save_asset Mat_ARALV1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ARALV1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_ARALV1_B",
            ("MaterialExpressionMapARPassthroughCameraUV", 1),
            ("MaterialExpressionAtmosphericLightVector", 1),
            ("MaterialExpressionTextureCoordinate", 1),
            ("MaterialExpressionDotProduct", 1));
    }
}
