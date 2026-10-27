// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 80 - Material Attributes (UE 5.0) - from Tasks/UE_5.0/Material/Material_Attributes.md
//
// MaterialExpressionMakeMaterialAttributes (one FExpressionInput per attribute: BaseColor,
// Metallic, Specular, Roughness, Anisotropy, EmissiveColor, Opacity, OpacityMask, Normal,
// Tangent, WorldPositionOffset, SubsurfaceColor, ClearCoat, ClearCoatRoughness,
// AmbientOcclusion, Refraction, PixelDepthOffset, ShadingModel, CustomizedUVs[8]) packs the
// outputs a material would otherwise carry on separate pins into one MaterialAttributes data
// flow; Break / Set / Blend (UMaterialExpressionBreakMaterialAttributes,
// UMaterialExpressionSetMaterialAttributes, UMaterialExpressionBlendMaterialAttributes) read,
// replace and mix that bundle. UE 5.0.3 source: Classes/Materials/MaterialExpression*MaterialAttributes.h -
// all four classes are verified in the UE 5.0 engine index.
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands). The material's
//            MaterialAttributes pin is reached the way UMaterial exposes it, through
//            GetExpressionInputForProperty(MP_MaterialAttributes), so "MaterialAttributes"
//            is a legal toExpression="Material" pin on both channels.
//   B side - [Material] + [MaterialSettings] DSL, lowered by MaterialTranslator to the
//            Asset IR material handler (apply_asset_ir).
//
// Assumed capabilities (user decision 2026-10-25: "Assumed to be implemented, used as transcribed content"). The
// expression classes exist; what has to be added is on the channel side:
//   1. create_material reads only domain / blendMode today, so the doc's
//      "UseMaterialAttributes = true" has no A-side home. The case passes it under the engine
//      property name (bUseMaterialAttributes, UMaterial's editor-only bitfield) - the same name
//      the read side reports in materialSettings - and asserts it back, so an ignored key fails
//      here rather than turning into a material that was never in attributes mode. The B side
//      asks for it through [MaterialSettings(bUseMaterialAttributes = true)], which the
//      translator already lowers onto the same property.
//   2. The attribute bundle pins are not plain FExpressionInput members: Break's input and
//      Blend's A / B are FMaterialAttributesInput (a derived USTRUCT), and Set's inputs are a
//      dynamic TArray<FExpressionInput> paired with an AttributeSetTypes GUID array. The pin
//      lookup by UPROPERTY name only accepts the base struct today, so those wires are the
//      capability this case exercises: the A side addresses them by the engine real names
//      (MaterialAttributes / A / B / Input / Roughness) and both sides then read the built
//      graph back.
//
// Property naming: the doc's DSL calls the packing node [MakeAttributes(BaseColor = ...)] and
// the mix call BlendAttributes(A:.., B:.., Alpha:..). Those are not the surface the material DSL
// syntax spec defines and MaterialTranslator lowers, so the B side uses the documented form -
// new NodeType { Pin = source } with bare "Pin = node;" output assignments - the same substitution
// already applied to Material Constant (No. 75) and Material If (No. 74). Pin and parameter names
// stay exactly as the doc gives them.
//
// Texture note: none of the three questions samples a texture, so no asset reference has to be
// substituted here; every value the docs name is a constant or a scalar parameter.
//
// No IR snapshot is taken here: per collaboration spec section 9 a transcribed case must not add
// VerifyIrSnapshot on its own. The doc's "no execution failure, no verification error" is covered by
// asserting every step command's business flag (including the add step's propertyErrors), asserting
// the attributes mode landed, and reading each finished material back to check that the expressions
// the doc names landed in it.
// =========================================================================

public sealed class Material_Attributes : ITestCase
{
    public string Name => "Material Attributes (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 80;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_MakeAttributes(ctx);
        BuildQ2_BreakAndSet(ctx);
        BuildQ3_BlendAttributes(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: MakeMaterialAttributes packing BaseColor + Normal
    // =====================================================================

    private static void BuildQ1_MakeAttributes(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_MA1_A";

        // Step 1: create material with the MaterialAttributes output mode on
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
            bUseMaterialAttributes = true,    // per task spec: UseMaterialAttributes = true
        }), "created", "create_material Mat_MA1_A");

        // Step 2: add Constant3Vector(1,0,0) and Constant3Vector(0,0,1)
        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",       // per task spec
        }), "add Constant3Vector(1,0,0)");

        string blueId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=0,B=1)",       // per task spec
        }), "add Constant3Vector(0,0,1)");

        // Step 3: add MakeMaterialAttributes: BaseColor = red, Normal = blue
        string makeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionMakeMaterialAttributes",
        }), "add MaterialExpressionMakeMaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = redId,
            fromPin = "",
            toExpression = makeId,
            toPin = "BaseColor",              // per task spec
        }), "connected", "connect Red -> Make.BaseColor");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = blueId,
            fromPin = "",
            toExpression = makeId,
            toPin = "Normal",                 // per task spec: blue as an approximate normal
        }), "connected", "connect Blue -> Make.Normal");

        // Step 4: link Make -> the material's MaterialAttributes pin, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = makeId,
            fromPin = "",
            toExpression = "Material",
            toPin = "MaterialAttributes",     // per task spec
        }), "connected", "connect Make -> Material.MaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_MA1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_MA1_A");
        ExpectMaterialAttributesEnabled(ctx, a1);
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionMakeMaterialAttributes", 1));

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_MA1_B" }),
            "saved", "save_asset Mat_MA1_B");
        ExpectMaterialAttributesEnabled(ctx, "/Game/Tests/UE5_0/Mat_MA1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_MA1_B",
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionMakeMaterialAttributes", 1));
    }

    // =====================================================================
    // Q2: Make -> Break, then Set replacing the Roughness channel
    // =====================================================================

    private static void BuildQ2_BreakAndSet(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_MA2_A";

        // Step 1: create material with the MaterialAttributes output mode on
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
            bUseMaterialAttributes = true,    // per task spec
        }), "created", "create_material Mat_MA2_A");

        // Step 2: add Constant3Vector(1,0,0) and MakeMaterialAttributes(BaseColor = red)
        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",       // per task spec
        }), "add Constant3Vector(1,0,0)");

        string makeId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionMakeMaterialAttributes",
        }), "add MakeMaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = redId,
            fromPin = "",
            toExpression = makeId,
            toPin = "BaseColor",
        }), "connected", "connect Red -> Make.BaseColor");

        // Step 3: add BreakMaterialAttributes and feed the packed bundle into it. The channel
        // the doc names (.BaseColor) has no consumer in its step list or its DSL, so nothing is
        // wired out of the break node - the graph holds the node, as both channels build it.
        string breakId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionBreakMaterialAttributes",
        }), "add BreakMaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = makeId,
            fromPin = "",
            toExpression = breakId,
            toPin = "MaterialAttributes",     // UMaterialExpressionBreakMaterialAttributes input
        }), "connected", "connect Make -> Break.MaterialAttributes");

        // Step 4: add Constant(0.5) and SetMaterialAttributes: source bundle = Make,
        // Roughness channel = that constant (per task spec's Roughness replacement)
        string roughId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant",
            constant = "0.5",                 // per task spec's [Const(0.5)]
        }), "add Constant(0.5)");

        string setId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionSetMaterialAttributes",
        }), "add SetMaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = makeId,
            fromPin = "",
            toExpression = setId,
            toPin = "Input",                  // per task spec: S1.Input(A1)
        }), "connected", "connect Make -> Set.Input");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = roughId,
            fromPin = "",
            toExpression = setId,
            toPin = "Roughness",              // per task spec: replaced channel
        }), "connected", "connect Constant(0.5) -> Set.Roughness");

        // Step 5: link Set -> MaterialAttributes, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = setId,
            fromPin = "",
            toExpression = "Material",
            toPin = "MaterialAttributes",     // per task spec
        }), "connected", "connect Set -> Material.MaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_MA2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_MA2_A");
        ExpectMaterialAttributesEnabled(ctx, a2);
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionMakeMaterialAttributes", 1),
            ("MaterialExpressionBreakMaterialAttributes", 1),
            ("MaterialExpressionSetMaterialAttributes", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_MA2_B" }),
            "saved", "save_asset Mat_MA2_B");
        ExpectMaterialAttributesEnabled(ctx, "/Game/Tests/UE5_0/Mat_MA2_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_MA2_B",
            ("MaterialExpressionMakeMaterialAttributes", 1),
            ("MaterialExpressionBreakMaterialAttributes", 1),
            ("MaterialExpressionSetMaterialAttributes", 1));
    }

    // =====================================================================
    // Q3: BlendMaterialAttributes mixing two packed bundles
    // =====================================================================

    private static void BuildQ3_BlendAttributes(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_MA3_A";

        // Step 1: create material with the MaterialAttributes output mode on
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
            bUseMaterialAttributes = true,    // per task spec
        }), "created", "create_material Mat_MA3_A");

        // Step 2: add Constant3Vector(1,0,0), Constant3Vector(0,1,0) and one Make per bundle
        string redId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=0,B=0)",       // per task spec: bundle A
        }), "add Constant3Vector(1,0,0)");

        string greenId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=0,G=1,B=0)",       // per task spec: bundle B
        }), "add Constant3Vector(0,1,0)");

        string makeAId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMakeMaterialAttributes",
        }), "add MakeMaterialAttributes(A)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = redId,
            fromPin = "",
            toExpression = makeAId,
            toPin = "BaseColor",
        }), "connected", "connect Red -> MakeA.BaseColor");

        string makeBId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMakeMaterialAttributes",
        }), "add MakeMaterialAttributes(B)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = greenId,
            fromPin = "",
            toExpression = makeBId,
            toPin = "BaseColor",
        }), "connected", "connect Green -> MakeB.BaseColor");

        // Step 3: add ScalarParameter (Blend = 0.5)
        string blendId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Blend",                   // per task spec
            defaultValue = 0.5,               // per task spec
        }), "add MaterialExpressionScalarParameter(Blend)");

        // Step 4: add BlendMaterialAttributes: A = bundle A, B = bundle B, Alpha = Blend
        string blendNode = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionBlendMaterialAttributes",
        }), "add MaterialExpressionBlendMaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = makeAId,
            fromPin = "",
            toExpression = blendNode,
            toPin = "A",                      // UMaterialExpressionBlendMaterialAttributes input A
        }), "connected", "connect MakeA -> Blend.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = makeBId,
            fromPin = "",
            toExpression = blendNode,
            toPin = "B",
        }), "connected", "connect MakeB -> Blend.B");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = blendId,
            fromPin = "",
            toExpression = blendNode,
            toPin = "Alpha",
        }), "connected", "connect Blend param -> Blend.Alpha");

        // Step 5: link Blend -> MaterialAttributes, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = blendNode,
            fromPin = "",
            toExpression = "Material",
            toPin = "MaterialAttributes",     // per task spec
        }), "connected", "connect Blend -> Material.MaterialAttributes");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_MA3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_MA3_A");
        ExpectMaterialAttributesEnabled(ctx, a3);
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionMakeMaterialAttributes", 2),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionBlendMaterialAttributes", 1));

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_MA3_B" }),
            "saved", "save_asset Mat_MA3_B");
        ExpectMaterialAttributesEnabled(ctx, "/Game/Tests/UE5_0/Mat_MA3_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_MA3_B",
            ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionMakeMaterialAttributes", 2),
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionBlendMaterialAttributes", 1));
    }

    // =====================================================================
    // Response parsing helpers
    // =====================================================================

    /// <summary>Assert a step command reported success and return the expressionId it issued.
    /// An add step writes every extra payload key as a property and reports the ones it could not
    /// write in propertyErrors while still answering added=true, so that field is checked here too.
    /// </summary>
    private static string ExpressionId(TestContext ctx, JsonNode response, string step)
    {
        ctx.AssertTrue(GetBool(response, "added"), $"{step} -> added=false: {GetStr(response, "reason")}");
        string propertyErrors = GetStr(response, "propertyErrors");
        ctx.AssertTrue(propertyErrors.Length == 0, $"{step} -> added, but these properties did not land: {propertyErrors}");
        string id = GetExpressionId(response);
        ctx.AssertTrue(id.Length > 0, $"{step} -> response carries no expressionId: {response.ToJsonString()}");
        return id;
    }

    /// <summary>Assert the business flag a step command reports (created / added / connected / compiled / saved).</summary>
    private static void StepOk(TestContext ctx, JsonNode response, string flag, string step)
    {
        ctx.AssertTrue(GetBool(response, flag), $"{step} -> {flag}=false: {GetStr(response, "reason")}");
    }

    private static string GetExpressionId(JsonNode response)
        => response?["response"]?["expressionId"]?.GetValue<string>()
           ?? response?["response"]?["nodeId"]?.GetValue<string>()
           ?? "";

    /// <summary>
    /// Assert the material finished in MaterialAttributes mode. The read side reports the mode in
    /// definition.materialSettings under the engine property name, so the check is against the asset,
    /// not against what the create step was asked for - a create command that ignores the key fails here.
    /// </summary>
    private static void ExpectMaterialAttributesEnabled(TestContext ctx, string assetPath)
    {
        var flag = ReadSettings(ctx, assetPath)["bUseMaterialAttributes"];
        bool enabled = false;
        if (flag is not null)
        {
            enabled = flag.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => flag.GetValue<double>() > 0,
                JsonValueKind.String => flag.GetValue<string>() is "true" or "1",
                _ => false,
            };
        }
        ctx.AssertTrue(enabled,
            $"{assetPath}: materialSettings.bUseMaterialAttributes did not land as true " +
            $"(read back {flag?.ToJsonString() ?? "(absent)"})");
    }

    /// <summary>
    /// Read the finished material back through get_asset_ir and assert that every expression class the
    /// task doc names is present with the expected multiplicity. Not a snapshot: it fails with the missing
    /// class named, which is how an expression the handler does not build yet surfaces instead of being
    /// reported as a pass.
    /// </summary>
    private static void ExpectExpressions(TestContext ctx, string assetPath, params (string Type, int Count)[] expected)
    {
        var nodes = ReadNodes(ctx, assetPath);
        foreach ((string type, int count) in expected)
        {
            int found = nodes.Count(n => n?["type"]?.GetValue<string>() == type);
            ctx.AssertTrue(found >= count,
                $"{assetPath}: expected {count} x {type}, found {found}. Built graph: " +
                string.Join(", ", nodes.Select(n => n?["type"]?.GetValue<string>() ?? "?")));
        }
    }

    private static JsonObject ReadSettings(TestContext ctx, string assetPath)
    {
        var response = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(response, "success"), $"get_asset_ir {assetPath}: {GetStr(response, "error")}");
        var settings = response["response"]?["definition"]?["materialSettings"]?.AsObject();
        ctx.AssertTrue(settings is not null, $"get_asset_ir {assetPath}: definition carries no materialSettings");
        return settings!;
    }

    private static JsonArray ReadNodes(TestContext ctx, string assetPath)
    {
        var response = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(response, "success"), $"get_asset_ir {assetPath}: {GetStr(response, "error")}");
        var nodes = response["response"]?["definition"]?["nodes"]?.AsArray();
        ctx.AssertTrue(nodes is not null, $"get_asset_ir {assetPath}: definition carries no nodes: {response.ToJsonString()}");
        return nodes!;
    }
}
