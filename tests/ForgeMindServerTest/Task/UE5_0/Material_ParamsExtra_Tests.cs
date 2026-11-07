// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 86 - Material Parameters Extra (UE 5.0)
//        - from the UE 5.0 material task doc "material - parameter supplement"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - parameter supplement", which cannot be written in source)
//
// Parameterisation and constant expressions beyond the scalar / vector family:
//   UMaterialExpressionDynamicParameter, UMaterialExpressionCollectionParameter,
//   UMaterialExpressionDoubleVectorParameter, UMaterialExpressionGenericConstant
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on each of the four classes -> 1 match each, no
//     NoDeclaration, so the scan index is fine and this is transcription work.
//   * ForgeMind.SpkTool "translate" on every B-side DSL below -> 0 diagnostics.
//   * Property names were taken from the engine headers, not the doc's wording:
//       - DynamicParameter carries "ParameterIndex" (uint32), not the doc's
//         "ParamIndex"/"Index" (MaterialExpressionDynamicParameter.h). The step
//         command and the DSL both write the real name.
//       - CollectionParameter carries "Collection" (TObjectPtr<UMaterialParameterCollection>)
//         and "ParameterName" (FName) (MaterialExpressionCollectionParameter.h).
//       - DoubleVectorParameter carries "DefaultValue" (FVector4d) and the shared
//         parameter "Name" (MaterialExpressionDoubleVectorParameter.h +
//         UMaterialExpressionParameter); a double vector is not an FLinearColor, so
//         the default goes through the struct's own text form (X=/Y=/Z=/W=).
//       - UMaterialExpressionGenericConstant is declared UCLASS(abstract) and carries NO
//         property at all - only the pure virtual GetConstantValue(). The double constant the
//         doc means is its concrete subclass UMaterialExpressionConstantDouble, which owns
//         "Value" (double) (MaterialExpressionGenericConstant.h:9-36).
//
// Doc/engine divergences, transcribed per the doc's step lists and written with real
// engine names (no silent fallbacks, spec section 8 / section 6):
//   * Question 1 asks for the "RGB" read of the dynamic parameter. DynamicParameter is
//     one of the few expressions that declare their own output list, and RGB is its
//     first declared output, so the wire leaves the pin at its default (index 0) on both
//     channels - the multi-output convention (take the first output unless the question
//     asks for another pin) and the doc's intent agree here.
//   * Question 1's header also asks Domain=Particle. UE 5.0.3 has no particle material domain:
//     EMaterialDomain (MaterialShared.h:244) lists Surface / DeferredDecal / LightFunction /
//     Volume / PostProcess / UI / RuntimeVirtualTexture, and UMaterialExpressionDynamicParameter::
//     Compile has no particle dependency (it emits Compiler->DynamicParameter(DefaultValue,
//     ParameterIndex)), so both channels ask for Surface while keeping the doc's BlendMode=
//     Additive and the node itself. Sending the impossible name used to fail only on the DSL
//     side - create_material answers created=true with the rejection in domainError and
//     settingsApplied=false - which is why this case now asserts those fields too.
//   * Question 2 references a material parameter collection the doc treats as an existing
//     asset; /Game/MPC_Demo is not in the project (Content has no MPC_Demo.uasset). An
//     unresolvable object reference is reported rather than stored as null, so the case creates
//     the container itself through create_material_parameter_collection - the same shape as
//     No. 79's material-function prerequisite - and keeps it under /Game/Tests/UE5_0, the
//     directory the group lifecycle clears every round (the rebuild-per-round rule from
//     directory the group lifecycle clears every round (the rebuild-per-round rule from
//     2026-10-28). The command also fills the parameter list, because a collection without
//     the entry is something the engine refuses to compile: "Tint" is created there as a
//     vector parameter, exactly the kind the node reads.
//   * Question 3 adds a "GenericConstant (Value = 2.0)" but never wires it (the doc's own
//     step 4 links only the double vector). The base class is abstract and has no property, so
//     the node that carries a double constant is its concrete subclass ConstantDouble - the
//     engine fact names it, the doc only named the family - and it is created unwired on both
//     channels so the graphs stay twins and the node is still counted. The first run proved the
//     base is not usable: add_material_expression answered "property 'value' not found on
//     MaterialExpressionGenericConstant_0" (run 20261029_1534).
//
// Multi-output pin convention (user ruling, see Material_Decal_Tests.cs): an expression
// with several outputs is taken from its first output; a different pin has to be asked
// for by its declared output name (A side fromPin / B side "Node.Name"), never by a
// channel swizzle, which means something else (an inserted ComponentMask).
//
// IR snapshot (VerifyIrSnapshot): the user named this file and asked for the
// mistaken-baseline comparison, so each asset gets one call point right after its save_asset,
// before the read-back assertions - the same shape No. 82~85 use: the asset path is handed to
// the helper (it derives the baseline name itself) and each channel is compared against its own
// previous run, without a cross-channel argument. Six call points, one per asset; no existing
// assertion was removed or reordered (spec section 9: adding a verification point counts as
// modifying the case, so it happens only on request). The first run writes the baseline under
// TestRuns/<run>/snapshot/<version>/<asset>.json as a fresh file and passes; later runs compare
// byte-wise after normalization and stop on mismatch.
// =========================================================================

public sealed class Material_ParamsExtra : ITestCase
{
    public string Name => "Material Parameters Extra (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 86;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_DynamicParameter(ctx);
        BuildQ2_CollectionParameter(ctx);
        BuildQ3_DoubleVectorAndConstantDouble(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: DynamicParameter (first output) -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_DynamicParameter(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Dyn1_A";

        // Step 1: create material. The doc's header asks Domain=Particle, but UE 5.0.3 has
        // no such domain: EMaterialDomain (MaterialShared.h:244) declares Surface / DeferredDecal /
        // LightFunction / Volume / PostProcess / UI / RuntimeVirtualTexture only - the particle
        // domain was dropped from the engine long ago. The node itself is unaffected:
        // UMaterialExpressionDynamicParameter::Compile is Compiler->DynamicParameter(DefaultValue,
        // ParameterIndex) with no particle dependency (MaterialExpressions.cpp), so the question's
        // subject stays intact and only the impossible domain is replaced. BlendMode=Additive is
        // kept exactly as the doc names it.
        //
        // create_material answers created=true even when a setting did not land - the
        // domain/blend failures land in domainError/blendModeError with settingsApplied=false -
        // so those two are asserted here as well; without them a material the engine silently
        // left in another domain still passed this step.
        var createA1 = EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Additive",
        });
        StepOk(ctx, createA1, "created", "create_material Mat_Dyn1_A");
        ctx.AssertTrue(GetBool(createA1, "settingsApplied"),
            $"create_material Mat_Dyn1_A -> created, but the settings did not land: {createA1.ToJsonString()}");
        ctx.AssertTrue(GetStr(createA1, "domainError").Length == 0,
            $"create_material Mat_Dyn1_A -> the engine rejected the domain: {GetStr(createA1, "domainError")}");
        ctx.AssertTrue(GetStr(createA1, "blendModeError").Length == 0,
            $"create_material Mat_Dyn1_A -> the engine rejected the blend mode: {GetStr(createA1, "blendModeError")}");

        // Step 2: add DynamicParameter. The doc's "ParamIndex" is not the engine
        // property name; the header declares ParameterIndex, and that is what is sent.
        string dynId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionDynamicParameter",
            ParameterIndex = 0,
        }), "add MaterialExpressionDynamicParameter(ParameterIndex=0)");

        // Step 3: link its first declared output (the doc's RGB read) -> EmissiveColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = dynId,
            fromPin = "",             // first declared output
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect DynamicParameter -> Material.EmissiveColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Dyn1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Dyn1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison
        ExpectExpressions(ctx, a1, ("MaterialExpressionDynamicParameter", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Dyn1_B" }),
            "saved", "save_asset Mat_Dyn1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Dyn1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Dyn1_B", ("MaterialExpressionDynamicParameter", 1));
    }

    // =====================================================================
    // Q2: CollectionParameter (MPC reference) -> BaseColor
    // =====================================================================

    private static void BuildQ2_CollectionParameter(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string collAsset = "/Game/Tests/UE5_0/MPC_Demo";
        const string a2 = "/Game/Tests/UE5_0/Mat_Coll1_A";

        // Step 0: create the parameter collection the node points at, with the entry the node
        // names. An empty collection resolves but has nothing to read, and the engine then
        // rejects the material while compiling it - which the suite could not see either until
        // compile_material started reporting shader errors. /Game/Tests is cleared every round,
        // so the container is rebuilt each run rather than relying on a leftover; the create_*
        // commands also refuse to overwrite, so nothing persists between rounds.
        StepOk(ctx, EditorCall(ctx, "create_material_parameter_collection", new
        {
            path = collAsset,
            vectors = new object[] { new { name = "Tint", defaultValue = "1,0.5,0,1" } },
        }), "created", "create_material_parameter_collection MPC_Demo(Tint)");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = collAsset }),
            "saved", "save_asset MPC_Demo");
        // Reload it from disk before anything references it: the collection's uniform-buffer
        // layout is prepared on the load path, and a material compiled against a collection
        // that never went through it asserts inside the shader compile (the crash behind
        // run 20261029_1606's follow-up).
        StepOk(ctx, EditorCall(ctx, "reload_asset", new { path = collAsset }),
            "reloaded", "reload_asset MPC_Demo");

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Coll1_A");

        // Step 2: add CollectionParameter pointing at the collection created above.
        // The reference has to resolve, so the key carries the saved asset path; an
        // unresolvable object path is reported through propertyErrors rather than leaving a
        // null Collection behind (the same hard rule the material texture reference follows).
        string collId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionCollectionParameter",
            Collection = collAsset + ".MPC_Demo",
            ParameterName = "Tint",
        }), "add MaterialExpressionCollectionParameter(MPC_Demo, Tint)");

        // Step 3: link its output -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = collId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect CollectionParameter -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Coll1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Coll1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // per-asset self-comparison
        ExpectExpressions(ctx, a2, ("MaterialExpressionCollectionParameter", 1));

        // -- B-side: DSL in the documented material syntax --
        // The collection already exists on disk by now (the A side created and saved it in
        // this same session), so the DSL side resolves against the very same asset - the two
        // channels must point at one object for the graphs to be comparable.
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Coll1_B" }),
            "saved", "save_asset Mat_Coll1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Coll1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Coll1_B", ("MaterialExpressionCollectionParameter", 1));
    }

    // =====================================================================
    // Q3: DoubleVectorParameter -> BaseColor, plus the doc's unwired double constant
    // =====================================================================

    private static void BuildQ3_DoubleVectorAndConstantDouble(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_DV1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_DV1_A");

        // Step 2: add DoubleVectorParameter (Name=DV, DefaultValue=(1,2,3,4) in the
        // struct's own text form - FVector4d is not an FLinearColor, so the component
        // names are X/Y/Z/W)
        string dvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDoubleVectorParameter",
            name = "DV",
            defaultValue = "(X=1,Y=2,Z=3,W=4)",
        }), "add MaterialExpressionDoubleVectorParameter(DV)");

        // Step 3: add the double constant the doc means (Value=2.0). Its named base class
        // GenericConstant is abstract and property-less, so the concrete ConstantDouble is the
        // creatable node. Its step list creates it but never wires it, so it stays
        // unconnected on both channels.
        string gcId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstantDouble",
            Value = 2.0,
        }), "add MaterialExpressionConstantDouble(2.0)");
        _ = gcId;

        // Step 4: link the double vector -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = dvId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect DoubleVectorParameter -> Material.BaseColor");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_DV1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_DV1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // per-asset self-comparison
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionDoubleVectorParameter", 1),
            ("MaterialExpressionConstantDouble", 1));

        // -- B-side: DSL --
        // The double vector is declared in node form on purpose: the IR parameter types are
        // Scalar / LinearColor / Texture / Bool only, so a double vector has no [Parameter]
        // spelling, while "new DoubleVectorParameter { ... }" lowers to exactly the
        // MaterialExpressionDoubleVectorParameter the step command creates. The parameter's name
        // is its engine property ParameterName (UMaterialExpressionParameter), not "Name": the
        // latter is no property at all, and until the bridge warnings were checked for every
        // asset type the mistake only surfaced as a swallowed PROPERTY_NOT_FOUND - leaving a
        // parameter node with no identity on the DSL side while the step kept answering success.
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_DV1_B" }),
            "saved", "save_asset Mat_DV1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_DV1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_DV1_B",
            ("MaterialExpressionDoubleVectorParameter", 1),
            ("MaterialExpressionConstantDouble", 1));
    }
}
