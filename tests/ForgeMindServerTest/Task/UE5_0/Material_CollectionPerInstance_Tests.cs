// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 100 - Material Collection And Per Instance Parameters (UE 5.0)
//           - from the UE 5.0 material task doc "material - collection / instance
//             parameters" (Tasks material group; the doc file name is the Chinese
//             phrase for that, which cannot be written in source)
//
// UMaterialExpressionCollectionParameter, UMaterialExpressionDynamicParameter,
// UMaterialExpressionPerInstanceRandom and UMaterialExpressionPerInstanceCustomData,
// with UMaterialExpressionMultiply (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on MaterialExpressionCollectionParameter / DynamicParameter /
//     PerInstanceRandom / PerInstanceCustomData / PerInstanceFadeAmount / Multiply -> 1 match each.
//   * Header facts that decide the shape of this case:
//       - CollectionParameter carries "Collection" (TObjectPtr<UMaterialParameterCollection>)
//         and "ParameterName" (FName) (MaterialExpressionCollectionParameter.h).
//       - DynamicParameter carries ParamNames (TArray<FString>), DefaultValue (FLinearColor) and
//         ParameterIndex (uint32) (MaterialExpressionDynamicParameter.h) - the doc's "Channel"
//         is not a property of the node.
//       - PerInstanceCustomData carries the optional input DefaultValue, its authored twin
//         ConstDefaultValue and DataIndex (uint32) (MaterialExpressionPerInstanceCustomData.h).
//       - PerInstanceRandom has no properties and one output.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 2 asks for Domain=Particle. UE 5.0.3 has no such domain: EMaterialDomain
//     (MaterialShared.h) declares Surface / DeferredDecal / LightFunction / Volume /
//     PostProcess / UI / RuntimeVirtualTexture only. The node itself has no particle
//     dependency, so the domain becomes Surface while BlendMode=Additive is kept as the doc
//     names it. The same replacement was made in No.86 for the same node.
//   * Question 2's "Channel = 0" has no counterpart; the node's own index property is
//     ParameterIndex and its default is already 0, so nothing is sent for it.
//   * Question 1's collection "/Game/Collections/C_Global" does not exist and that directory
//     is not cleared between runs. The case creates its own collection under /Game/Tests/UE5_0
//     (the group lifecycle's directory) through create_material_parameter_collection, with a
//     vector parameter named "Tint" - a collection without the entry is something the engine
//     refuses to compile - and reloads it from disk before referencing it, for the same reason
//     No.86 documents (the uniform-buffer layout is prepared on the load path).
//   * Question 1 also cannot keep the doc's asset names: Mat_Coll1_A/B are the assets No.86
//     already builds for its own CollectionParameter question, so this question writes
//     Mat_CollP1_A/B. Two cases sharing one asset would make each round depend on which of
//     them ran last.
//
// Overlap worth flagging to the doc's owner: question 1 (CollectionParameter) and question 2
// (DynamicParameter) are the same two nodes No.86 builds, and question 3 of Material-Parameter Supplement covers
// DoubleVectorParameter. Only question 3 of this doc - the per-instance pair - was uncovered
// before this case. The doc is transcribed as written rather than trimmed to the new part.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// Question 1 shares one material parameter collection between both channels, so its parity read is
// also the check that the two channels addressed the same collection entry rather than two
// differently resolved object references.
// =========================================================================

public sealed class Material_CollectionPerInstance : ITestCase
{
    public string Name => "Material Collection And Per Instance Parameters (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 100;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_CollectionParameter(ctx);
        BuildQ2_DynamicParameter(ctx);
        BuildQ3_PerInstanceRandomAndCustomData(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: CollectionParameter (MPC_Global.Tint) -> BaseColor
    // =====================================================================

    private static void BuildQ1_CollectionParameter(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string collAsset = "/Game/Tests/UE5_0/MPC_Global";
        const string a1 = "/Game/Tests/UE5_0/Mat_CollP1_A";

        // Step 1: the collection the node reads, created fresh for this round
        StepOk(ctx, EditorCall(ctx, "create_material_parameter_collection", new
        {
            path = collAsset,
            vectors = new object[] { new { name = "Tint", defaultValue = "1,0.5,0,1" } },
        }), "created", "create_material_parameter_collection MPC_Global(Tint)");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = collAsset }),
            "saved", "save_asset MPC_Global");
        // Reload it from disk before anything references it: the collection's uniform-buffer
        // layout is prepared on the load path, and a material compiled against a collection that
        // never went through it asserts inside the shader compile (the crash No.86 documents)
        StepOk(ctx, EditorCall(ctx, "reload_asset", new { path = collAsset }),
            "reloaded", "reload_asset MPC_Global");

        // Step 2: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_CollP1_A");

        // Step 3: add CollectionParameter
        string collParamId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionCollectionParameter",
            Collection = collAsset + ".MPC_Global",
            ParameterName = "Tint",
        }), "add MaterialExpressionCollectionParameter(MPC_Global, Tint)");

        // Step 4: link -> BaseColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = collParamId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect CollectionParameter -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_CollP1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_CollP1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionCollectionParameter", 1));
        ExpectOutputLink(ctx, a1, "BaseColor", "MaterialExpressionCollectionParameter");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        const string b1 = "/Game/Tests/UE5_0/Mat_CollP1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_CollP1_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionCollectionParameter", 1));
        ExpectOutputLink(ctx, b1, "BaseColor", "MaterialExpressionCollectionParameter");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: DynamicParameter -> EmissiveColor (Additive, Surface per divergence note)
    // =====================================================================

    private static void BuildQ2_DynamicParameter(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_DynP1_A";

        // create_material answers created=true even when a setting did not land, so the two
        // error fields are asserted as well - a material the engine left in another blend mode
        // would otherwise pass this step
        var createA2 = EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",   // the doc's Particle domain does not exist in 5.0.3, see header
            blendMode = "Additive",
        });
        StepOk(ctx, createA2, "created", "create_material Mat_DynP1_A");
        ctx.AssertTrue(GetBool(createA2, "settingsApplied"),
            $"create_material Mat_DynP1_A -> created, but the settings did not land: {createA2.ToJsonString()}");
        ctx.AssertTrue(GetStr(createA2, "blendModeError").Length == 0,
            $"create_material Mat_DynP1_A -> the engine rejected the blend mode: {GetStr(createA2, "blendModeError")}");

        string dynId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionDynamicParameter",
        }), "add MaterialExpressionDynamicParameter");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = dynId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect DynamicParameter -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_DynP1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_DynP1_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionDynamicParameter", 1));
        ExpectOutputLink(ctx, a2, "EmissiveColor", "MaterialExpressionDynamicParameter");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        const string b2 = "/Game/Tests/UE5_0/Mat_DynP1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_DynP1_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionDynamicParameter", 1));
        ExpectOutputLink(ctx, b2, "EmissiveColor", "MaterialExpressionDynamicParameter");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: PerInstanceRandom x PerInstanceCustomData(0) -> Multiply -> BaseColor
    // =====================================================================

    private static void BuildQ3_PerInstanceRandomAndCustomData(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_PIR1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_PIR1_A");

        string rndId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionPerInstanceRandom",
        }), "add MaterialExpressionPerInstanceRandom");

        string dataId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionPerInstanceCustomData",
            DataIndex = 0,
        }), "add MaterialExpressionPerInstanceCustomData(DataIndex=0)");

        string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = rndId,
            fromPin = "",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect PerInstanceRandom -> Multiply.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = dataId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect PerInstanceCustomData -> Multiply.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = mulId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Multiply -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_PIR1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_PIR1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionPerInstanceRandom", 1),
            ("MaterialExpressionPerInstanceCustomData", 1),
            ("MaterialExpressionMultiply", 1));
        ExpectOutputLink(ctx, a3, "BaseColor", "MaterialExpressionMultiply");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionMultiply", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        const string b3 = "/Game/Tests/UE5_0/Mat_PIR1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_PIR1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionPerInstanceRandom", 1),
            ("MaterialExpressionPerInstanceCustomData", 1),
            ("MaterialExpressionMultiply", 1));
        ExpectOutputLink(ctx, b3, "BaseColor", "MaterialExpressionMultiply");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionMultiply", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
