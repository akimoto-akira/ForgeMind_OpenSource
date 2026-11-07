// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 87 - Material Layers And Channel Mask (UE 5.0)
//        - from the UE 5.0 material task doc "material - layer / channel parameter"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - layer / channel parameter", which cannot be written in source)
//
// UMaterialExpressionChannelMaskParameter, UMaterialExpressionMaterialAttributeLayers,
// UMaterialExpressionMaterialLayerOutput (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on the three classes -> 1 match each, no NoDeclaration.
//   * ForgeMind.SpkTool "translate" on all three B-side DSL below -> 0 diagnostics.
//   * Property names come from the engine headers, which is what decides this file's shape:
//       - UMaterialExpressionChannelMaskParameter derives from
//         UMaterialExpressionVectorParameter and adds exactly one own UPROPERTY,
//         "MaskChannel" (TEnumAsByte<EChannelMaskParameterColor::Type>, values
//         Red / Green / Blue / Alpha); the value pin is "Input" and the parameter name is
//         the inherited "Name" (MaterialExpressionChannelMaskParameter.h).
//       - UMaterialExpressionMaterialLayerOutput derives from
//         UMaterialExpressionFunctionOutput, so its wiring member is "A" and it declares no
//         property of its own; GetInputType() returns MCT_MaterialAttributes, i.e. the pin
//         carries an attribute bundle (MaterialExpressionMaterialLayerOutput.h).
//       - UMaterialExpressionMaterialAttributeLayers carries "Input"
//         (FMaterialAttributesInput) plus the layer caller arrays
//         (MaterialExpressionMaterialAttributeLayers.h).
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 1 asks the channel mask parameter for "R=true, G=true, B=false, A=false" -
//     a two-channel mask. The class carries a single-channel enum, so no combination is
//     expressible; the first requested channel (Red) is what the node can actually hold, and
//     that is what both channels write. The doc's "Name=Channels" is kept: "Name" is a real
//     inherited property.
//   * Question 1's texture is the doc's "/Game/T_Brick", which does not exist in the project.
//     Following the No.74 ruling (a material reference must resolve; take a real asset, never
//     modify the engine), the StarterContent brick texture /Game/StarterContent/Textures/
//     T_Brick_Clay_New_D is used on both channels.
//   * Question 1 wires the texture through "RGBA". That is the TextureSample's own declared
//     sixth output (engine output list RGB=0, R=1, G=2, B=3, A=4, RGBA=5), so the request is
//     a named output - index 5, connected directly - and not a channel swizzle; both channels
//     land on outputIndex 5.
//   * Question 3's "LayerType = top layer" (the doc's wording) has no landing point: UMaterialExpressionMaterialLayerOutput
//     declares no such property (only the inherited OutputName / OutputType of a function
//     output). The key is dropped on both channels rather than aimed at an unrelated property,
//     and the wiring uses the real member name "A" instead of the doc's "Input(A)" call.
//   * Questions 2 and 3 ask for UseMaterialAttributes=true. The engine property is
//     bUseMaterialAttributes (Material.h:889), the key the bridge already applies to any extra
//     create_material payload key and reads back in materialSettings - the No.80 form is reused
//     verbatim, including the read-back assertion that the switch really landed.
//
// Multi-output convention (user ruling, see Material_Decal_Tests.cs): outputs are taken from
// the first declared pin unless the question asks for another by name, as question 1 does for
// the texture's RGBA.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) inserted on user instruction per
// collaboration spec section 9: one call per built asset, right after that asset's save -
// A side after compile_material + save_asset, B side after ApplyDsl + save_asset (material
// assets are not saved by the DSL apply, so the save stays explicit). First run seeds the
// six baselines under TestRuns/5.0/, the second run has to report them all as a match.
// =========================================================================

public sealed class Material_LayersAndChannelMask : ITestCase
{
    public string Name => "Material Layers And Channel Mask (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 87;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_ChannelMaskParameter(ctx);
        BuildQ2_MaterialAttributeLayers(ctx);
        BuildQ3_MaterialLayerOutput(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: TextureSample.RGBA -> ChannelMaskParameter -> BaseColor
    // =====================================================================

    private static void BuildQ1_ChannelMaskParameter(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_ChanMask1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ChanMask1_A");

        // Step 2: add the texture sample - the reference has to resolve, so the real
        // StarterContent brick texture replaces the doc's missing /Game/T_Brick (No.74 ruling)
        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Brick_Clay_New_D.T_Brick_Clay_New_D",
        }), "add MaterialExpressionTextureSample(T_Brick_Clay_New_D)");

        // Step 3: add ChannelMaskParameter. "MaskChannel" is the engine property name and it
        // holds one channel; the doc's R+G pair is not expressible, so Red (its first set
        // channel) is written. The parameter's name is the base class's "ParameterName" - the step
        // command takes it under the "name" key, which is not a property name itself.
        string maskId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionChannelMaskParameter",
            name = "Channels",
            MaskChannel = "Red",
        }), "add MaterialExpressionChannelMaskParameter(Channels, MaskChannel=Red)");

        // Step 4a: link Tex.RGBA -> the mask's Input. RGBA is the texture sample's own named
        // output (index 5), so the wire is a named-output request, not a channel extraction.
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = texId,
            fromPin = "RGBA",
            toExpression = maskId,
            toPin = "Input",
        }), "connected", "connect Tex.RGBA -> ChannelMaskParameter.Input");

        // Step 4b: mask output -> BaseColor (single declared output)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = maskId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect ChannelMaskParameter -> Material.BaseColor");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_ChanMask1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_ChanMask1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionChannelMaskParameter", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ChanMask1_B" }),
            "saved", "save_asset Mat_ChanMask1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ChanMask1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_ChanMask1_B",
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionChannelMaskParameter", 1));
    }

    // =====================================================================
    // Q2: MaterialAttributeLayers -> MaterialAttributes (attribute-bundle mode)
    // =====================================================================

    private static void BuildQ2_MaterialAttributeLayers(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Layers1_A";

        // Step 1: create material with the attribute-bundle switch on (engine property
        // bUseMaterialAttributes; the create command applies every non-envelope payload key and
        // reports a failure through propertyErrors rather than dropping the key)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
            bUseMaterialAttributes = true,
        }), "created", "create_material Mat_Layers1_A");

        // Step 2: add MaterialAttributeLayers
        string layersId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionMaterialAttributeLayers",
        }), "add MaterialExpressionMaterialAttributeLayers");

        // Step 3: link its output -> MaterialAttributes
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = layersId,
            fromPin = "",
            toExpression = "Material",
            toPin = "MaterialAttributes",
        }), "connected", "connect MaterialAttributeLayers -> Material.MaterialAttributes");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Layers1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Layers1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        ExpectExpressions(ctx, a2, ("MaterialExpressionMaterialAttributeLayers", 1));
        // The switch is a bool property, so the read side reports it as a JSON boolean - the
        // shared string-valued ExpectMaterialSetting does not apply; the value is asserted
        // straight off the settings object instead.
        ctx.AssertTrue(BlueprintTestHelper.ReadMaterialSettings(ctx, a2)["bUseMaterialAttributes"]?.GetValue<bool>() == true,
            $"{a2}: materialSettings.bUseMaterialAttributes did not land as true");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Layers1_B" }),
            "saved", "save_asset Mat_Layers1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Layers1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Layers1_B", ("MaterialExpressionMaterialAttributeLayers", 1));
        ctx.AssertTrue(BlueprintTestHelper.ReadMaterialSettings(ctx, "/Game/Tests/UE5_0/Mat_Layers1_B")["bUseMaterialAttributes"]?.GetValue<bool>() == true,
            "/Game/Tests/UE5_0/Mat_Layers1_B: materialSettings.bUseMaterialAttributes did not land as true");
    }

    // =====================================================================
    // Q3: MakeMaterialAttributes -> MaterialLayerOutput.A (no material output pin)
    // =====================================================================

    private static void BuildQ3_MaterialLayerOutput(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_LayerOut1_A";

        // Step 1: create material in attribute-bundle mode
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
            bUseMaterialAttributes = true,
        }), "created", "create_material Mat_LayerOut1_A");

        // Step 2: the red base colour constant
        string baseId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add MaterialExpressionConstant3Vector(1,0,0)");

        // Step 3: MakeMaterialAttributes carrying that base colour
        string makeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMakeMaterialAttributes",
        }), "add MaterialExpressionMakeMaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = baseId,
            fromPin = "",
            toExpression = makeId,
            toPin = "BaseColor",
        }), "connected", "connect Base -> MakeMaterialAttributes.BaseColor");

        // Step 4: add MaterialLayerOutput. The class declares no LayerType property (its own
        // header adds none), so the doc's "top layer" type key has no landing point and nothing is written
        // for it; the node is a function-output subclass, so the wiring member is "A".
        string layerOutId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMaterialLayerOutput",
        }), "add MaterialExpressionMaterialLayerOutput");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = makeId,
            fromPin = "",
            toExpression = layerOutId,
            toPin = "A",
        }), "connected", "connect Make -> MaterialLayerOutput.A");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_LayerOut1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_LayerOut1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionMakeMaterialAttributes", 1),
            ("MaterialExpressionMaterialLayerOutput", 1));
        ctx.AssertTrue(BlueprintTestHelper.ReadMaterialSettings(ctx, a3)["bUseMaterialAttributes"]?.GetValue<bool>() == true,
            $"{a3}: materialSettings.bUseMaterialAttributes did not land as true");

        // -- B-side: DSL --
        // The layer output consumes the bundle through its inherited "A" member and is itself
        // the graph's output, so no material output pin is assigned on either channel.
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_LayerOut1_B" }),
            "saved", "save_asset Mat_LayerOut1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_LayerOut1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_LayerOut1_B",
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionMakeMaterialAttributes", 1),
            ("MaterialExpressionMaterialLayerOutput", 1));
        ctx.AssertTrue(BlueprintTestHelper.ReadMaterialSettings(ctx, "/Game/Tests/UE5_0/Mat_LayerOut1_B")["bUseMaterialAttributes"]?.GetValue<bool>() == true,
            "/Game/Tests/UE5_0/Mat_LayerOut1_B: materialSettings.bUseMaterialAttributes did not land as true");
    }
}
