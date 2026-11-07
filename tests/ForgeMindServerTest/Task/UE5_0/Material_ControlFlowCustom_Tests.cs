// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 90 - Material Control Flow And Custom (UE 5.0)
//        - from the UE 5.0 material task doc "material - program control / custom"
//          (Tasks material group; the doc file name is the Chinese phrase for
//          "material - program control", which cannot be written in source)
//
// Engine nodes verified via SpkTool by their full MaterialExpression class names.
// ForLoop.Index is only available once BeginScope_For has established the loop; it cannot be wired
// to a material root output on its own.
// Q1 keeps the native loop: ExecBegin -> initialize -> ForLoop, LoopBody accumulates Index,
// Completed -> ExecEnd. ExecEnd returns the whole package built by SetMaterialAttributes, whose
// EmissiveColor reads the local variable after the loop completes. The legacy compiler's Make node
// only evaluates attribute-by-attribute and cannot serve this whole-package return path.
// With execution flow enabled in UE 5.0, material compilation goes only through ExecBegin
// (Material.cpp:5011-5015) and ExecEnd reads the MaterialAttributes input at the material root
// (MaterialExpressions.cpp:22007-22011); so this question still wires that input to a
// SetMaterialAttributes node but does not write bUseMaterialAttributes (unused on the exec-flow path).
// The UE 5.0 legacy compiler fixes the loop step at 1; this question's explicit input is also 1.
// Q2 keeps the native IfThenElse: ExecBegin -> declare local -> IfThenElse, Then/Else each write the
// selected value; the branch's red/black result is chosen by a data-side If (A/B are the float compare
// values, AGreaterThanB/ALessThanB are the two colors, single output takes the result,
// MaterialExpressions.cpp:10274-10318). A material attribute must not read the local outside the
// branch: in the legacy translator a local first declared inside a conditional scope has its
// declaration block emitted after the branches (HLSLMaterialTranslator::GetScopeCode outputs the
// dependency chunks first, then emits the child blocks in two passes by DeclaredScopeIndex != this
// level / == this level), so reading it outside yields undeclared identifier 'BranchChoice' (visible in
// the generated EvaluatePixelMaterialAttributes); Q1's accumulation completes inside the loop body and
// its declaration lands in the root scope, so it is not subject to this limit.
// Q3 uses the engine's own parameter name Input0 on both sides (a Custom node's inputs and outputs
// are fully customizable, pins addressed by FCustomInput.InputName): the constructor pre-seeds one
// blank row (MaterialExpressions.cpp:12025-12026), and adding a parameter by hand in the editor
// appends after it, so the first cell is unnamed and the first real parameter is compiled as
// "input 2"; the bridge now reuses that blank row first, so both sides' first named parameter lands
// in the first cell and the graph shape still matches. This question also fixes the bridge's Custom pin
// resolution: a name not listed as a parameter that collides with a static UPROPERTY
// (Code/OutputType/Description) falls through to the normal property lookup, otherwise Code would end
// up in Inputs[i].Input.Expression and the node would compile with empty Code reporting
// "Custom material Custom missing input 2 (Code)" (run 20261029_2246).
// Execution pins use the engine property real names Exec/LoopBody/Completed/Then/Else; no C# for/if
// statements are introduced. The test project enables r.MaterialEnableControlFlow in its startup
// config; only the first two questions turn on bEnableExecWire. Assertions cover compile, settings,
// node counts, data wires and exec wires.
// Mis-click comparison (VerifyIrSnapshot): inserted once the user named this file (collaboration spec
// section 9). Three per question - the A asset's self baseline, the B asset's self baseline, and one
// A/B cross-channel content-parity read after B is saved; the six assets each write their own
// graph-level baseline on the first round (no error) and later rounds diff against it. Each baseline
// freezes only its own channel's graph; only the cross-channel read asks whether the step commands
// and the DSL built the same graph.
// =========================================================================

public sealed class Material_ControlFlowCustom : ITestCase
{
    public string Name => "Material Control Flow And Custom (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 90;

    public async Task Run(TestContext ctx)
    {
        BuildQ1_ForLoopIndex(ctx);
        BuildQ2_IfThenElseCondition(ctx);
        BuildQ3_CustomCode(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Q1: accumulate Index inside the loop's execution scope, then output the completed result.
    // =====================================================================

    private static void BuildQ1_ForLoopIndex(TestContext ctx)
    {
        const string a1 = "/Game/Tests/UE5_0/Mat_For1_A";
        const string b1 = "/Game/Tests/UE5_0/Mat_For1_B";
        CreateControlFlowMaterial(ctx, a1);
        string begin = Add(ctx, a1, "ExecBegin");
        string finish = Add(ctx, a1, "ExecEnd");
        string start = Add(ctx, a1, "Constant", ("R", 0.0));
        string end = Add(ctx, a1, "Constant", ("R", 3.0));
        string step = Add(ctx, a1, "Constant", ("R", 1.0));
        string loop = Add(ctx, a1, "ForLoop");
        string total = Add(ctx, a1, "GetLocal", ("LocalName", "LoopTotal"));
        string sum = Add(ctx, a1, "Add");
        string initialize = Add(ctx, a1, "SetLocal", ("LocalName", "LoopTotal"));
        string accumulate = Add(ctx, a1, "SetLocal", ("LocalName", "LoopTotal"));
        string attributes = Add(ctx, a1, "SetMaterialAttributes");

        Connect(ctx, a1, start, loop, "StartIndex");
        Connect(ctx, a1, end, loop, "EndIndex");
        Connect(ctx, a1, step, loop, "IndexStep");
        Connect(ctx, a1, total, sum, "A");
        Connect(ctx, a1, loop, sum, "B", "Index");
        Connect(ctx, a1, start, initialize, "Value");
        Connect(ctx, a1, sum, accumulate, "Value");
        Connect(ctx, a1, begin, initialize, "Input", "Exec");
        Connect(ctx, a1, initialize, loop, "Input", "Exec");
        Connect(ctx, a1, loop, accumulate, "Input", "LoopBody");
        Connect(ctx, a1, loop, finish, "Input", "Completed");
        Connect(ctx, a1, total, attributes, "EmissiveColor");
        Connect(ctx, a1, attributes, "Material", "MaterialAttributes");
        FinishMaterial(ctx, a1);
        ExpectLoop(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);   // per-asset self-comparison

        ApplyDsl(ctx, "");
        FinishMaterial(ctx, b1);
        ExpectLoop(ctx, b1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b1);   // per-asset self-comparison
        // A/B parity (B saved above)
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1, b1);
    }

    // =====================================================================
    // Q2: choose red or black via the Then/Else execution chain.
    // =====================================================================

    private static void BuildQ2_IfThenElseCondition(TestContext ctx)
    {
        const string a2 = "/Game/Tests/UE5_0/Mat_ITE1_A";
        const string b2 = "/Game/Tests/UE5_0/Mat_ITE1_B";
        CreateControlFlowMaterial(ctx, a2);
        string begin = Add(ctx, a2, "ExecBegin");
        string finish = Add(ctx, a2, "ExecEnd");
        string cond = Add(ctx, a2, "ScalarParameter", ("name", "Cond"), ("defaultValue", 1.0));
        string red = Add(ctx, a2, "Constant3Vector", ("constant", "(R=1,G=0,B=0)"));
        string black = Add(ctx, a2, "Constant3Vector", ("constant", "(R=0,G=0,B=0)"));
        string zero = Add(ctx, a2, "Constant", ("R", 0.0));
        string one = Add(ctx, a2, "Constant", ("R", 1.0));
        string selection = Add(ctx, a2, "If");
        string initialize = Add(ctx, a2, "SetLocal", ("LocalName", "BranchChoice"));
        string selectRed = Add(ctx, a2, "SetLocal", ("LocalName", "BranchChoice"));
        string selectBlack = Add(ctx, a2, "SetLocal", ("LocalName", "BranchChoice"));
        string branch = Add(ctx, a2, "IfThenElse");
        string attributes = Add(ctx, a2, "SetMaterialAttributes");
        Connect(ctx, a2, zero, initialize, "Value");
        Connect(ctx, a2, one, selectRed, "Value");
        Connect(ctx, a2, zero, selectBlack, "Value");
        Connect(ctx, a2, cond, branch, "Condition");
        Connect(ctx, a2, begin, initialize, "Input", "Exec");
        Connect(ctx, a2, initialize, branch, "Input", "Exec");
        Connect(ctx, a2, branch, selectRed, "Input", "Then");
        Connect(ctx, a2, branch, selectBlack, "Input", "Else");
        Connect(ctx, a2, selectRed, finish, "Input", "Exec");
        Connect(ctx, a2, selectBlack, finish, "Input", "Exec");
        Connect(ctx, a2, cond, selection, "A");
        Connect(ctx, a2, zero, selection, "B");
        Connect(ctx, a2, red, selection, "AGreaterThanB");
        Connect(ctx, a2, black, selection, "ALessThanB");
        Connect(ctx, a2, selection, attributes, "BaseColor");
        Connect(ctx, a2, attributes, "Material", "MaterialAttributes");
        FinishMaterial(ctx, a2);
        ExpectBranch(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // per-asset self-comparison

        ApplyDsl(ctx, "");
        FinishMaterial(ctx, b2);
        ExpectBranch(ctx, b2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b2);   // per-asset self-comparison
        // A/B parity (B saved above)
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2, b2);
    }

    // =====================================================================
    // Q3: Custom HLSL snippet fed by a texture coordinate
    // =====================================================================

    private static void BuildQ3_CustomCode(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_Cust1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_Cust1_A");

        // Step 2: the UV source (engine property is CoordinateIndex)
        string uvId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionTextureCoordinate",
            CoordinateIndex = 0,
        }), "add MaterialExpressionTextureCoordinate(CoordinateIndex=0)");

        // Step 3: the Custom node - code text, one float output
        string custId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionCustom",
            Code = "return sin(Input0.x);",
            OutputType = "Float1",
        }), "add MaterialExpressionCustom(Code, OutputType=Float1)");

        // Step 4: the Custom node's argument, addressed by the label its FCustomInput
        // carries (the bridge names the blank row the engine constructor seeds, which is
        // the row the details panel would have had the user fill, rather than appending)
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = uvId,
            fromPin = "",
            toExpression = custId,
            toPin = "Input0",
        }), "connected", "connect TextureCoordinate -> Custom.Input0");

        // Step 5: Custom output -> EmissiveColor, compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = custId,
            fromPin = "",
            toExpression = "Material",
            toPin = "EmissiveColor",
        }), "connected", "connect Custom -> Material.EmissiveColor");
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_Cust1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_Cust1_A");
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionCustom", 1),
            ("MaterialExpressionTextureCoordinate", 1));

        // The B-side dynamic input uses the same Input0 name as the A side.
        ApplyDsl(ctx, "");
        FinishMaterial(ctx, "/Game/Tests/UE5_0/Mat_Cust1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_Cust1_B",
            ("MaterialExpressionCustom", 1),
            ("MaterialExpressionTextureCoordinate", 1));
        foreach (string path in new[] { a3, "/Game/Tests/UE5_0/Mat_Cust1_B" })
        {
            ExpectOutputLink(ctx, path, "EmissiveColor", "MaterialExpressionCustom");
            ExpectLinks(ctx, path, false, ("Custom", "Input0", "TextureCoordinate", false));
        }
        const string b3 = "/Game/Tests/UE5_0/Mat_Cust1_B";
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // per-asset self-comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, b3);   // per-asset self-comparison
        // A/B parity (B saved above)
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3, b3);
    }

    private static void CreateControlFlowMaterial(TestContext ctx, string path)
    {
        var result = EditorCall(ctx, "create_material", new
        {
            path, domain = "Surface", blendMode = "Opaque", bEnableExecWire = true,
        });
        StepOk(ctx, result, "created", $"create_material {path}");
        ctx.AssertTrue(GetBool(result, "settingsApplied"), $"{path}: material settings failed: {result}");
        ctx.AssertTrue(GetStr(result, "propertyErrors").Length == 0, $"{path}: {GetStr(result, "propertyErrors")}");
    }

    private static string Add(TestContext ctx, string path, string type, params (string Key, object Value)[] properties)
    {
        var payload = new Dictionary<string, object> { ["path"] = path, ["expressionClass"] = "MaterialExpression" + type };
        foreach (var property in properties) payload[property.Key] = property.Value;
        return ExpressionId(ctx, EditorCall(ctx, "add_material_expression", payload), $"add {type} to {path}");
    }

    private static void Connect(TestContext ctx, string path, string source, string target, string input, string output = "")
        => StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path, fromExpression = source, fromPin = output, toExpression = target, toPin = input,
        }), "connected", $"connect {source}.{output} -> {target}.{input}");

    private static void FinishMaterial(TestContext ctx, string path)
    {
        var result = EditorCall(ctx, "compile_material", new { path });
        StepOk(ctx, result, "compiled", $"compile_material {path}");
        ctx.AssertTrue(GetStr(result, "shaderMapState") == "finished", $"{path}: shader compilation is not finished: {result}");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path }), "saved", $"save_asset {path}");
    }

    private static void ExpectLoop(TestContext ctx, string path)
    {
        ExpectExpressions(ctx, path, ("MaterialExpressionExecBegin", 1), ("MaterialExpressionExecEnd", 1),
            ("MaterialExpressionForLoop", 1), ("MaterialExpressionConstant", 3), ("MaterialExpressionGetLocal", 1),
            ("MaterialExpressionSetLocal", 2), ("MaterialExpressionAdd", 1), ("MaterialExpressionSetMaterialAttributes", 1));
        ExpectOutputLink(ctx, path, "MaterialAttributes", "MaterialExpressionSetMaterialAttributes");
        ExpectLinks(ctx, path, true,
            ("ExecBegin", "Exec", "SetLocal", true), ("SetLocal", "Exec", "ForLoop", true),
            ("ForLoop", "LoopBody", "SetLocal", true), ("ForLoop", "Completed", "ExecEnd", true),
            ("ForLoop", "StartIndex", "Constant", false), ("ForLoop", "EndIndex", "Constant", false),
            ("ForLoop", "IndexStep", "Constant", false), ("Add", "A", "GetLocal", false),
            ("Add", "B", "ForLoop", false), ("SetLocal", "Value", "Add", false),
            ("SetLocal", "Value", "Constant", false), ("SetMaterialAttributes", "EmissiveColor", "GetLocal", false));
    }

    private static void ExpectBranch(TestContext ctx, string path)
    {
        ExpectExpressions(ctx, path, ("MaterialExpressionExecBegin", 1), ("MaterialExpressionExecEnd", 1),
            ("MaterialExpressionIfThenElse", 1), ("MaterialExpressionScalarParameter", 1), ("MaterialExpressionConstant3Vector", 2),
            ("MaterialExpressionConstant", 2), ("MaterialExpressionSetLocal", 3),
            ("MaterialExpressionIf", 1), ("MaterialExpressionSetMaterialAttributes", 1));
        ExpectOutputLink(ctx, path, "MaterialAttributes", "MaterialExpressionSetMaterialAttributes");
        ExpectLinks(ctx, path, true,
            ("ExecBegin", "Exec", "SetLocal", true), ("SetLocal", "Exec", "IfThenElse", true),
            ("IfThenElse", "Then", "SetLocal", true), ("IfThenElse", "Else", "SetLocal", true),
            ("SetLocal", "Exec", "ExecEnd", true), ("IfThenElse", "Condition", "ScalarParameter", false),
            ("If", "A", "ScalarParameter", false), ("If", "B", "Constant", false),
            ("If", "AGreaterThanB", "Constant3Vector", false), ("If", "ALessThanB", "Constant3Vector", false),
            ("SetMaterialAttributes", "BaseColor", "If", false));
    }

    private static void ExpectLinks(TestContext ctx, string path, bool execution,
        params (string Owner, string Pin, string Other, bool Exec)[] links)
    {
        var result = EditorCall(ctx, "get_asset_ir", new { path, level = "graph" });
        ctx.AssertTrue(GetBool(result, "success"), $"get_asset_ir {path}: {result}");
        var definition = result["response"]?["definition"];
        var nodes = definition?["nodes"]?.AsArray();
        ctx.AssertTrue(nodes is not null, $"{path}: missing expression graph");
        if (execution)
        {
            var settings = definition?["materialSettings"];
            ctx.AssertTrue(settings?["bEnableExecWire"]?.GetValue<bool>() == true
                && settings?["compiledWithExecutionFlow"]?.GetValue<bool>() == true,
                $"{path}: execution flow was not enabled: {settings}");
            var localNames = nodes!.Where(n => n?["type"]?.GetValue<string>() is "MaterialExpressionGetLocal" or "MaterialExpressionSetLocal")
                .Select(n => n?["localName"]?.GetValue<string>()).ToArray();
            ctx.AssertTrue(localNames.Length > 0 && localNames.All(n => !string.IsNullOrEmpty(n) && n == localNames[0]),
                $"{path}: local variable names do not agree: {string.Join(",", localNames)}");
        }
        foreach (var link in links)
        {
            bool found = nodes!.Any(n => n?["type"]?.GetValue<string>() == "MaterialExpression" + link.Owner
                && (n?["inputs"]?.AsArray() ?? new JsonArray()).Any(input =>
                    input?["name"]?.GetValue<string>() == link.Pin
                    && (input?["kind"]?.GetValue<string>() == "exec") == link.Exec
                    && nodes!.Any(other => other?["id"]?.GetValue<string>() == input?["expression"]?.GetValue<string>()
                        && other?["type"]?.GetValue<string>() == "MaterialExpression" + link.Other)));
            ctx.AssertTrue(found, $"{path}: missing {(link.Exec ? "exec" : "data")} link {link.Owner}.{link.Pin} -> {link.Other}");
        }
    }
}
