// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 91 - Material Sky Atmosphere (UE 5.0)
//        - from the UE 5.0 material task doc "material - atmosphere / sky"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - atmosphere / sky", which cannot be written in source -
//          this is question 1 after the duplicate
//           questions were removed on user instruction)
//
// UMaterialExpressionSkyAtmosphereLightDirection (UE 5.0.3
// Materials/MaterialExpressionSkyAtmosphereLightDirection.h): the direction of the
// sky-atmosphere light, one output, no user-facing input pins.
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on MaterialExpressionSkyAtmosphereLightDirection ->
//     1 match, no NoDeclaration.
//   * ForgeMind.SpkTool "translate" on the B-side DSL below -> 0 diagnostics.
//
// Why this case has a single question: the doc originally carried three. Its former
// question 1 (AtmosphericLightVector -> Normal) duplicates what No.85 already builds and
// passed, and its former question 3 (CloudLayer -> BaseColor) shared the asset names
// Mat_Cloud1_A/B with No.83 - both were deleted on user instruction because the surviving
// copy of each had already passed testing. UMaterialExpressionCloudLayer does not exist in
// 5.0.3 either (that header only defines UMaterialExpressionCloudSampleAttribute).
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9). This single question carries three: the A
// asset's self baseline, the B asset's self baseline, and one A/B cross-channel parity read after B
// is saved. Each baseline freezes only its own channel's graph, so only the parity read asks whether
// the step commands and the DSL built the same graph. Coverage is the step commands' business
// flags, the read-back count of the named expression, plus this snapshot layer.
// =========================================================================

public sealed class Material_SkyAtmosphere : ITestCase
{
    public string Name => "Material Sky Atmosphere (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 91;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_SkyAtmosphereLightDirection(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: SkyAtmosphereLightDirection -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_SkyAtmosphereLightDirection(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Sky1_A";

        // Step 1: create material (the doc's DSL header: Surface / Opaque)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Sky1_A");

        // Step 2: add SkyAtmosphereLightDirection
        string skyId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionSkyAtmosphereLightDirection",
        }), "add MaterialExpressionSkyAtmosphereLightDirection");

        // Step 3: link its output -> EmissiveColor (single declared output)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = skyId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect SkyAtmosphereLightDirection -> Material.EmissiveColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Sky1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Sky1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionSkyAtmosphereLightDirection", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison

        // -- B-side: DSL in the documented material syntax --
        const string b1 = "/Game/Tests/UE5_0/Mat_Sky1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_Sky1_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionSkyAtmosphereLightDirection", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);   // per-asset self-comparison
        // A/B parity (B saved above)
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);
    }
}
