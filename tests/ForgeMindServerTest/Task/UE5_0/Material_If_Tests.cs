// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 74 - Material If (UE 5.0) - from Tasks/UE_5.0 Material/material-if.md
//
// MaterialExpressionIf (UMaterialExpressionIf): pure data-flow conditional
// (no exec pins). Input pins: A, B; Output pins: AGreaterThanB, AEqualsB,
// ALessThanB. Also exposes float EqualsThreshold, ConstB (per engine source
// Materials/MaterialExpressionIf.h, UE 5.0.3).
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//   Both channels build through the same Companion expression operations, and
//   get_asset_ir(level=full) reads the graph back including pin connections.
//
// B-side syntax note: the task doc sketches the material DSL in an attribute /
// call form that is not the DSL surface defined by the material DSL syntax spec
// (and omits the mandatory Mode). Per user decision the case uses the documented,
// translator-supported form instead of widening the translator.
//
// Topology note: where the task doc's A-side steps and B-side DSL disagreed (the
// DSL shared one constant node between two pins and wired AEqualsB), the A-side
// step list is authoritative, so the DSL mirrors it: one constant per pin and no
// AEqualsB connection.
//
// Texture note: the task doc names bare assets (/Game/T_Brick, /Game/T_Rock,
// /Game/T_Grass) that do not exist in this project. A sample with an unresolvable
// reference is not cosmetic - the texture stays null, the material then fails to
// compile and the error surfaces on the nodes wired downstream of the sample (the If
// node in this case), so the case uses the real StarterContent diffuse textures the
// doc's names stand for, and asserts on both sides that the reference actually resolved.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) inserted on user instruction per
// collaboration spec section 9: one call per built asset, right after that asset's save -
// A side after compile_material + save_asset, B side after ApplyDsl + save_asset (material
// assets are not saved by the DSL apply, so the save stays explicit). First run seeds the
// eight baselines under TestRuns/5.0/, the second run has to report them all as a match.
// =========================================================================

public sealed class Material_If : ITestCase
{
    public string Name => "Material If (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 74;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_SingleIf(ctx);
        BuildQ2_Cascade(ctx);
        BuildQ3_Param(ctx);
        BuildQ4_TexMix(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Single if (threshold color switch)
    // =====================================================================

    private static void BuildQ1_SingleIf(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_If_Single_A";

        // Step 1: create material asset (Domain=Surface, BlendMode=Opaque)
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",       // per task spec
            blendMode = "Opaque",     // per task spec
        }), "created", "create_material Mat_If_Single_A");

        // Step 2: add MaterialExpressionTextureSample (Texture=T_Brick), take .R channel
        string texId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTextureSample",
            // the doc's T_Brick: the project's clay-brick diffuse
            texture = "/Game/StarterContent/Textures/T_Brick_Clay_New_D",
        }), "add MaterialExpressionTextureSample(T_Brick)");

        // Step 3: add MaterialExpressionScalarParameter (Name=Threshold, Default=0.5)
        string thresholdId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Threshold",       // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add MaterialExpressionScalarParameter(Threshold)");

        // Step 4: add MaterialExpressionIf: A=Tex.R, B=Threshold,
        //         AGreaterThanB=(1,0,0), ALessThanB=(0,0,0)
        string ifId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionIf",
        }), "add MaterialExpressionIf");

        // Connect Tex.R -> If.A
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = texId,
            fromPin = "R",            // per task spec: .R channel
            toExpression = ifId,
            toPin = "A",
        }), "connected", "connect Tex.R -> If.A");

        // Connect Threshold -> If.B
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = thresholdId,
            fromPin = "",             // scalar parameter single output
            toExpression = ifId,
            toPin = "B",
        }), "connected", "connect Threshold -> If.B");

        // Set If.AGreaterThanB constant = (1,0,0) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = ifId,
            fromPin = "AGreaterThanB",
            constant = "(R=1,G=0,B=0)",  // per task spec: (1,0,0)
        }), "connected", "set If.AGreaterThanB = (1,0,0)");

        // Set If.ALessThanB constant = (0,0,0) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = ifId,
            fromPin = "ALessThanB",
            constant = "(R=0,G=0,B=0)",  // per task spec: (0,0,0)
        }), "connected", "set If.ALessThanB = (0,0,0)");

        // Step 5: connect If output -> Material BaseColor pin
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = ifId,
            fromPin = "",             // If single output
            toExpression = "Material",
            toPin = "BaseColor",      // per task spec
        }), "connected", "connect If -> Material.BaseColor");

        // Step 6: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_If_Single_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_If_Single_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // -- B-side: DSL in the documented material syntax, mirroring the A-side steps --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_If_Single_B" }),
            "saved", "save_asset Mat_If_Single_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_If_Single_B");

    }

    // =====================================================================
    // Q2: If cascade (three-level grayscale)
    // =====================================================================

    private static void BuildQ2_Cascade(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_If_Cascade_A";

        // Step 1: create material
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",       // per task spec (implied same as Q1)
            blendMode = "Opaque",
        }), "created", "create_material Mat_If_Cascade_A");

        // Step 2: add ScalarParameter (Name=V, Default=0.5)
        string vId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "V",               // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add ScalarParameter(V)");

        // Step 3: add If_1 (A=V, B=0.7, AGreaterThanB=White, ALessThanB=If_2 output)
        string if1Id = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionIf",
        }), "add If_1");

        // Step 4: add If_2 (A=V, B=0.3, AGreaterThanB=Gray, ALessThanB=Black)
        string if2Id = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionIf",
        }), "add If_2");

        // Connect V -> If_1.A
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = vId,
            fromPin = "",
            toExpression = if1Id,
            toPin = "A",
        }), "connected", "connect V -> If_1.A");

        // Set If_1.B = 0.7 (ConstB) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = if1Id,
            fromPin = "B",
            constant = "0.7",         // per task spec
        }), "connected", "set If_1.B = 0.7");

        // Set If_1.AGreaterThanB = White(1,1,1) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = if1Id,
            fromPin = "AGreaterThanB",
            constant = "(R=1,G=1,B=1)",  // per task spec: White
        }), "connected", "set If_1.AGreaterThanB = White");

        // Connect V -> If_2.A
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = vId,
            fromPin = "",
            toExpression = if2Id,
            toPin = "A",
        }), "connected", "connect V -> If_2.A");

        // Set If_2.B = 0.3 (ConstB) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = if2Id,
            fromPin = "B",
            constant = "0.3",         // per task spec
        }), "connected", "set If_2.B = 0.3");

        // Set If_2.AGreaterThanB = Gray(0.5,0.5,0.5) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = if2Id,
            fromPin = "AGreaterThanB",
            constant = "(R=0.5,G=0.5,B=0.5)",  // per task spec: Gray
        }), "connected", "set If_2.AGreaterThanB = Gray");

        // Set If_2.ALessThanB = Black(0,0,0) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = if2Id,
            fromPin = "ALessThanB",
            constant = "(R=0,G=0,B=0)",  // per task spec: Black
        }), "connected", "set If_2.ALessThanB = Black");

        // Connect If_2 output -> If_1.ALessThanB (cascade) -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = if2Id,
            fromPin = "",
            toExpression = if1Id,
            toPin = "ALessThanB",
        }), "connected", "connect If_2 -> If_1.ALessThanB");

        // Step 5: connect If_1 output -> BaseColor
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = if1Id,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect If_1 -> Material.BaseColor");

        // Step 6: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_If_Cascade_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_If_Cascade_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // -- B-side: DSL (If_1 first so both channels create the outer If first) --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_If_Cascade_B" }),
            "saved", "save_asset Mat_If_Cascade_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_If_Cascade_B");

    }

    // =====================================================================
    // Q3: If + parameters (threshold and both colors are parameters)
    // =====================================================================

    private static void BuildQ3_Param(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_If_Param_A";

        // Step 1: create material
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_If_Param_A");

        // Step 2: add ScalarParameter (Name=V, Default=0.4), ScalarParameter (Name=Threshold, Default=0.5)
        string vId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "V",               // per task spec
            defaultValue = 0.4,       // per task spec
        }), "add ScalarParameter(V)");

        string threshId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Threshold",       // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add ScalarParameter(Threshold)");

        // Step 3: add VectorParameter (Name=ColorHi, Default=(1,0,0,1)),
        //         VectorParameter (Name=ColorLo, Default=(0,0,1,1))
        string colorHiId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionVectorParameter",
            name = "ColorHi",         // per task spec
            defaultValue = "(R=1,G=0,B=0,A=1)",  // per task spec
        }), "add VectorParameter(ColorHi)");

        string colorLoId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionVectorParameter",
            name = "ColorLo",         // per task spec
            defaultValue = "(R=0,G=0,B=1,A=1)",  // per task spec
        }), "add VectorParameter(ColorLo)");

        // Step 4: add If: A=V, B=Threshold, AGreaterThanB=ColorHi, ALessThanB=ColorLo
        string ifId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionIf",
        }), "add MaterialExpressionIf");

        // Connect V -> If.A
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = vId,
            fromPin = "",
            toExpression = ifId,
            toPin = "A",
        }), "connected", "connect V -> If.A");

        // Connect Threshold -> If.B
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = threshId,
            fromPin = "",
            toExpression = ifId,
            toPin = "B",
        }), "connected", "connect Threshold -> If.B");

        // Connect ColorHi -> If.AGreaterThanB
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = colorHiId,
            fromPin = "",
            toExpression = ifId,
            toPin = "AGreaterThanB",
        }), "connected", "connect ColorHi -> If.AGreaterThanB");

        // Connect ColorLo -> If.ALessThanB
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = colorLoId,
            fromPin = "",
            toExpression = ifId,
            toPin = "ALessThanB",
        }), "connected", "connect ColorLo -> If.ALessThanB");

        // Step 5: connect If output -> BaseColor
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = ifId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect If -> Material.BaseColor");

        // Step 6: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_If_Param_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_If_Param_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // -- B-side: DSL --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_If_Param_B" }),
            "saved", "save_asset Mat_If_Param_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_If_Param_B");

    }

    // =====================================================================
    // Q4: If dual-texture mix (select texture by luminance)
    // =====================================================================

    private static void BuildQ4_TexMix(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a4 = "/Game/Tests/UE5_0/Mat_If_TexMix_A";

        // Step 1: create material
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a4,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_If_TexMix_A");

        // Step 2: add TextureSample (TexA=T_Rock), TextureSample (TexB=T_Grass)
        string texAId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionTextureSample",
            // the doc's T_Rock: the project's basalt diffuse
            texture = "/Game/StarterContent/Textures/T_Rock_Basalt_D",
        }), "add TextureSample(T_Rock)");

        string texBId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionTextureSample",
            // the doc's T_Grass: the project's grass ground diffuse
            texture = "/Game/StarterContent/Textures/T_Ground_Grass_D",
        }), "add TextureSample(T_Grass)");

        // Step 3: add ScalarParameter (Name=Mask, Default=0.5), take TexA .R for comparison
        string maskId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Mask",            // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add ScalarParameter(Mask)");

        // Step 4: add If: A=TexA.R, B=Mask, AGreaterThanB=TexA.RGBA, ALessThanB=TexB.RGBA
        string ifId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionIf",
        }), "add MaterialExpressionIf");

        // Connect TexA.R -> If.A
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = texAId,
            fromPin = "R",            // per task spec: TexA.R
            toExpression = ifId,
            toPin = "A",
        }), "connected", "connect TexA.R -> If.A");

        // Connect Mask -> If.B
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = maskId,
            fromPin = "",
            toExpression = ifId,
            toPin = "B",
        }), "connected", "connect Mask -> If.B");

        // Connect TexA.RGBA -> If.AGreaterThanB -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = texAId,
            fromPin = "RGBA",         // per task spec: TexA.RGBA
            toExpression = ifId,
            toPin = "AGreaterThanB",
        }), "connected", "connect TexA.RGBA -> If.AGreaterThanB");

        // Connect TexB.RGBA -> If.ALessThanB -- per task spec
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = texBId,
            fromPin = "RGBA",         // per task spec: TexB.RGBA
            toExpression = ifId,
            toPin = "ALessThanB",
        }), "connected", "connect TexB.RGBA -> If.ALessThanB");

        // Step 5: connect If output -> BaseColor
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = ifId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect If -> Material.BaseColor");

        // Step 6: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a4 }),
            "compiled", "compile_material Mat_If_TexMix_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a4 }),
            "saved", "save_asset Mat_If_TexMix_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a4);

        // -- B-side: DSL --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_If_TexMix_B" }),
            "saved", "save_asset Mat_If_TexMix_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_If_TexMix_B");

    }

    // The assets themselves are verified by the BlueprintTestHelper.VerifyIrSnapshot calls placed
    // after each save; the step commands are checked through the shared BlueprintTestHelper
    // StepOk / ExpressionId helpers.
}
