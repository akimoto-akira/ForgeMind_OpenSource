// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 107 - Material Distance Cull Fade And Locals (UE 5.0)
//        - from the UE 5.0 material task doc "material - falloff / culling / locals"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - attenuation and clipping", which cannot be written in source)
//
// UMaterialExpressionDistanceCullFade, UMaterialExpressionObjectRadius,
// UMaterialExpressionSetLocal, UMaterialExpressionGetLocal, UMaterialExpressionConstant3Vector,
// UMaterialExpressionExecBegin, UMaterialExpressionExecEnd, UMaterialExpressionSetMaterialAttributes
// (UE 5.0.3 Materials/MaterialExpression*.h).
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" -> 1 match each for DistanceCullFade / ObjectRadius /
//     SetLocal / GetLocal / Constant3Vector. The exec-flow nodes and SetMaterialAttributes
//     are the ones No. 90 already compiles.
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//   * Engine facts (the engine form wins over the doc wording):
//       - DistanceCullFade::Compile and ObjectRadius::Compile are plain compiler calls with
//         no pixel/vertex stage guard in the Surface domain (MaterialExpressions.cpp:11632,
//         11784); ObjectRadius is refused only in MD_DeferredDecal, which no question uses.
//       - SetLocal declares FExpressionExecOutput Exec, FExpressionInput Value, and FName
//         LocalName (MaterialExpressionSetLocal.h:17-24); its Compile checks
//         OutputIndex == CompileExecutionOutputIndex (MaterialExpressions.cpp:22296).
//         GetLocal::Compile just reads the local by LocalName (22254). Both are execution-flow
//         nodes: a bare SetLocal/GetLocal pair with no exec chain cannot compile. The doc's
//         "G.Input(S.Input(Red))" is not a DSL form, so question 3 uses the same exec chain
//         No. 90 question 1 builds: ExecBegin -> SetLocal -> ExecEnd, with GetLocal read by a
//         SetMaterialAttributes node that returns the whole attribute package. The doc's
//         GetLocal -> BaseColor target is preserved as that node's BaseColor input.
//
// No IR snapshot (VerifyIrSnapshot) is taken: per collaboration spec section 9 a
// transcribed case must not add one on its own. Coverage is the step commands' business
// flags, the read-back count of the named expressions, and the output/input link checks.
// =========================================================================

public sealed class Material_CullFadeLocals : ITestCase
{
    public string Name => "Material Distance Cull Fade And Locals (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 107;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_DistanceCullFade(ctx);
        BuildQ2_ObjectRadius(ctx);
        BuildQ3_SetGetLocal(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: DistanceCullFade -> Opacity
    // =====================================================================

    private static void BuildQ1_DistanceCullFade(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_CullFade1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_CullFade1_A");

        string fadeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionDistanceCullFade",
        }), "add MaterialExpressionDistanceCullFade");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = fadeId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect DistanceCullFade -> Material.Opacity");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_CullFade1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_CullFade1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionDistanceCullFade", 1));
        ExpectOutputLink(ctx, a1, "Opacity", "MaterialExpressionDistanceCullFade");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b1 = "/Game/Tests/UE5_0/Mat_CullFade1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_CullFade1_B");
        ExpectExpressions(ctx, b1, ("MaterialExpressionDistanceCullFade", 1));
        ExpectOutputLink(ctx, b1, "Opacity", "MaterialExpressionDistanceCullFade");
    }

    // =====================================================================
    // Q2: ObjectRadius -> BaseColor
    // =====================================================================

    private static void BuildQ2_ObjectRadius(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_ObjR1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ObjR1_A");

        string radiusId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionObjectRadius",
        }), "add MaterialExpressionObjectRadius");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = radiusId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect ObjectRadius -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_ObjR1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_ObjR1_A");
        ExpectExpressions(ctx, a2, ("MaterialExpressionObjectRadius", 1));
        ExpectOutputLink(ctx, a2, "BaseColor", "MaterialExpressionObjectRadius");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b2 = "/Game/Tests/UE5_0/Mat_ObjR1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_ObjR1_B");
        ExpectExpressions(ctx, b2, ("MaterialExpressionObjectRadius", 1));
        ExpectOutputLink(ctx, b2, "BaseColor", "MaterialExpressionObjectRadius");
    }

    // =====================================================================
    // Q3: SetLocal / GetLocal via the engine's execution flow
    // =====================================================================

    private static void BuildQ3_SetGetLocal(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Local1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
            bEnableExecWire = true,
        }), "created", "create_material Mat_Local1_A");

        string beginId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionExecBegin",
        }), "add MaterialExpressionExecBegin");

        string finishId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionExecEnd",
        }), "add MaterialExpressionExecEnd");

        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add Constant3Vector(1,0,0)");

        string setId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSetLocal",
            LocalName = "L1",
        }), "add MaterialExpressionSetLocal(L1)");

        string getId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionGetLocal",
            LocalName = "L1",
        }), "add MaterialExpressionGetLocal(L1)");

        string attributesId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionSetMaterialAttributes",
        }), "add MaterialExpressionSetMaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = redId,
            fromPin = "",
            toExpression = setId,
            toPin = "Value",
        }), "connected", "connect Red -> SetLocal.Value");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = beginId,
            fromPin = "Exec",
            toExpression = setId,
            toPin = "Input",
        }), "connected", "connect ExecBegin -> SetLocal.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = setId,
            fromPin = "Exec",
            toExpression = finishId,
            toPin = "Input",
        }), "connected", "connect SetLocal -> ExecEnd.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = getId,
            fromPin = "",
            toExpression = attributesId,
            toPin = "BaseColor",
        }), "connected", "connect GetLocal -> SetMaterialAttributes.BaseColor");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = attributesId,
            fromPin = "",
            toExpression = "Material",
            toPin = "MaterialAttributes",
        }), "connected", "connect SetMaterialAttributes -> Material.MaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Local1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Local1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionExecBegin", 1),
            ("MaterialExpressionExecEnd", 1),
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionSetLocal", 1),
            ("MaterialExpressionGetLocal", 1),
            ("MaterialExpressionSetMaterialAttributes", 1));
        ExpectOutputLink(ctx, a3, "MaterialAttributes", "MaterialExpressionSetMaterialAttributes");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionSetLocal", "Value");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionSetMaterialAttributes", "BaseColor");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        const string b3 = "/Game/Tests/UE5_0/Mat_Local1_B";
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_Local1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionExecBegin", 1),
            ("MaterialExpressionExecEnd", 1),
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionSetLocal", 1),
            ("MaterialExpressionGetLocal", 1),
            ("MaterialExpressionSetMaterialAttributes", 1));
        ExpectOutputLink(ctx, b3, "MaterialAttributes", "MaterialExpressionSetMaterialAttributes");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionSetLocal", "Value");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionSetMaterialAttributes", "BaseColor");
    }
}
