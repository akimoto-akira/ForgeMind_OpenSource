// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 84 - Material Decal (UE 5.0)
//        - from the UE 5.0 material task doc "decal"
//          (Tasks material group; the doc file name is "material - decal")
//
// Decal-only expressions of the material domain:
//   UMaterialExpressionDecalMipmapLevel, UMaterialExpressionDecalLifetimeOpacity,
//   UMaterialExpressionDecalDerivative (UE 5.0.3 Materials/MaterialExpression*.h).
// DecalLifetimeOpacity carries no properties and one output; DecalDerivative carries no
// properties and TWO named outputs - the engine constructor declares DDX at index 0 and
// DDY at index 1 and Compile() picks the derivative direction from that index
// (MaterialExpressions.cpp:17633-17642) - so wiring the bare node takes DDX, and this
// case takes DDY by name on both channels. DecalMipmapLevel carries the optional
// TextureSize input plus the ConstWidth/ConstHeight fallback properties. All class names
// were verified present in engine.spk via ForgeMind.SpkTool "class" before writing; the
// whole B-side DSL below was verified with "translate" (0 diagnostics).
//
// Multi-output convention this case follows (user ruling, 2026-10-28): an expression with
// several outputs is wired from its FIRST output unless the case deliberately asks for
// another pin, and a pin asked for by name is the source's own declared output name
// (DDY here), not a channel swizzle - a name resolves against the engine output list and
// connects directly, while the numeric R/G/B/A form keeps meaning "extract that channel"
// through an inserted ComponentMask.
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands).
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//
// Doc/engine divergence note: questions 1 and 3 pass a Texture=T_Decal key, but
// neither UMaterialExpressionDecalMipmapLevel nor UMaterialExpressionDecalDerivative
// carries a Texture property (their headers declare none - the mip-level node
// derives the texture size from its optional TextureSize input / Const* pair, the
// derivative node samples the decal's own texture implicitly). The key has no
// landing point in the engine, so it is dropped on both channels and no texture
// substitution is needed.
//
// All three materials are created with Domain=DeferredDecal (required for decal
// expressions per the doc header) and BlendMode=Translucent (per the doc's DSL
// headers).
//
// IR snapshots (BlueprintTestHelper.VerifyIrSnapshot) are taken on user instruction:
// one point per asset, 6 in total (Q1-Q3, A/B each), placed right after each asset's
// save_asset and before the read-back assertions. The first run seeds the 6 baselines
// into TestRuns/5.0/ (a seed is not a pass); the second run must report all 6 as
// "IR snapshot match" for the case to be stable across runs. On a mismatch the
// baseline stays untouched and the root-cause analysis goes to the user first.
// Q3's baselines freeze the named-output form (Normal wired from DecalDerivative's DDY,
// no ComponentMask in the graph); the step-command business flags and the
// ExpectExpressions checks remain alongside the snapshots.
// =========================================================================

public sealed class Material_Decal : ITestCase
{
    public string Name => "Material Decal (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 84;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_DecalMipmapLevel(ctx);
        BuildQ2_DecalLifetimeOpacity(ctx);
        BuildQ3_DecalDerivative(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: DecalMipmapLevel -> BaseColor
    // =====================================================================

    private static void BuildQ1_DecalMipmapLevel(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Decal1_A";

        // Step 1: create material (Domain=DeferredDecal, Translucent per the doc)
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "DeferredDecal",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Decal1_A");

        // Step 2: add DecalMipmapLevel. The doc's Texture=T_Decal key has no landing
        // point - the class carries no Texture property (header verified), so nothing
        // is set here.
        string mipId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionDecalMipmapLevel",
        }), "add MaterialExpressionDecalMipmapLevel");

        // Step 3: link its output -> BaseColor
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = mipId,
            fromPin = "",             // single output
            toExpression = "Material",
            toPin = "BaseColor",
        }), "connected", "connect DecalMipmapLevel -> Material.BaseColor");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Decal1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Decal1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison
        ExpectExpressions(ctx, a1, ("MaterialExpressionDecalMipmapLevel", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Decal1_B" }),
            "saved", "save_asset Mat_Decal1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Decal1_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Decal1_B", ("MaterialExpressionDecalMipmapLevel", 1));
    }

    // =====================================================================
    // Q2: DecalLifetimeOpacity -> Opacity
    // =====================================================================

    private static void BuildQ2_DecalLifetimeOpacity(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Decal2_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "DeferredDecal",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Decal2_A");

        // Step 2: add DecalLifetimeOpacity (no properties, one output)
        string lifeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionDecalLifetimeOpacity",
        }), "add MaterialExpressionDecalLifetimeOpacity");

        // Step 3: link its output -> Opacity
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = lifeId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Opacity",
        }), "connected", "connect DecalLifetimeOpacity -> Material.Opacity");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Decal2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Decal2_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // per-asset self-comparison
        ExpectExpressions(ctx, a2, ("MaterialExpressionDecalLifetimeOpacity", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Decal2_B" }),
            "saved", "save_asset Mat_Decal2_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Decal2_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Decal2_B", ("MaterialExpressionDecalLifetimeOpacity", 1));
    }

    // =====================================================================
    // Q3: DecalDerivative -> Normal
    // =====================================================================

    private static void BuildQ3_DecalDerivative(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Decal3_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "DeferredDecal",
            blendMode = "Translucent",
        }), "created", "create_material Mat_Decal3_A");

        // Step 2: add DecalDerivative. The doc's Texture=T_Decal key has no landing
        // point - the class carries no Texture property (header verified).
        string derId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionDecalDerivative",
        }), "add MaterialExpressionDecalDerivative");

        // Step 3: link the node's DDY output -> Normal. DecalDerivative's default output is
        // DDX and that combination is already known to compile cleanly, so this channel takes
        // the SECOND declared pin by name (user instruction); the bridge resolves "DDY"
        // against the outputs the source declares and connects that pin directly instead of
        // reading index 1 as a channel request.
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = derId,
            fromPin = "DDY",
            toExpression = "Material",
            toPin = "Normal",
        }), "connected", "connect DecalDerivative.DDY -> Material.Normal");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Decal3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Decal3_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // per-asset self-comparison
        ExpectExpressions(ctx, a3, ("MaterialExpressionDecalDerivative", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Decal3_B" }),
            "saved", "save_asset Mat_Decal3_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Decal3_B");   // per-asset self-comparison
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Decal3_B", ("MaterialExpressionDecalDerivative", 1));
    }
}
