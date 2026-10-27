// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 75 - Material Constant (UE 5.0) - from Tasks/UE_5.0/Material/Material_Constant.md
//
// MaterialExpressionConstant / Constant2Vector / Constant3Vector /
// Constant4Vector (UMaterialExpressionConstant*): compile-time scalar/vector
// constants for material graphs. UE 5.0.3 source: Materials/MaterialExpressionConstant*.h.
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//   Both channels build through the same Companion expression operations, and
//   get_asset_ir(level=full) reads the graph back including pin connections.
//
// DSL syntax note: the task doc uses attribute / call form [Const2/3/4] which
// is not the documented material DSL surface. Per user decision the case uses
// the documented, translator-supported form: new Constant / Constant2Vector /
// Constant3Vector / Constant4Vector, the documented node type name Multiply
// (not the task doc's "Mul"), and the object-initializer form for wiring into
// another node's input (new ComponentMask { Input = UV }), not a Mask.Input(UV)
// member call.
//
// Topology note: where the task doc's DSL uses member access on constants
// (C.RGB, C.A), the B-side follows the existing material DSL semantics:
// RGBA swizzle maps to the default output pin, a single-channel swizzle (.A)
// on a single-output source is realized as a ComponentMask extracting that
// channel - the same node the editor builds by hand - inserted by the shared
// bridge ops on both channels, so the two assets hold the same graph.
//
// Pin naming: ComponentMask's single input has no editor label; both channels
// address it by the engine real name of its FExpressionInput UPROPERTY,
// "Input" - the A side as toPin, the B side as the initializer entry name.
// Constants carry their value in the node-level "constant" field on both
// channels (A-side payload key, B-side ctor-arg lowering), which the shared
// material ops layer writes onto the member each class actually owns.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) inserted on user
// instruction per collaboration spec section 9: one call per built asset,
// right after that asset's save - A side after compile_material + save_asset,
// B side after ApplyDsl + save_asset (material assets are not saved by the
// DSL apply, so the save stays explicit). First run seeds the eight baselines
// under TestRuns/5.0/, the second run has to report them all as a match.
// =========================================================================

public sealed class Material_Constant : ITestCase
{
    public string Name => "Material Constant (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 75;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_Const3BaseColor(ctx);
        BuildQ2_Const4Alpha(ctx);
        BuildQ3_Const2Mask(ctx);
        BuildQ4_ConstMath(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Constant3Vector pure color (BaseColor)
    // =====================================================================

    private static void BuildQ1_Const3BaseColor(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Const3_A";

        // Step 1: create material asset (Domain=Surface, BlendMode=Opaque)
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Const3_A");

        // Step 2: add MaterialExpressionConstant3Vector (R=1, G=0, B=0)
        // "constant" is the node-level value field the IR schema uses; on the
        // Constant3Vector it lands on the engine's FLinearColor "Constant" property.
        string constId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add MaterialExpressionConstant3Vector(1,0,0)");

        // Step 3: connect constant output -> Material BaseColor pin
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = constId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Const3 -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Const3_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Const3_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // -- B-side: DSL in the documented material syntax --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Const3_B" }),
            "saved", "save_asset Mat_Const3_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Const3_B");
    }

    // =====================================================================
    // Q2: Constant4Vector with Alpha (BaseColor + Opacity)
    // =====================================================================

    private static void BuildQ2_Const4Alpha(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Const4_A";

        // Step 1: create material (BlendMode=Translucent)
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Const4_A");

        // Step 2: add MaterialExpressionConstant4Vector (R=0, G=1, B=0, A=0.5)
        string constId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant4Vector",
            constant = "(R=0,G=1,B=0,A=0.5)",
        }), "add MaterialExpressionConstant4Vector(0,1,0,0.5)");

        // Step 3: connect .RGB -> BaseColor
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = constId,
            fromPin = "RGB",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Const4.RGB -> Material.BaseColor");

        // Step 4: connect .A -> Opacity
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = constId,
            fromPin = "A",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect Const4.A -> Material.Opacity");

        // Step 5: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Const4_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Const4_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // -- B-side: DSL --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Const4_B" }),
            "saved", "save_asset Mat_Const4_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Const4_B");
    }

    // =====================================================================
    // Q3: Constant2Vector + ComponentMask
    // =====================================================================

    private static void BuildQ3_Const2Mask(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Const2_A";

        // Step 1: create material
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Const2_A");

        // Step 2: add MaterialExpressionConstant2Vector (X=0.5, Y=0.5); the value
        // lands on the engine's R/G floats via the node-level "constant" field.
        string constId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant2Vector",
            constant = "(X=0.5,Y=0.5)",
        }), "add MaterialExpressionConstant2Vector(0.5,0.5)");

        // Step 3: add MaterialExpressionComponentMask (R=true, G=true, B=false, A=false)
        string maskId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionComponentMask",
            r = true,
            g = true,
            b = false,
            a = false,
        }), "add MaterialExpressionComponentMask");

        // Step 4: connect Const2 -> Mask input. ComponentMask's only input has no
        // editor label; its engine real name (the FExpressionInput UPROPERTY) is
        // "Input", the same name the IR inputs[] entries carry, and pin resolution
        // is by that real name - no default-pin guessing fallback.
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = constId,
            fromPin = "",
            toExpression = maskId,
            toPin = "Input",
        }), "connected", "connect Const2 -> Mask input");

        // Step 5: connect Mask output -> BaseColor
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = maskId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Mask -> Material.BaseColor");

        // Step 6: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Const2_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Const2_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // -- B-side: DSL in the documented material syntax; wiring into another
        // node's input is the object-initializer form (DSL_Syntax.md: new NodeType
        // { Input = source }), so the translator emits the IR inputs[] entry. --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Const2_B" }),
            "saved", "save_asset Mat_Const2_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Const2_B");
    }

    // =====================================================================
    // Q4: Multi-constant math (Const + Add + Mul)
    // =====================================================================

    private static void BuildQ4_ConstMath(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a4 = "/Game/Tests/UE5_0/Mat_ConstMath_A";

        // Step 1: create material
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "create_material", new
        {
            path = a4,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ConstMath_A");

        // Step 2: add Constant3Vector (0.2,0.2,0.2)
        string aId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.2,G=0.2,B=0.2)",
        }), "add Constant3Vector(0.2,0.2,0.2)");

        // Step 3: add Constant3Vector (0.3,0.4,0.5)
        string bId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.3,G=0.4,B=0.5)",
        }), "add Constant3Vector(0.3,0.4,0.5)");

        // Step 4: add MaterialExpressionAdd
        string addId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionAdd",
        }), "add MaterialExpressionAdd");

        // Step 5: add Constant (0.5); the value lands on the engine's R float via
        // the node-level "constant" field.
        string halfId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionConstant",
            constant = "0.5",
        }), "add Constant(0.5)");

        // Step 6: add MaterialExpressionMultiply
        string mulId = ExpressionId(ctx, BlueprintTestHelper.EditorCall(ctx, "add_material_expression", new
        {
            path = a4,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        // Step 7: connect first Const3 -> Add.A
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = aId,
            fromPin = "",
            toExpression = addId,
            toPin = "A",
        }), "connected", "connect Const3_A -> Add.A");

        // Step 8: connect second Const3 -> Add.B
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = bId,
            fromPin = "",
            toExpression = addId,
            toPin = "B",
        }), "connected", "connect Const3_B -> Add.B");

        // Step 9: connect Add -> Mul.A
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = addId,
            fromPin = "",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect Add -> Mul.A");

        // Step 10: connect Constant(0.5) -> Mul.B
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = halfId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect Half -> Mul.B");

        // Step 11: connect Mul output -> BaseColor
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "connect_material_pins", new
        {
            path = a4,
            fromExpression = mulId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect Mul -> Material.BaseColor");

        // Step 12: compile / save
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "compile_material", new { path = a4 }),
            "compiled", "compile_material Mat_ConstMath_A");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = a4 }),
            "saved", "save_asset Mat_ConstMath_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a4);

        // -- B-side: DSL --
        BlueprintTestHelper.ApplyDsl(ctx, "");
        StepOk(ctx, BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ConstMath_B" }),
            "saved", "save_asset Mat_ConstMath_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ConstMath_B");
    }

    // The assets themselves are verified by the BlueprintTestHelper.VerifyIrSnapshot calls placed
    // after each save; the step commands are checked through the shared BlueprintTestHelper
    // StepOk / ExpressionId helpers.
}
