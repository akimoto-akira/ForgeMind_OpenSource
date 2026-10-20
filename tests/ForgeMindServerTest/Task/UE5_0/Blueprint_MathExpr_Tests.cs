// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 47 - Blueprint Math Expression (UE 5.0) - from Tasks/UE_5.0/blueprint-math-expr.md
//
// Tasks (each: one MCP-stepped asset + one DSL asset, "one task two assets" Q4):
//   Task 1 - single formula:       Calc(float x, float y) => x * y + 1
//   Task 2 - multi-input formula:  F(float a, float b, float c) => a * (b + c) / 2
//   Task 3 - vector formula:       G(Vector p, float s) => p * s + (10,0,0)
//
// SPECIAL REQUIREMENT - settings matrix over convertAssignmentOperatorsToBlueprintMacros:
//   This conversion exercises how the DSL lowers arithmetic expressions under the four
//   values of McpSettings.convertAssignmentOperatorsToBlueprintMacros
//   (McpSettings.cs:13 enum; default Macro at line 40). The "sides" are:
//     Section 1 - probe: read the CURRENT value of the setting via the `settings` tool.
//     Section 2 - A-side  : the task's operation space A (MCP), setting-independent - built once.
//     Section 3 - B-side  : operation space B (DSL) after setting PureNodes (works).
//                C-side  : operation space B (DSL) after setting Macro   - COPY of B (same DSL code).
//     Section 4 - D-side  : operation space B (DSL) after setting MathNodes - COPY of B (same DSL code).
//
// Three strategies must all build complete formulas: PureNodes forms a pure node network, Macro forms a single macro instance,
// MathNodes forms the engine math expression node. Real-time IR checks outer node type, subgraph, and connections,
// preventing unimplemented strategies from silently degrading or constructing only partial expressions; this is not historical snapshot diff comparison.
//
//   The `settings` tool writes McpSettingsManager.Shared.Current (ForgeMindServerHost wires
//   SettingsTool with that same singleton), which is exactly what the translator reads at
//   translation time - so setting it here genuinely drives the DSL path. The original value
//   is captured in Section 1 and restored at the end so the global setting is not left mutated.
//
//   A-side (MCP) decomposes each formula into KismetMathLibrary nodes (the MCP-equivalent of
//   the DSL's PureNodes lowering, targeting the same "cross-method equivalence" the doc asks
//   for) rather than a single UK2Node_MathExpression node: the bridge exposes no dedicated
//   MathExpression MCP action, and the decomposed graph is what the DSL actually produces.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction
// (collaboration spec point 9) after the case was visually confirmed. Every built asset -
// the 3 A-side MCP assets, the 9 doc DSL assets (B/C/D) and the 3 arithmetic-edge assets -
// stores its own graph-level IR baseline on first run (no failure); later runs diff against
// it, catching accidental damage to already-tested structures (per-asset self-comparison,
// not A/B cross-comparison). The ApplyAndVerifyMath / VerifyArithmeticEdges IR assertions
// above are separate real-time structural checks, not this snapshot.
// =========================================================================

public sealed class Blueprint_MathExpr : ITestCase
{
    public string Name => "Blueprint Math Expression (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 47;

    public async Task Run(TestContext ctx)
    {
        // ── Section 1: probe current convertAssignmentOperatorsToBlueprintMacros ──
        var getR = ctx.CallTool("settings", new()
        {
            ["action"] = "get",
            ["key"] = "convertAssignmentOperatorsToBlueprintMacros",
        });
        ctx.Logger.Info(null,
            "[MExpr] section1 current convertAssignmentOperatorsToBlueprintMacros = {0}", getR.Message);
        // Capture the original value so we can restore it at the end (avoid leaving the
        // global setting mutated for sibling tests).
        string originalMode = getR.Message.Substring(getR.Message.IndexOf("= ") + 2).Trim();

        try
        {
            // ── Section 2: A-side - operation space A (MCP), setting-independent ──
            BuildTask1Mcp(ctx);
            BuildTask2Mcp(ctx);
            BuildTask3Mcp(ctx);

            // ── Section 3: B-side (PureNodes) then C-side (Macro, copy of B) ──
            SetAssignmentMode(ctx, "PureNodes");
            ApplyAndVerifyMath(ctx, DslTask1, "MExpr_Basic_B", "Calc", "PureNodes", 2);
            ApplyAndVerifyMath(ctx, DslTask2, "MExpr_Multi_B", "F", "PureNodes", 3);
            ApplyAndVerifyMath(ctx, DslTask3, "MExpr_Vec_B", "G", "PureNodes", 3);

            SetAssignmentMode(ctx, "Macro");
            // C-side: identical DSL code to B (copy of B), only the asset path differs.
            ApplyAndVerifyMath(ctx, CopyB(DslTask1, "Macro"), "MExpr_Basic_Macro", "Calc", "Macro", 2);
            ApplyAndVerifyMath(ctx, CopyB(DslTask2, "Macro"), "MExpr_Multi_Macro", "F", "Macro", 3);
            ApplyAndVerifyMath(ctx, CopyB(DslTask3, "Macro"), "MExpr_Vec_Macro", "G", "Macro", 3);

            // ── Section 4: D-side (MathNodes, copy of B) ──
            SetAssignmentMode(ctx, "MathNodes");
            // D-side: identical DSL code to B (copy of B), only the asset path differs.
            ApplyAndVerifyMath(ctx, CopyB(DslTask1, "MathNodes"), "MExpr_Basic_MathNodes", "Calc", "MathNodes", 2);
            ApplyAndVerifyMath(ctx, CopyB(DslTask2, "MathNodes"), "MExpr_Multi_MathNodes", "F", "MathNodes", 3);
            ApplyAndVerifyMath(ctx, CopyB(DslTask3, "MathNodes"), "MExpr_Vec_MathNodes", "G", "MathNodes", 3);
            foreach (string mode in new[] { "PureNodes", "Macro", "MathNodes" })
            {
                SetAssignmentMode(ctx, mode);
                VerifyArithmeticEdges(ctx, mode);
            }
        }
        finally
        {
            // Always restore the pre-run setting captured in Section 1, even when C/D (Macro/
            // MathNodes) throw inside ApplyDsl - otherwise the global setting would be left
            // mutated (PureNodes/Macro/MathNodes) for sibling tests.
            SetAssignmentMode(ctx, originalMode);
        }

        await Task.CompletedTask;
    }

    // ── A-side: operation space A (MCP) - decompose each formula into KismetMathLibrary nodes ──

    private static void ApplyAndVerifyMath(TestContext ctx, string dsl, string assetName,
        string functionName, string mode, int operationCount)
    {
        BlueprintTestHelper.ApplyDsl(ctx, dsl);
        string path = "/Game/Tests/UE5_0/" + assetName;
        var response = BlueprintTestHelper.EditorCall(ctx, "get_asset_ir", new { path, level = "full" });
        var definition = response["response"]?["definition"];
        ctx.AssertTrue(definition is not null, $"Missing IR definition: {assetName}");
        var function = definition!["functions"]!.AsArray().First(f => f?["name"]?.GetValue<string>() == functionName)!;
        var graph = function["graph"]!;
        var nodes = graph["nodes"]!.AsArray().OfType<JsonObject>().ToList();
        var valueNodes = nodes.Where(n => n["type"]!.GetValue<string>() is not ("K2Node_FunctionEntry" or "K2Node_FunctionResult")).ToList();
        JsonNode body = graph;
        if (mode != "PureNodes")
        {
            ctx.AssertTrue(valueNodes.Count == 1, $"{assetName}: expected one expression node, got {valueNodes.Count}");
            string nodeType = mode == "Macro" ? "K2Node_MacroInstance" : "K2Node_MathExpression";
            ctx.AssertTrue(valueNodes[0]["type"]!.GetValue<string>() == nodeType, $"{assetName}: expected {nodeType}");
            ctx.AssertTrue(!valueNodes[0]["pins"]!.AsArray().Any(p => p?["type"]?.GetValue<string>() == "exec"),
                $"{assetName}: arithmetic expression must be pure");
            body = mode == "Macro" ? definition["macros"]!.AsArray().Single()!["graph"]! : valueNodes[0]["subgraph"]!;
        }
        var bodyNodes = body["nodes"]!.AsArray().OfType<JsonObject>().ToList();
        int actual = bodyNodes.Count(n => n["type"]!.GetValue<string>() is "K2Node_CallFunction" or "K2Node_PromotableOperator");
        ctx.AssertTrue(actual == operationCount, $"{assetName}: expected {operationCount} calculation nodes, got {actual}");
        var wires = body["connections"]!.AsArray();
        foreach (var node in bodyNodes)
        {
            foreach (var pin in node["pins"]!.AsArray().OfType<JsonObject>().Where(p =>
                p["direction"]!.GetValue<string>() == "output" && p["type"]!.GetValue<string>() != "exec"))
            {
                ctx.AssertTrue(wires.Any(w => w?["fromNode"]?.GetValue<string>() == node["id"]!.GetValue<string>()
                    && w?["fromPin"]?.GetValue<string>() == pin["name"]!.GetValue<string>()),
                    $"{assetName}: disconnected expression output {node["title"]}.{pin["name"]}");
            }
        }
        if (functionName == "G")
        {
            var vector = bodyNodes.Single(n => n["pins"]!.AsArray().Any(p => p?["name"]?.GetValue<string>() == "X"));
            var x = vector["pins"]!.AsArray().First(p => p?["name"]?.GetValue<string>() == "X");
            ctx.AssertTrue(double.TryParse(x?["defaultValue"]?.GetValue<string>(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value) && value == 10,
                $"{assetName}: vector constant X must remain 10");
        }
        ctx.Logger.Info(null, "[MExpr] verified {0}: mode={1}, calculationNodes={2}", assetName, mode, actual);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, path);
    }

    private static void VerifyArithmeticEdges(TestContext ctx, string mode)
    {
        string path = "/Game/Tests/UE5_0/MExpr_Edges_" + mode;
        BlueprintTestHelper.ApplyDsl(ctx, $$"""
[Blueprint(Path = "{{path}}", Mode = Create)]
[Extends(typeof(Actor))]
public class MExpr_Edges_{{mode}}
{
    [Function]
    float Evaluate(float x)
    {
        float value = x + 1.0f;
        value *= (x + 2.0f);
        return (value / 2.0f);
    }
    [Function]
    float Ratio() { return 1.0f / 2.0f; }
    [Function]
    int Half(int x) { return x / 2; }
    [Function]
    float Repeated(float x) { return (x + x) * (x + 1.0f); }
}
""");
        var response = BlueprintTestHelper.EditorCall(ctx, "get_asset_ir", new { path, level = "full" });
        var definition = response["response"]!["definition"]!;
        var functions = definition["functions"]!.AsArray();
        var ratio = functions.Single(f => f?["name"]?.GetValue<string>() == "Ratio")!;
        JsonNode ratioGraph = ratio["graph"]!;
        if (mode == "Macro")
            ratioGraph = definition["macros"]!.AsArray().Single(m => m!["inputs"]!.AsArray().Count == 0)!["graph"]!;
        else if (mode == "MathNodes")
            ratioGraph = ratioGraph["nodes"]!.AsArray().Single(n => n?["type"]?.GetValue<string>() == "K2Node_MathExpression")!["subgraph"]!;
        var divide = ratioGraph["nodes"]!.AsArray().Single(n => n?["type"]?.GetValue<string>() == "K2Node_CallFunction")!;
        var output = divide["pins"]!.AsArray().Single(p => p?["direction"]?.GetValue<string>() == "output");
        ctx.AssertTrue(output?["type"]?.GetValue<string>() == "double", $"{mode}: 1.0f / 2.0f must use floating division");
        // Engine UK2Node_MathExpression disables unary operators and resolves commutative
        // operators strictly left-to-right, so scalar-left forms (s * p) and -(x) are only
        // reproducible under PureNodes/Macro; the MathNodes probe stays within the parser's
        // supported forms (see doc task 3, which uses vector-left p * s).
        if (mode != "PureNodes")
        {
            string kind = mode == "Macro" ? "K2Node_MacroInstance" : "K2Node_MathExpression";
            var evaluate = functions.Single(f => f?["name"]?.GetValue<string>() == "Evaluate")!;
            ctx.AssertTrue(evaluate["graph"]!["nodes"]!.AsArray().Count(n => n?["type"]?.GetValue<string>() == kind) == 3,
                $"{mode}: initializer, compound assignment and parenthesized return must all follow the setting");
            var repeated = functions.Single(f => f?["name"]?.GetValue<string>() == "Repeated")!;
            var expression = repeated["graph"]!["nodes"]!.AsArray().Single(n => n?["type"]?.GetValue<string>() == kind)!;
            ctx.AssertTrue(expression["pins"]!.AsArray().Count(p => p?["direction"]?.GetValue<string>() == "input") == 1,
                $"{mode}: repeated x must share one expression input");
        }
        ctx.Logger.Info(null, "[MExpr] verified arithmetic edges: {0}", mode);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, path);
    }

    private static void BuildTask1Mcp(TestContext ctx)
    {
        // Task 1: Calc(float x, float y) => x * y + 1
        const string a1 = "/Game/Tests/UE5_0/MExpr_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Calc",
            new[] { new { name = "x", type = "float" }, new { name = "y", type = "float" } },
            outputs: new[] { new { name = "ReturnValue", type = "float" } });
        var mul1 = BlueprintTestHelper.AddNode(ctx, a1, "Calc", "Multiply_FloatFloat", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "x", mul1.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "y", mul1.nodeId, "B");
        var add1 = BlueprintTestHelper.AddNode(ctx, a1, "Calc", "Add_FloatFloat", 550, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, mul1.nodeId, "ReturnValue", add1.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a1, add1.nodeId, "B", "1");
        // Pure function nodes (no exec pins): exec chain goes entry -> result directly.
        BlueprintTestHelper.Connect(ctx, a1, add1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
    }

    private static void BuildTask2Mcp(TestContext ctx)
    {
        // Task 2: F(float a, float b, float c) => a * (b + c) / 2
        const string a2 = "/Game/Tests/UE5_0/MExpr_Multi_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "F",
            new[]
            {
                new { name = "a", type = "float" },
                new { name = "b", type = "float" },
                new { name = "c", type = "float" },
            },
            outputs: new[] { new { name = "ReturnValue", type = "float" } });
        var addInner2 = BlueprintTestHelper.AddNode(ctx, a2, "F", "Add_FloatFloat", 300, -100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "b", addInner2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "c", addInner2.nodeId, "B");
        var mul2 = BlueprintTestHelper.AddNode(ctx, a2, "F", "Multiply_FloatFloat", 550, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", mul2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a2, addInner2.nodeId, "ReturnValue", mul2.nodeId, "B");
        var div2 = BlueprintTestHelper.AddNode(ctx, a2, "F", "Divide_FloatFloat", 800, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, mul2.nodeId, "ReturnValue", div2.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a2, div2.nodeId, "B", "2");
        BlueprintTestHelper.Connect(ctx, a2, div2.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
    }

    private static void BuildTask3Mcp(TestContext ctx)
    {
        // Task 3: G(Vector p, float s) => p * s + (10,0,0)
        // A-side uses double s so it connects cleanly to Multiply_VectorFloat's double B pin
        // (the DSL keeps float s; the translator handles the float->double promotion).
        const string a3 = "/Game/Tests/UE5_0/MExpr_Vec_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "G",
            new[] { new { name = "p", type = "Vector" }, new { name = "s", type = "double" } },
            outputs: new[] { new { name = "ReturnValue", type = "Vector" } });
        var mulV3 = BlueprintTestHelper.AddNode(ctx, a3, "G", "Multiply_VectorFloat", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "p", mulV3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "s", mulV3.nodeId, "B");
        var makeVec3 = BlueprintTestHelper.AddNode(ctx, a3, "G", "MakeVector", 550, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, makeVec3.nodeId, "X", "10");
        BlueprintTestHelper.SetPinDefault(ctx, a3, makeVec3.nodeId, "Y", "0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, makeVec3.nodeId, "Z", "0");
        var addV3 = BlueprintTestHelper.AddNode(ctx, a3, "G", "Add_VectorVector", 800, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, mulV3.nodeId, "ReturnValue", addV3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, makeVec3.nodeId, "ReturnValue", addV3.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a3, addV3.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
    }

    // ── DSL (B-side, doc form verbatim) - reused verbatim for C/D via CopyB ──

    private const string DslTask1 = """
[Blueprint(Path = "/Game/Tests/UE5_0/MExpr_Basic_B", Mode = Create)]
[Extends(typeof(Actor))]
public class MExpr_Basic_B
{
    [Function]
    float Calc(float x, float y)
    {
        return x * y + 1.0f;
    }
}
""";

    private const string DslTask2 = """
[Blueprint(Path = "/Game/Tests/UE5_0/MExpr_Multi_B", Mode = Create)]
[Extends(typeof(Actor))]
public class MExpr_Multi_B
{
    [Function]
    float F(float a, float b, float c)
    {
        return a * (b + c) / 2.0f;
    }
}
""";

    private const string DslTask3 = """
[Blueprint(Path = "/Game/Tests/UE5_0/MExpr_Vec_B", Mode = Create)]
[Extends(typeof(Actor))]
public class MExpr_Vec_B
{
    [Function]
    Vector G(Vector p, float s)
    {
        return p * s + new Vector(10, 0, 0);
    }
}
""";

    /// <summary>
    /// Produces the same DSL as B-side (CopyB semantics) but retargeted to a different
    /// asset path + class name so it does not clobber the B-side asset. Used for C-side
    /// (Macro) and D-side (MathNodes): identical DSL code, only the setting differs.
    /// </summary>
    private static string CopyB(string bDsl, string suffix) =>
        bDsl
            .Replace("MExpr_Basic_B", "MExpr_Basic_" + suffix)
            .Replace("MExpr_Multi_B", "MExpr_Multi_" + suffix)
            .Replace("MExpr_Vec_B", "MExpr_Vec_" + suffix);

    /// <summary>
    /// Sets convertAssignmentOperatorsToBlueprintMacros via the `settings` tool. The tool
    /// writes McpSettingsManager.Shared.Current, which the DSL translator reads live.
    /// </summary>
    private static void SetAssignmentMode(TestContext ctx, string value)
    {
        var r = ctx.CallTool("settings", new()
        {
            ["action"] = "set",
            ["key"] = "convertAssignmentOperatorsToBlueprintMacros",
            ["value"] = value,
        });
        ctx.AssertTrue(r.Success, $"Cannot set arithmetic mode to {value}: {r.Message}");
        ctx.Logger.Info(null, "[MExpr] set convertAssignmentOperatorsToBlueprintMacros = {0} ({1})",
            value, r.Success ? "ok" : r.Message);
    }
}
