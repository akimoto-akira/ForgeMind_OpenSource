// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 76 - Material Compare (UE 5.0) - from Tasks/UE_5.0/Material/Material_Compare.md
//
// MaterialExpressionLess / GreaterEqual / Equal / NotEqual (derived from
// UMaterialExpressionBinaryOp: inputs A, B plus float ConstA / ConstB overrides):
// a material comparison emits a 0/1 float mask, not a blueprint boolean branch,
// so the result is wired into a material output or into If.B.
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//   Both channels build through the same Companion expression operations, so the
//   two assets hold the same graph.
//
// Capability verification and completion: only UMaterialExpressionLess has an
// engine declaration on 5.0.3 (it lives in MaterialExpressionBinaryOp.h - the
// task doc's evidence line names per-class headers that do not exist) and its
// compile path is the new HLSL generator only, which 5.0 cannot enable at
// runtime. GreaterEqual / Equal / NotEqual have no engine declaration at all.
// Per user decision 2026-10-25 ("assume implemented, transcribe as named") the
// case was transcribed unchanged and its first run failed exactly at the first
// missing declaration - which per spec section 6 makes the gap the requirement
// to close. The comparison family is now real expression classes provided by the
// ForgeMindForUE plugin (ForgeMindMaterialCompareExpressions.h): the engine
// BinaryOp input shape (A / B with ConstA / ConstB overrides), a float1 0/1
// mask output, and a compile path that works on the default legacy translator
// (If primitive with the comparison's truth table) - GreaterEqual / Greater /
// LessEqual / Equal / NotEqual as new names, plus LessMask replacing the
// uncompilable native Less (see the routing note below). add_material_expression
// resolves them through live reflection, so this case drives the documented
// step lists unchanged - nothing substituted, nothing skipped.
//
// Less routing: the engine's native UMaterialExpressionLess compiles only on the
// new HLSL generator, which 5.0 cannot enable at runtime, so the bridge routes
// the "Less" op to the plugin's UMaterialExpressionLessMask (same truth table on
// the shared If-based compile path; ForgeMindMaterial::ExpressionClassName) on
// BOTH channels. The doc's name stays as the request; the read-back type and the
// expectations below are MaterialExpressionLessMask, and Mat_Less1_A/B compile
// for real - no error pattern left on that material pair.
//
// DSL syntax note: the task doc sketches the material DSL in an attribute / call
// form ([ScalarParam], Less(a, b), [Output(...)]). That is not the surface the
// material DSL syntax spec defines and MaterialTranslator lowers, so the B side
// uses the documented form - [Parameter] fields plus new NodeType { Pin = source }
// initializers and bare "Pin = node;" output assignments - the same substitution
// already applied to Material Constant (No. 75) and Material If (No. 74) by user
// decision. The doc's Mul/Sine call form maps to the documented node type names.
//
// Topology note: where the doc's Q2 DSL also wires AEqualsB while its A-side step
// list does not, the A-side step list is authoritative and the DSL mirrors it (the
// rule set for Material If). Q3 adds a NotEqual node that its step list never
// connects; the DSL declares the same unconnected node so the graphs stay equal.
//
// Texture note: the doc names a bare /Game/T_Noise that does not exist in this
// project; a sample with an unresolved reference is not cosmetic (the null texture
// turns into a compile error on every node wired to the sample), so Q1 uses the
// project's real Perlin noise mask and asserts the reference resolved on both sides.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) inserted on user instruction
// per collaboration spec point 9 (2026-10-25): one snapshot per asset, six in total
// (Q1-Q3, each A-side after compile_material + save_asset, each B-side after
// ApplyDsl + save_asset). The first run after this seeds six baselines into
// TestRuns/5.0/ (no failure); the run after that must match all six. Until the
// snapshot request, the document's "no execution failure, no verification error"
// was covered by asserting each step command's business flag (including the add
// step's propertyErrors, which "added" alone does not cover) and by reading the
// finished material back to check that every expression the doc names actually
// landed in it - those checks stay in place alongside the snapshots.
// =========================================================================

public sealed class Material_Compare : ITestCase
{
    public string Name => "Material Compare (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 76;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_LessMask(ctx);
        BuildQ2_GreaterEqualIf(ctx);
        BuildQ3_EqualNotEqual(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: Less -> Opacity mask (texture R below a threshold)
    // =====================================================================

    private static void BuildQ1_LessMask(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Less1_A";

        // Step 1: create material (the doc's DSL asks for Translucent, which is what
        // makes the Opacity mask of this question visible)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Less1_A");

        // Step 2: add MaterialExpressionTextureSample (/Game/T_Noise), take .R
        // the doc's T_Noise: the project's perlin noise mask
        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Perlin_Noise_M",
        }), "add MaterialExpressionTextureSample(T_Noise)");

        // Step 3: add MaterialExpressionScalarParameter (Threshold = 0.5)
        string thresholdId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Threshold",       // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add MaterialExpressionScalarParameter(Threshold)");

        // Step 4: add MaterialExpressionLess: A = Tex.R, B = Threshold. The doc's
        // name is kept as the request; the bridge routes "Less" to the plugin's
        // compilable UMaterialExpressionLessMask (same truth table, legacy-compiler
        // If path), so the read-back type is MaterialExpressionLessMask.
        string lessId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionLess",
        }), "add MaterialExpressionLess");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = texId,
            fromPin = "R",            // per task spec: .R channel
            toExpression = lessId,
            toPin = "A",
        }), "connected", "connect Tex.R -> Less.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = thresholdId,
            fromPin = "",             // scalar parameter single output
            toExpression = lessId,
            toPin = "B",
        }), "connected", "connect Threshold -> Less.B");

        // Step 5: link -> Opacity
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = lessId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",        // per task spec
        }), "connected", "connect Less -> Material.Opacity");

        // Step 6: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Less1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Less1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionLessMask", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Less1_B" }),
            "saved", "save_asset Mat_Less1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Less1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Less1_B",
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionLessMask", 1));
    }

    // =====================================================================
    // Q2: GreaterEqual driving an If color switch
    // =====================================================================

    private static void BuildQ2_GreaterEqualIf(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_GE1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_GE1_A");

        // Step 2: add ScalarParameter (V = 0.5) and GreaterEqual: A = V, B = 0.5
        string vId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "V",               // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add MaterialExpressionScalarParameter(V)");

        string geId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionGreaterEqual",
        }), "add MaterialExpressionGreaterEqual");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = vId,
            fromPin = "",
            toExpression = geId,
            toPin = "A",
        }), "connected", "connect V -> GreaterEqual.A");

        // GreaterEqual.B = 0.5 (the doc writes the bound as a literal, which both
        // channels realize as a Constant node - the same node the editor makes)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = geId,
            fromPin = "B",
            constant = "0.5",         // per task spec
        }), "connected", "set GreaterEqual.B = 0.5");

        // Step 3: add If: A = GreaterEqual output, B = 1, AGreaterThanB = red, ALessThanB = black
        string ifId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionIf",
        }), "add MaterialExpressionIf");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = geId,
            fromPin = "",
            toExpression = ifId,
            toPin = "A",
        }), "connected", "connect GreaterEqual -> If.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = ifId,
            fromPin = "B",
            constant = "1",           // per task spec: B = 1
        }), "connected", "set If.B = 1");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = ifId,
            fromPin = "AGreaterThanB",
            constant = "(R=1,G=0,B=0)",   // per task spec: red
        }), "connected", "set If.AGreaterThanB = red");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = ifId,
            fromPin = "ALessThanB",
            constant = "(R=0,G=0,B=0)",   // per task spec: black
        }), "connected", "set If.ALessThanB = black");

        // Step 4: link -> BaseColor, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = ifId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",      // per task spec
        }), "connected", "connect If -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_GE1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_GE1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionGreaterEqual", 1),
            ("MaterialExpressionIf", 1));

        // -- B-side: DSL (mirrors the A-side step list: no AEqualsB wire) --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_GE1_B" }),
            "saved", "save_asset Mat_GE1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_GE1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_GE1_B",
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionGreaterEqual", 1),
            ("MaterialExpressionIf", 1));
    }

    // =====================================================================
    // Q3: Equal / NotEqual parameter-match masks
    // =====================================================================

    private static void BuildQ3_EqualNotEqual(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Eq1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Eq1_A");

        // Step 2: add ScalarParameter (Mode = 1.0)
        string modeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Mode",            // per task spec
            defaultValue = 1.0,       // per task spec
        }), "add MaterialExpressionScalarParameter(Mode)");

        // Step 3: add Equal: A = Mode, B = 1.0 (mask of "mode equals 1")
        string eqId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionEqual",
        }), "add MaterialExpressionEqual");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = modeId,
            fromPin = "",
            toExpression = eqId,
            toPin = "A",
        }), "connected", "connect Mode -> Equal.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = eqId,
            fromPin = "B",
            constant = "1.0",         // per task spec
        }), "connected", "set Equal.B = 1.0");

        // Step 4: add NotEqual: A = Mode, B = 0.0. The doc's step list adds this node
        // and never wires it on, so it stays an unconnected expression here too.
        string neId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionNotEqual",
        }), "add MaterialExpressionNotEqual");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = modeId,
            fromPin = "",
            toExpression = neId,
            toPin = "A",
        }), "connected", "connect Mode -> NotEqual.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = neId,
            fromPin = "B",
            constant = "0.0",         // per task spec
        }), "connected", "set NotEqual.B = 0.0");

        // Step 5: link Equal -> EmissiveColor, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = eqId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",  // per task spec
        }), "connected", "connect Equal -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Eq1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Eq1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionEqual", 1),
            ("MaterialExpressionNotEqual", 1));

        // -- B-side: DSL (declares the same unconnected NotEqual node) --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Eq1_B" }),
            "saved", "save_asset Mat_Eq1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Eq1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Eq1_B",
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionEqual", 1),
            ("MaterialExpressionNotEqual", 1));
    }

    // The step commands and the per-asset content checks run through the shared
    // BlueprintTestHelper helpers: StepOk / ExpressionId (which also fails an add whose
    // payload keys did not all land, per the propertyErrors field) and ExpectExpressions
    // (the live read-back count check that once surfaced the missing comparison classes).
}
