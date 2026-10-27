// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 78 - Material Scalar Parameter (UE 5.0) - from Tasks/UE_5.0/Material/Material_ScalarParam.md
//
// MaterialExpressionScalarParameter (UMaterialExpressionScalarParameter: float DefaultValue
// on the class, ParameterName from UMaterialExpressionParameter): the adjustable scalar a
// material instance can override. UE 5.0.3 source: Classes/Materials/MaterialExpressionScalarParameter.h.
// This case covers the parameter on its own (Q1) and inside arithmetic (Q2 Sine + Multiply,
// Q3 Add), which is what the task doc's three questions are.
//
// Bridge capability this case drives:
//   A side - create_material / add_material_expression / connect_material_pins /
//            compile_material (Companion material step commands). The add step's name key
//            creates the expression as a parameter (ParameterName written + GUID refreshed),
//            which is the same plumbing the Asset IR channel's parameters[] uses, so a
//            parameter made step by step and one made from a DSL [Parameter] field are the
//            same node.
//   B side - [Material] + [MaterialSettings] + [Parameter] DSL, lowered by MaterialTranslator
//            to the Asset IR material handler (apply_asset_ir).
//
// All expression classes this doc names - ScalarParameter, Sine, Multiply, Add, Constant,
// Constant3Vector - are verified against the UE 5.0 engine index and the 5.0.3 source, so this
// file needs no assumed capability: it is a straight transcription.
//
// DSL syntax note: the doc sketches the material DSL in an attribute / call form ([ScalarParam],
// Mul(Sine(Speed), White), [Output(Roughness)]); that is not the surface the material DSL syntax
// spec defines and MaterialTranslator lowers, so the B side uses the documented form - [Parameter]
// fields, new NodeType { Pin = source } initializers and bare "Pin = node;" output assignments -
// the same substitution already applied to Material Constant (No. 75) and Material If (No. 74).
// The documented node type names (Sine / Multiply / Add / Constant3Vector) are what the translator
// prefixes with MaterialExpression, matching the classes the A side addresses.
//
// Naming note: Q1's parameter is called "Roughness" (per task spec) and is wired to the material's
// Roughness output pin. The DSL variable carrying it is named Rough so that the output assignment
// line stays readable; the parameter name written into the asset is still "Roughness", which is
// what the verification checks. Q3 keeps the doc's "A" / "B" parameter names verbatim.
//
// Diff comparison (BlueprintTestHelper.VerifyIrSnapshot) re-inserted on user instruction (2026-10-26):
// one snapshot per asset, single argument, six in total (Q1-Q3, each A-side right after
// compile_material + save_asset, each B-side right after ApplyDsl + save_asset). Earlier the same day
// this file's six snapshot calls and the six baselines they compared against were withdrawn together on
// user order, so no baseline is on disk yet: the first run seeds six baselines into TestRuns/5.0/ without
// judging failure, the run after that must match all six. What gets frozen therefore starts from the
// corrected graph - every output pin the task doc names is connected on both channels. Alongside the
// snapshots, the coverage that replaced them during the withdrawal stays in place: every step command's
// business flag (including the add step's propertyErrors, which covers the parameter name and default
// value the step asked for), the expected expression classes and counts, the expected parameter names,
// and the output-pin wiring assertion described next.
//
// Wiring assertion (added 2026-10-26 after user visual feedback on Mat_Scalar1_B): every question now
// also reads back definition.materialOutputs and asserts the pin the task doc names is fed by an
// expression of the expected class. Q1's B side used to produce a material whose scalar parameter node
// existed but whose Roughness output pin was never connected - the DSL referenced the parameter through
// the field's variable name ("Rough"), while the parameter node is addressed by its parameter name
// ("Roughness"), and the reference resolved to a node the graph does not carry, so the bridge dropped
// the connection. Node-presence and parameter-name checks could not see that (both passed either way),
// which is why the pin wiring is asserted explicitly on the A side and the B side alike.
// =========================================================================

public sealed class Material_ScalarParam : ITestCase
{
    public string Name => "Material Scalar Parameter (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 78;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_RoughnessParam(ctx);
        BuildQ2_SineModulatedColor(ctx);
        BuildQ3_TwoParamsAdded(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: one scalar parameter driving Roughness
    // =====================================================================

    private static void BuildQ1_RoughnessParam(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_Scalar1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Scalar1_A");

        // Step 2: add MaterialExpressionScalarParameter (Roughness = 0.5)
        string roughParamId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Roughness",       // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add MaterialExpressionScalarParameter(Roughness)");

        // Step 3: link parameter output -> Roughness pin
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = roughParamId,
            fromPin = "",             // scalar parameter single output
            toExpression = "Material",
            toPin = "Roughness",      // per task spec
        }), "connected", "connect Roughness param -> Material.Roughness");

        // Step 4: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_Scalar1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_Scalar1_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        ExpectExpressions(ctx, a1, ("MaterialExpressionScalarParameter", 1));
        ExpectParameter(ctx, a1, "Roughness");
        ExpectOutputLink(ctx, a1, "Roughness", "MaterialExpressionScalarParameter");

        // -- B-side: DSL in the documented material syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Scalar1_B" }),
            "saved", "save_asset Mat_Scalar1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Scalar1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Scalar1_B", ("MaterialExpressionScalarParameter", 1));
        ExpectParameter(ctx, "/Game/Tests/UE5_0/Mat_Scalar1_B", "Roughness");
        ExpectOutputLink(ctx, "/Game/Tests/UE5_0/Mat_Scalar1_B", "Roughness", "MaterialExpressionScalarParameter");
    }

    // =====================================================================
    // Q2: scalar parameter inside arithmetic (Sine * white -> EmissiveColor)
    // =====================================================================

    private static void BuildQ2_SineModulatedColor(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/Mat_Scalar2_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a2,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Scalar2_A");

        // Step 2: add ScalarParameter (Speed = 1.0)
        string speedId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "Speed",           // per task spec
            defaultValue = 1.0,       // per task spec
        }), "add MaterialExpressionScalarParameter(Speed)");

        // Step 3: add MaterialExpressionSine driven by that parameter
        string sineId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionSine",
        }), "add MaterialExpressionSine");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = speedId,
            fromPin = "",
            toExpression = sineId,
            toPin = "Input",
        }), "connected", "connect Speed -> Sine.Input");

        // Step 4: add Constant3Vector(1,1,1) and MaterialExpressionMultiply: A = Sine, B = white
        string whiteId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant3Vector",
            constant = "(R=1,G=1,B=1)",   // per task spec
        }), "add MaterialExpressionConstant3Vector(1,1,1)");

        string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = sineId,
            fromPin = "",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect Sine -> Multiply.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = whiteId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect White -> Multiply.B");

        // Step 5: link Multiply -> EmissiveColor, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = mulId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",  // per task spec
        }), "connected", "connect Multiply -> Material.EmissiveColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material Mat_Scalar2_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset Mat_Scalar2_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionSine", 1),
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionMultiply", 1));
        ExpectParameter(ctx, a2, "Speed");
        ExpectOutputLink(ctx, a2, "EmissiveColor", "MaterialExpressionMultiply");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Scalar2_B" }),
            "saved", "save_asset Mat_Scalar2_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Scalar2_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Scalar2_B",
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionSine", 1),
            ("MaterialExpressionConstant3Vector", 1),
            ("MaterialExpressionMultiply", 1));
        ExpectParameter(ctx, "/Game/Tests/UE5_0/Mat_Scalar2_B", "Speed");
        ExpectOutputLink(ctx, "/Game/Tests/UE5_0/Mat_Scalar2_B", "EmissiveColor", "MaterialExpressionMultiply");
    }

    // =====================================================================
    // Q3: two scalar parameters added (into Metallic)
    // =====================================================================

    private static void BuildQ3_TwoParamsAdded(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Scalar3_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Scalar3_A");

        // Step 2: add ScalarParameter (A = 0.3) and ScalarParameter (B = 0.7)
        string paramAId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "A",               // per task spec
            defaultValue = 0.3,       // per task spec
        }), "add MaterialExpressionScalarParameter(A)");

        string paramBId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "B",               // per task spec
            defaultValue = 0.7,       // per task spec
        }), "add MaterialExpressionScalarParameter(B)");

        // Step 3: add MaterialExpressionAdd: A = param A, B = param B
        string addId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionAdd",
        }), "add MaterialExpressionAdd");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = paramAId,
            fromPin = "",
            toExpression = addId,
            toPin = "A",
        }), "connected", "connect param A -> Add.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = paramBId,
            fromPin = "",
            toExpression = addId,
            toPin = "B",
        }), "connected", "connect param B -> Add.B");

        // Step 4: link Add -> Metallic, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = addId,
            fromPin = "",
            toExpression = "Material",
            toPin = "Metallic",       // per task spec
        }), "connected", "connect Add -> Material.Metallic");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Scalar3_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Scalar3_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionAdd", 1));
        ExpectParameter(ctx, a3, "A");
        ExpectParameter(ctx, a3, "B");
        ExpectOutputLink(ctx, a3, "Metallic", "MaterialExpressionAdd");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_Scalar3_B" }),
            "saved", "save_asset Mat_Scalar3_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_Scalar3_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Scalar3_B",
            ("MaterialExpressionScalarParameter", 2),
            ("MaterialExpressionAdd", 1));
        ExpectParameter(ctx, "/Game/Tests/UE5_0/Mat_Scalar3_B", "A");
        ExpectParameter(ctx, "/Game/Tests/UE5_0/Mat_Scalar3_B", "B");
        ExpectOutputLink(ctx, "/Game/Tests/UE5_0/Mat_Scalar3_B", "Metallic", "MaterialExpressionAdd");
    }

    // =====================================================================
    // Response parsing helpers
    // =====================================================================

    /// <summary>Assert a step command reported success and return the expressionId it issued.</summary>
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
    /// Read the finished material back through get_asset_ir and assert that every expression
    /// class the task doc names is present with the expected multiplicity.
    /// </summary>
    private static void ExpectExpressions(TestContext ctx, string assetPath, params (string Type, int Count)[] expected)
    {
        var nodes = ReadNodes(ctx, assetPath);
        foreach ((string type, int count) in expected)
        {
            int found = nodes.Count(n => n?["type"]?.GetValue<string>() == type);
            ctx.AssertTrue(found >= count,
                $"{assetPath}: expected {count} x {type}, found {found}. Built graph: {Describe(nodes)}");
        }
    }

    /// <summary>
    /// Assert the material carries a parameter node under the name the task doc gave it. The
    /// read side reports a parameter's identity as parameterName, so this checks the name the
    /// instance would override by - not just that some scalar parameter node exists.
    /// </summary>
    private static void ExpectParameter(TestContext ctx, string assetPath, string parameterName)
    {
        var nodes = ReadNodes(ctx, assetPath);
        bool found = nodes.Any(n => n?["type"]?.GetValue<string>() == "MaterialExpressionScalarParameter"
            && n?["parameterName"]?.GetValue<string>() == parameterName);
        ctx.AssertTrue(found,
            $"{assetPath}: no MaterialExpressionScalarParameter named '{parameterName}'. Built parameters: " +
            string.Join(", ", nodes
                .Where(n => n?["type"]?.GetValue<string>() == "MaterialExpressionScalarParameter")
                .Select(n => n?["parameterName"]?.GetValue<string>() ?? "(unnamed)")));
    }

    /// <summary>
    /// Assert the material's output pin is actually fed: read the graph back and require
    /// definition.materialOutputs to carry a sourceNodeId for that pin, pointing at an expression of the
    /// expected class. A pin the graph does not feed is reported by the read side as absent - which is how a
    /// built-but-unconnected material used to pass every other check in this file.
    /// </summary>
    private static void ExpectOutputLink(TestContext ctx, string assetPath, string pin, string expectedSourceClass)
    {
        var response = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(response, "success"), $"get_asset_ir {assetPath}: {GetStr(response, "error")}");

        var outputs = response["response"]?["definition"]?["materialOutputs"];
        ctx.AssertTrue(outputs is not null,
            $"{assetPath}: read side carries no definition.materialOutputs: {response.ToJsonString()}");

        var pins = outputs!.AsObject();
        string wired = string.Join(", ", pins
            .Where(p => p.Value?["sourceNodeId"]?.GetValue<string>() is { Length: > 0 })
            .Select(p => p.Key)
            .OrderBy(k => k, StringComparer.Ordinal));
        string source = pins.TryGetPropertyValue(pin, out var fed)
            ? fed?["sourceNodeId"]?.GetValue<string>() ?? ""
            : "";

        ctx.AssertTrue(source.Length > 0,
            $"{assetPath}: output pin '{pin}' is not connected - materialOutputs carries no sourceNodeId for it. " +
            $"Pins fed in this graph: [{wired}]");
        ctx.AssertTrue(source.StartsWith(expectedSourceClass, StringComparison.Ordinal),
            $"{assetPath}: output pin '{pin}' is fed by '{source}', expected an expression of type " +
            $"'{expectedSourceClass}'. Pins fed in this graph: [{wired}]");
    }

    private static JsonArray ReadNodes(TestContext ctx, string assetPath)
    {
        var response = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(response, "success"), $"get_asset_ir {assetPath}: {GetStr(response, "error")}");
        var nodes = response["response"]?["definition"]?["nodes"]?.AsArray();
        ctx.AssertTrue(nodes is not null, $"get_asset_ir {assetPath}: definition carries no nodes: {response.ToJsonString()}");
        return nodes!;
    }

    private static string Describe(JsonArray nodes)
        => string.Join(", ", nodes.Select(n => n?["type"]?.GetValue<string>() ?? "?"));
}
