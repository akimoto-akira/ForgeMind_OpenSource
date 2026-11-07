// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 95 - Material Custom Normal Outputs (UE 5.0)
//        - from the UE 5.0 material task doc "material - custom normal output"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - custom normal output", which cannot be written in source)
//
// UMaterialExpressionBentNormalCustomOutput and UMaterialExpressionClearCoatNormalCustomOutput
// - both derived from UMaterialExpressionCustomOutput - plus UMaterialExpressionVertexNormalWS
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on VertexNormalWS / BentNormalCustomOutput /
//     ClearCoatNormalCustomOutput / CustomOutput -> 1 match each.
//   * Header facts that decide the shape of this case:
//       - UMaterialExpressionCustomOutput is declared UCLASS(abstract, ...) - it has no class
//         default object, so the node cannot be created at all; only its concrete
//         subclasses exist as graph nodes.
//       - Both concrete subclasses expose exactly one input named "Input"
//         (MaterialExpressionBentNormalCustomOutput.h, MaterialExpressionClearCoatNormalCustomOutput.h).
//   * ForgeMind.SpkTool "translate" on the B-side DSL below -> 0 diagnostics.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Questions 1 and 2 write their DSL as "[Output(BentNormal)]" / "[Output(ClearCoatNormal)]",
//     i.e. as if the material had such an output pin. It does not: the custom-output NODE *is*
//     the channel - being in the graph with its Input wired is what writes the GBuffer channel
//     (the same shape as No.83's volumetric / absorption outputs). So both channels build
//     VertexNormalWS -> <node>.Input and assign nothing to any material output.
//   * Question 3 asks for the abstract base UMaterialExpressionCustomOutput itself. The name is
//     real (it resolves in engine.spk) but the class is declared UCLASS(abstract) with no class
//     default object, so no editor - bridge or otherwise - can instantiate it; it is not a graph
//     node, only its concrete subclasses are. The step is therefore left out (the same call No.93
//     made for the doc's non-existent MeshPaintTexture node) instead of written as an add that can
//     only fail - a case that is guaranteed to fail cannot stay in the suite. It is not silently
//     swapped for a concrete subclass either: that would change what the doc asks, and Q1 and Q2
//     already cover the two real custom-output subclasses.
//   * Question 2 keeps the doc's ShadingModel=ClearCoat on the material itself
//     (MSM_ClearCoat, addressed by its bare enum name, which the bridge's enum resolver
//     accepts) and reads the switch back so a silently ignored key cannot pass as a built
//     material.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// For these custom-output nodes that parity is the real check: nothing is wired to a material
// output, so it is the VertexNormalWS -> <node>.Input link (and the node being in the graph at
// all) that the two channels must agree on. Coverage is the step commands' business flags, the
// read-back count of the named expressions, the ClearCoat setting read on question 2, plus this
// snapshot layer.
// =========================================================================

public sealed class Material_CustomNormalOutputs : ITestCase
{
    public string Name => "Material Custom Normal Outputs (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 95;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_BentNormalCustomOutput(ctx);
        BuildQ2_ClearCoatNormalCustomOutput(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: VertexNormalWS -> BentNormalCustomOutput.Input
    // =====================================================================

    private static void BuildQ1_BentNormalCustomOutput(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_BentN1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_BentN1_A");

        // Step 2: the bent-normal source
        string normalId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionVertexNormalWS",
        }), "add MaterialExpressionVertexNormalWS");

        // Step 3: the custom output node
        string bentId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionBentNormalCustomOutput",
        }), "add MaterialExpressionBentNormalCustomOutput");

        // Step 4: feed its only input - the node in the graph is the output channel itself
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = normalId,
            fromPin = "",
            toExpression = bentId,
            toPin = "Input",
        }), "connected", "connect VertexNormalWS -> BentNormalCustomOutput.Input");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_BentN1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_BentN1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionVertexNormalWS", 1),
            ("MaterialExpressionBentNormalCustomOutput", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_BentN1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_BentN1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionVertexNormalWS", 1),
            ("MaterialExpressionBentNormalCustomOutput", 1));
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: VertexNormalWS -> ClearCoatNormalCustomOutput.Input, ClearCoat shading model
    // =====================================================================

    private static void BuildQ2_ClearCoatNormalCustomOutput(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_CCNormal1_A";

        // Step 1: create material in the ClearCoat shading model
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
            ShadingModel = "ClearCoat",
        }), "created", "create_material Mat_CCNormal1_A");

        // Step 2: the source normal
        string normalId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionVertexNormalWS",
        }), "add MaterialExpressionVertexNormalWS");

        // Step 3: the clear-coat custom output node
        string ccId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionClearCoatNormalCustomOutput",
        }), "add MaterialExpressionClearCoatNormalCustomOutput");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = normalId,
            fromPin = "",
            toExpression = ccId,
            toPin = "Input",
        }), "connected", "connect VertexNormalWS -> ClearCoatNormalCustomOutput.Input");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_CCNormal1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_CCNormal1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionVertexNormalWS", 1),
            ("MaterialExpressionClearCoatNormalCustomOutput", 1));
        ExpectMaterialSetting(ctx, a2, "ShadingModel", "ClearCoat");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_CCNormal1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_CCNormal1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionVertexNormalWS", 1),
            ("MaterialExpressionClearCoatNormalCustomOutput", 1));
        ExpectMaterialSetting(ctx, b2, "ShadingModel", "ClearCoat");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }
}
