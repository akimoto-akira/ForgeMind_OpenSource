// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 97 - Material Switches (UE 5.0)
//        - from the UE 5.0 material task doc "material - switch" (Tasks material
//          group; the doc file name is the Chinese phrase for that, which cannot
//          be written in source)
//
// UMaterialExpressionStaticSwitch, UMaterialExpressionStaticSwitchParameter and
// UMaterialExpressionQualitySwitch, fed by UMaterialExpressionConstant3Vector
// (UE 5.0.3 Materials/MaterialExpression*.h). These are compile-time branches -
// the runtime per-pixel branch is the If node covered by No.74.
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" on MaterialExpressionStaticSwitch / StaticSwitchParameter /
//     QualitySwitch / FeatureLevelSwitch / Constant3Vector -> 1 match each.
//   * Header facts that decide the shape of this case:
//       - StaticSwitch carries the inputs A, B and an optional Value input, plus the authored
//         bitfield DefaultValue (UPROPERTY with meta OverridingInputProperty="Value",
//         MaterialExpressionStaticSwitch.h). The authored choice is therefore written as
//         DefaultValue; Value stays for the bool-driven form No.98 uses.
//       - StaticSwitchParameter derives from StaticBoolParameter: ParameterName comes from
//         UMaterialExpressionParameter, the authored bool is DefaultValue, and the node adds
//         the inputs A and B (MaterialExpressionStaticSwitchParameter.h).
//       - QualitySwitch has no per-level members: it carries "Default" plus the static array
//         Inputs[EMaterialQualityLevel::Num], and the editor labels them through
//         GetInputName(Index) - index 0 is "Default", the rest are Low / High / Medium / Epic
//         in the order of EMaterialQualityLevel (SceneTypes.h:141-151) and
//         MaterialQualityLevelNames (MaterialShared.cpp:141-148).
//   * ForgeMind.SpkTool "translate" on the three B-side DSL blocks below -> 0 diagnostics.
//
// Bridge capability this case needed (added together with it):
//   A pin that is not a named member was unreachable. ForgeMindMaterial::GetExpressionInput now
//   falls back to the engine's own input interface - GetInputs() paired with GetInputName(Index),
//   which the engine keeps in the same order - after the reflection lookup, so QualitySwitch's
//   Low/High/Medium/Epic pins resolve through the single lookup both channels share instead of
//   another per-class special case. The read side gained the matching pass in
//   CollectExpressionInputs (entries already named by the member walk or the existing special
//   cases are skipped, so every graph baselined before this case keeps its shape).
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 3 wires Low / Medium / High only. UMaterialExpressionQualitySwitch::Compile
//     (MaterialExpressions.cpp:8691-8699) answers "Quality switch missing default input" when
//     Default is not connected, so the case also feeds Default with the low-quality colour on
//     both channels. Without that the material does not compile at all.
//   * The doc names the authored bool of the plain switch "Value" in prose; on the node that
//     name belongs to the optional input pin, so the property written here is DefaultValue.
//   * FeatureLevelSwitch is listed in the doc header but no question asks for it; it is left
//     for the doc's owner rather than invented into a question here.
//
// IR snapshot (VerifyIrSnapshot) - the mis-click comparison - is inserted here on the user's
// naming of this file (collaboration spec section 9: only once the user points at the file).
// Each question carries three: the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel parity read taken after B is saved. A baseline freezes only its own channel's
// graph, so only the parity read asks whether the step commands and the DSL built the same graph.
// For question 3 the snapshot is what pins down the array-backed Default / Low / Medium / High
// wires: they are named by the engine's GetInputName rather than by a member, so they are also the
// case that would silently lose a wire if that resolution ever regressed.
// =========================================================================

public sealed class Material_Switches : ITestCase
{
    public string Name => "Material Switches (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 97;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_StaticSwitch(ctx);
        BuildQ2_StaticSwitchParameter(ctx);
        BuildQ3_QualitySwitch(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: StaticSwitch (DefaultValue=true) with red / blue -> BaseColor
    // =====================================================================

    private static void BuildQ1_StaticSwitch(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_SSwitch1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SSwitch1_A");

        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add Constant3Vector(1,0,0)");

        string blueId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=0,B=1)",
        }), "add Constant3Vector(0,0,1)");

        string switchId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionStaticSwitch",
            DefaultValue = true,
        }), "add MaterialExpressionStaticSwitch(DefaultValue=true)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = redId,
            fromPin = "",
            toExpression = switchId,
            toPin = "A",
        }), "connected", "connect red -> StaticSwitch.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = blueId,
            fromPin = "",
            toExpression = switchId,
            toPin = "B",
        }), "connected", "connect blue -> StaticSwitch.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = switchId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect StaticSwitch -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_SSwitch1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_SSwitch1_A");
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionStaticSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, a1, "BaseColor", "MaterialExpressionStaticSwitch");
        ExpectExpressionInputLinks(ctx, a1, "MaterialExpressionStaticSwitch", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // A-side self baseline

        // -- B-side: DSL in the documented material syntax --
        const string b1 = "/Game/Tests/UE5_0/Mat_SSwitch1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b1 }),
            "saved", "save_asset Mat_SSwitch1_B");
        ExpectExpressions(ctx, b1,
            ("MaterialExpressionStaticSwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, b1, "BaseColor", "MaterialExpressionStaticSwitch");
        ExpectExpressionInputLinks(ctx, b1, "MaterialExpressionStaticSwitch", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q2: StaticSwitchParameter (UseBright) with white / grey -> BaseColor
    // =====================================================================

    private static void BuildQ2_StaticSwitchParameter(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_SSwitchP1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_SSwitchP1_A");

        string whiteId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=1,B=1)",
        }), "add Constant3Vector(1,1,1)");

        string greyId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0.2,G=0.2,B=0.2)",
        }), "add Constant3Vector(0.2,0.2,0.2)");

        string paramId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionStaticSwitchParameter",
            name = "UseBright",
            DefaultValue = true,
        }), "add MaterialExpressionStaticSwitchParameter(UseBright)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = whiteId,
            fromPin = "",
            toExpression = paramId,
            toPin = "A",
        }), "connected", "connect white -> StaticSwitchParameter.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = greyId,
            fromPin = "",
            toExpression = paramId,
            toPin = "B",
        }), "connected", "connect grey -> StaticSwitchParameter.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = paramId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect StaticSwitchParameter -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_SSwitchP1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_SSwitchP1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionStaticSwitchParameter", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, a2, "BaseColor", "MaterialExpressionStaticSwitchParameter");
        ExpectExpressionInputLinks(ctx, a2, "MaterialExpressionStaticSwitchParameter", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // A-side self baseline

        // -- B-side: DSL --
        const string b2 = "/Game/Tests/UE5_0/Mat_SSwitchP1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b2 }),
            "saved", "save_asset Mat_SSwitchP1_B");
        ExpectExpressions(ctx, b2,
            ("MaterialExpressionStaticSwitchParameter", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, b2, "BaseColor", "MaterialExpressionStaticSwitchParameter");
        ExpectExpressionInputLinks(ctx, b2, "MaterialExpressionStaticSwitchParameter", "A", "B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);   // A/B parity (B saved above)
    }

    // =====================================================================
    // Q3: QualitySwitch (Default / Low / Medium / High) -> BaseColor
    // =====================================================================

    private static void BuildQ3_QualitySwitch(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_QSwitch1_A";

        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_QSwitch1_A");

        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",
        }), "add Constant3Vector(1,0,0)");

        string greenId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=1,B=0)",
        }), "add Constant3Vector(0,1,0)");

        string qualityId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionQualitySwitch",
        }), "add MaterialExpressionQualitySwitch");

        // The pins Low / High / Medium are not members - they live in the node's static Inputs
        // array and are named by the engine's GetInputName, resolved by the shared lookup
        foreach ((string colourId, string pin) in new[]
        {
            (redId, "Default"),   // required by QualitySwitch::Compile, see header note
            (redId, "Low"),
            (redId, "Medium"),
            (greenId, "High"),
        })
        {
            StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
            {
                path = a3,
                fromExpression = colourId,
                fromPin = "",
                toExpression = qualityId,
                toPin = pin,
            }), "connected", $"connect Constant3Vector -> QualitySwitch.{pin}");
        }

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = qualityId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect QualitySwitch -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_QSwitch1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_QSwitch1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionQualitySwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, a3, "BaseColor", "MaterialExpressionQualitySwitch");
        ExpectExpressionInputLinks(ctx, a3, "MaterialExpressionQualitySwitch",
            "Default", "Low", "Medium", "High");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // A-side self baseline

        // -- B-side: DSL --
        const string b3 = "/Game/Tests/UE5_0/Mat_QSwitch1_B";
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = b3 }),
            "saved", "save_asset Mat_QSwitch1_B");
        ExpectExpressions(ctx, b3,
            ("MaterialExpressionQualitySwitch", 1),
            ("MaterialExpressionConstant3Vector", 2));
        ExpectOutputLink(ctx, b3, "BaseColor", "MaterialExpressionQualitySwitch");
        ExpectExpressionInputLinks(ctx, b3, "MaterialExpressionQualitySwitch",
            "Default", "Low", "Medium", "High");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);       // B-side self baseline
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);   // A/B parity (B saved above)
    }
}
