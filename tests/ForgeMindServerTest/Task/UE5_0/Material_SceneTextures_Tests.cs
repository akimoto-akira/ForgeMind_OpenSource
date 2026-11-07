// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 89 - Material Scene Textures (UE 5.0)
//        - from the UE 5.0 material task doc "material - scene texture / post process sampling"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - scene texture / post process sampling", which cannot be written in source)
//
// UMaterialExpressionSceneTexture and UMaterialExpressionSceneDepth (UE 5.0.3
// Materials/MaterialExpressionSceneTexture.h, MaterialExpressionSceneDepth.h) sampled
// inside a post-process domain material.
//
// Verification chain (spec section 6, done before writing):
//   * ForgeMind.SpkTool "class" -> MaterialExpressionSceneTexture / SceneDepth / Saturate /
//     Multiply / Constant: 1 match each.
//   * ForgeMind.SpkTool "class" -> MaterialExpressionPostProcessInput: 0 matches, and a scan
//     of every header under Engine/Source/Runtime/Engine/Classes finds no such class either -
//     this is not a missing scan entry, the node simply does not exist in 5.0.3.
//   * ForgeMind.SpkTool "translate" on both B-side DSL below -> 0 diagnostics, and on the
//     question-2 block re-authored with BlendableOutputAlpha -> 0 diagnostics with
//     definition.materialSettings carrying blendableOutputAlpha=true plus
//     materialOutputs.Opacity fed by the Saturate node.
//   * SceneTexture's real UPROPERTYs are "Coordinates" (FExpressionInput), "SceneTextureId"
//     (TEnumAsByte<ESceneTextureId>) and "bFiltered" (bool). The doc's "Id" is not a property,
//     so the key written is the engine name SceneTextureId.
//
// Doc/engine divergences (the engine form wins over the doc wording):
//   * Question 1 asks for "SceneTextureId=PostProcessInput0 / Normal": a buffer and a pin in
//     one line. SceneTexture derives its output list from the selected buffer, and there is no
//     "Normal" output on the post-process input buffers in 5.0.3, so the buffer is kept
//     (SceneTextureId) and the wire takes the node's FIRST declared output per the multi-output
//     convention (A side fromPin "", B side the bare node reference). Asking for "Normal" would
//     mean inventing a pin the engine does not build.
//     The buffer value itself is handed over in the doc's bare form ("PostProcessInput0"): the
//     bridge resolves enum strings through ResolveEnumValue, which accepts the engine name or
//     its prefix-stripped tail. If the running editor's enumeration spells it differently, the
//     add step fails with the offending key in propertyErrors rather than leaving an unset node
//     behind - that failure is the finding, not a silent pass.
//   * Question 3 (MaterialExpressionPostProcessInput) is NOT transcribed: the class does not
//     exist in 5.0.3 and spec section 6 forbids writing an unverified name, "assumed
//     implemented" or not (assumption covers behaviour, never names). The question is handed to
//     the user: either it is meant as a SceneTexture buffer selection (already covered by
//     question 1) or the task doc needs a different node.
//   * Question 2's doc comment "Saturate(SceneDepth * 0.01)" is spelled out as the explicit
//     node chain the bridge can build: Multiply(SceneDepth, Constant 0.01) -> Saturate ->
//     Opacity. Both channels create the same four nodes, so the multiplication is a real node
//     rather than a translation-time guess.
//   * Question 2's "Domain=PostProcess" plus "-> Opacity" needs one more setting to be a live
//     wire at all. UMaterial::IsPropertyActive_Internal (UE 5.0.3 Materials/Material.cpp:
//     5286-5289) keeps exactly two root outputs in the post-process domain:
//         InProperty == MP_EmissiveColor || (bBlendableOutputAlpha && InProperty == MP_Opacity)
//     Without the "Output Alpha" flag the engine never reads the Opacity output: the four nodes
//     are still built and every step still answers success, so the wiring is inert while the
//     case stays green (this is the shape the case had before this note). Both channels
//     therefore set BlendableOutputAlpha, UMaterial's PostProcessMaterial bitfield
//     (Material.h:935, declared outside WITH_EDITORONLY_DATA; editor label "Output Alpha"),
//     which is also what makes BlendMode reachable in this domain (Material.cpp:3478-3490) and
//     what drives the post-process blend state (PostProcess/PostProcessMaterial.cpp:109,
//     MaterialShared.cpp:2066 MATERIAL_OUTPUT_OPACITY_AS_ALPHA); the engine's
//     r.PostProcessAllowBlendModes defaults to 1, so the doc's Translucent stays meaningful.
//
// What the settings still cannot be checked from, and why it stays that way in this pass:
//   * The IR read side reports a fixed whitelist of setting keys (ForgeMindIR_Material.cpp:
//     1198-1201 - MaterialDomain / BlendMode / ShadingModel / ShadingModels / TwoSided /
//     OpacityMaskClipValue / bUseMaterialAttributes), so BlendableOutputAlpha cannot be asserted
//     against the finished asset. The A side is covered by create_material's propertyErrors (a
//     payload key that fails to write is named there while created stays true), and the B side
//     reaches the same ApplyMaterialSetting entry with the same key apart from the first letter's
//     case, which the property lookup matches case-insensitively (ForgeMindIRCommon.cpp:381).
//   * Closed since: the bridge used to resolve a material root output by name alone
//     ("MP_" + pin) without consulting IsPropertyActive, so an output the domain ignores was wired
//     silently and every step still reported success. ForgeMindMaterial::IsMaterialOutputActive now
//     asks the engine, and both channels refuse the wire - connect_material_pins fails the step and
//     the DSL side reports MAT_PIN_NOT_AVAILABLE, which ApplyDsl turns into a failure for every
//     asset type. That is what makes this question's BlendableOutputAlpha setting load-bearing
//     rather than decorative: drop the flag and the Opacity connection stops building the asset.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration spec
// point 9). Every asset this case builds (Mat_ST1 and Mat_SceneDepth1, A and B side each) writes
// its own graph-level IR baseline on the first run (no failure) and diffs against it on later
// runs, so a change to structures already covered here is reported instead of passing silently.
// Each question closes with the A/B cross-check once the B asset is saved - the doc's per-question
// "content equivalence between the two construction methods, asset path excluded". The two
// baselines alone cannot show that: each one freezes its own channel's graph, only the cross-check
// requires the step commands and the DSL to have built the same graph. Coverage before the
// snapshots stays as authored: the step commands' business flags, the read-back count of the
// expressions each question names, the domain/blend settings that landed, and the root output pin
// each question feeds.
// =========================================================================

public sealed class Material_SceneTextures : ITestCase
{
    public string Name => "Material Scene Textures (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 89;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_SceneTextureToEmissive(ctx);
        BuildQ2_SceneDepthFogToOpacity(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: SceneTexture (first output) -> EmissiveColor
    // =====================================================================

    private static void BuildQ1_SceneTextureToEmissive(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_ST1_A";

        // Step 1: create material in the post-process domain
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "PostProcess",
            blendMode = "Opaque",
        }), "created", "create_material Mat_ST1_A");

        // Step 2: add SceneTexture pointing at the post-process input 0 buffer
        string stId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionSceneTexture",
            SceneTextureId = "PostProcessInput0",
        }), "add MaterialExpressionSceneTexture(PostProcessInput0)");

        // Step 3: link its first declared output -> EmissiveColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = stId,
            fromPin = "",             // first declared output of the selected buffer
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect SceneTexture -> Material.EmissiveColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_ST1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_ST1_A");
        ExpectExpressions(ctx, a1, ("MaterialExpressionSceneTexture", 1));
        ExpectMaterialSetting(ctx, a1, "MaterialDomain", "PostProcess");
        ExpectOutputLink(ctx, a1, "EmissiveColor", "MaterialExpressionSceneTexture");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_ST1_B" }),
            "saved", "save_asset Mat_ST1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_ST1_B", ("MaterialExpressionSceneTexture", 1));
        ExpectMaterialSetting(ctx, "/Game/Tests/UE5_0/Mat_ST1_B", "MaterialDomain", "PostProcess");
        ExpectOutputLink(ctx, "/Game/Tests/UE5_0/Mat_ST1_B", "EmissiveColor", "MaterialExpressionSceneTexture");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_ST1_B");   // per-asset self-comparison
        // A/B parity (B saved above)
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, "/Game/Tests/UE5_0/Mat_ST1_B");
    }

    // =====================================================================
    // Q2: Saturate(SceneDepth * 0.01) -> Opacity
    // =====================================================================

    private static void BuildQ2_SceneDepthFogToOpacity(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_SceneDepth1_A";

        // Step 1: create material (Translucent, because the result drives Opacity; the Output Alpha
        // bitfield is what makes the Opacity root output exist in this domain at all)
        var createA2 = EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "PostProcess",
            blendMode = "Translucent",
            BlendableOutputAlpha = true,
        });
        StepOk(ctx, createA2, "created", "create_material Mat_SceneDepth1_A");
        // StepOk reads the created flag alone, while create_material reports the payload keys beyond
        // its envelope that it could not write in propertyErrors. The alpha flag is the setting this
        // case now depends on, so a key that did not land has to fail here rather than leave an inert
        // Opacity wire behind a green step.
        string createA2Errors = GetStr(createA2, "propertyErrors");
        ctx.AssertTrue(createA2Errors.Length == 0,
            $"create_material Mat_SceneDepth1_A -> created, but these settings did not land: {createA2Errors}");

        // Step 2: the scene depth sample and the 0.01 scale constant
        string depthId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionSceneDepth",
        }), "add MaterialExpressionSceneDepth");

        string scaleId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant",
            R = 0.01,
        }), "add MaterialExpressionConstant(0.01)");

        // Step 3: Multiply, then Saturate over the scaled depth
        string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = depthId,
            fromPin = "",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect SceneDepth -> Multiply.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = scaleId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect Constant -> Multiply.B");

        string satId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionSaturate",
        }), "add MaterialExpressionSaturate");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = mulId,
            fromPin = "",
            toExpression = satId,
            toPin = "Input",
        }), "connected", "connect Multiply -> Saturate.Input");

        // Step 4: Saturate -> Opacity, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = satId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect Saturate -> Material.Opacity");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_SceneDepth1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_SceneDepth1_A");
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionSceneDepth", 1),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionMultiply", 1),
            ("MaterialExpressionSaturate", 1));
        ExpectMaterialSetting(ctx, a2, "MaterialDomain", "PostProcess");
        ExpectMaterialSetting(ctx, a2, "BlendMode", "Translucent");
        ExpectOutputLink(ctx, a2, "Opacity", "MaterialExpressionSaturate");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // per-asset self-comparison

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_SceneDepth1_B" }),
            "saved", "save_asset Mat_SceneDepth1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_SceneDepth1_B",
            ("MaterialExpressionSceneDepth", 1),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionMultiply", 1),
            ("MaterialExpressionSaturate", 1));
        ExpectMaterialSetting(ctx, "/Game/Tests/UE5_0/Mat_SceneDepth1_B", "MaterialDomain", "PostProcess");
        ExpectMaterialSetting(ctx, "/Game/Tests/UE5_0/Mat_SceneDepth1_B", "BlendMode", "Translucent");
        ExpectOutputLink(ctx, "/Game/Tests/UE5_0/Mat_SceneDepth1_B", "Opacity", "MaterialExpressionSaturate");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_SceneDepth1_B");   // per-asset self-comparison
        // A/B parity (B saved above)
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, "/Game/Tests/UE5_0/Mat_SceneDepth1_B");
    }
}
