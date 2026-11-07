// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json.Nodes;
using static ForgeMindServer.Test.BlueprintTestHelper;

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 79 - Material Function (UE 5.0) - from Tasks/UE_5.0/Material/Material_Function.md
//
// MaterialExpressionMaterialFunctionCall (UMaterialExpressionMaterialFunctionCall: a
// Function reference plus per-input / per-output FFunctionExpression* bookkeeping) calls a
// Material Function asset from a material graph; UMaterialExpressionFunctionInput (InputName,
// InputType, Preview) and UMaterialExpressionFunctionOutput (OutputName, FExpressionInput A)
// declare that function's interface inside the function graph. UE 5.0.3 source:
// Classes/Materials/MaterialExpressionMaterialFunctionCall.h, MaterialExpressionFunctionInput.h,
// MaterialExpressionFunctionOutput.h - all three classes are verified in the UE 5.0 engine index.
//
// Assumed capabilities (user decision 2026-10-25: "Assumed to be implemented, used as transcribed content"). The classes
// exist, but the channels that have to build with them do not carry this shape yet, and this case
// is transcribed against the end state:
//   1. Material step commands are material-only today: create_material / add_material_expression /
//      connect_material_pins / compile_material each resolve their path with Cast<UMaterial>, so a
//      Material Function asset cannot be created or edited through them. Q2 therefore uses the
//      create_material_function command (the name following the existing create_material /
//      create_data_table convention) and then the expression and pin commands on that path.
//   2. The Asset IR material handler builds a material_function, but its ApplyGraph skips every
//      node whose type is FunctionInput or FunctionOutput, so a DSL function asset ends with a body
//      and no interface. Q2's B side is written in the documented [MaterialFunction] +
//      [FunctionInput] / [FunctionOutput] form, which the translator already lowers; what has to
//      land is the handler side of those two node types.
//   3. A function call's input pins are not static FExpressionInput members (they are the node's
//      dynamic per-input list), so the pin-addressing lookup by UPROPERTY name cannot see them.
//      Both channels wire them by the name the task doc gives ("In" in Q1, "Input" in Q3).
//   4. The function assets Q1 and Q3 call (/Game/Tests/UE5_0/F_MyFunc, /Game/Tests/UE5_0/F_A,
//      /Game/Tests/UE5_0/F_B) are what the doc names under /Game/Functions; the project ships no
//      material function asset at all, so the Function reference is expected to resolve only once
//      those assets exist. The add step is the check: an asset reference that does not resolve fails
//      the step instead of leaving a null Function behind. The doc only ever calls them - it never
//      says how they are built - so the case builds them itself before Q1 (see EnsureCalledFunctions),
//      with the interfaces the questions address: F_MyFunc takes "In", F_A and F_B take "Input".
//      They are case-owned assets, so they are kept under /Game/Tests with the rest of what this case
//      builds - inside the folder the group lifecycle wipes before every run - and are therefore
//      rebuilt from scratch on every run instead of being carried over from an earlier one.
//
// Interface naming: Q2 names the output "Out". The engine's UMaterialExpressionFunctionOutput holds
// the wired expression in its FExpressionInput A, so the A side connects to pin "A" (section 8:
// resolve by what is actually found); the DSL's "Out = Body;" is lowered by the function translator
// under the entry name "Input", which is the name the assumed handler has to accept as the same wire.
//
// Texture note: Q3's doc names a bare /Game/T_A that does not exist in this project; a sample with an
// unresolved reference is not cosmetic (the null texture becomes a compile error on every node wired
// to it), so the case uses the project's real clay-brick diffuse, the substitution Material If
// (No. 74) already applies, and both sides read back the built graph.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Every asset this case builds - the
// three prerequisite functions (F_MyFunc / F_A / F_B) and the six question assets - stores
// its own graph-level IR baseline on first run (no failure); later runs diff against it,
// catching accidental damage to already-tested structures (per-asset self-comparison, not
// A/B cross-comparison). Q1 / Q3 materials are snapshot-read after reload_asset, so the
// baseline rests on what was persisted, matching what a reopened editor shows.
// =========================================================================

public sealed class Material_Function : ITestCase
{
    public string Name => "Material Function (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 79;

    public async Task Run(TestContext ctx)
    {
        EnsureCalledFunctions(ctx);
        BuildQ1_FunctionCall(ctx);
        BuildQ2_FunctionInterface(ctx);
        BuildQ3_NestedFunctionCall(ctx);

        await Task.CompletedTask;
    }

    // =====================================================================
    // Prerequisite: the material functions the three questions call
    // =====================================================================

    /// <summary>
    /// Build the three material functions the questions call, under /Game/Tests like every other
    /// asset this case owns: the folder is wiped before every run, so they are rebuilt each run
    /// rather than carried over, and create_* never has to clobber an existing asset.
    /// </summary>
    private static void EnsureCalledFunctions(TestContext ctx)
    {
        // F_MyFunc(In) = In * white: the doc's Q1 reads "scalar parameter in, colour out",
        // which is what feeding the scalar through a multiply by white spells out.
        {
            const string fn = "/Game/Tests/UE5_0/F_MyFunc";
            StepOk(ctx, EditorCall(ctx, "create_material_function", new { path = fn }),
                "created", "create_material_function F_MyFunc");

            string inId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
            {
                path = fn,
                expressionClass = "MaterialExpressionFunctionInput",
                inputName = "In",
                inputType = "Scalar",
            }), "add FunctionInput(In) of F_MyFunc");

            string whiteId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
            {
                path = fn,
                expressionClass = "MaterialExpressionConstant3Vector",
                constant = "(R=1,G=1,B=1)",
            }), "add Constant3Vector(1,1,1) of F_MyFunc");

            string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
            {
                path = fn,
                expressionClass = "MaterialExpressionMultiply",
            }), "add Multiply of F_MyFunc");

            StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
            {
                path = fn,
                fromExpression = inId,
                fromPin = "",
                toExpression = mulId,
                toPin = "A",
            }), "connected", "connect In -> Multiply.A (F_MyFunc)");

            StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
            {
                path = fn,
                fromExpression = whiteId,
                fromPin = "",
                toExpression = mulId,
                toPin = "B",
            }), "connected", "connect White -> Multiply.B (F_MyFunc)");

            string outId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
            {
                path = fn,
                expressionClass = "MaterialExpressionFunctionOutput",
                outputName = "Out",
            }), "add FunctionOutput(Out) of F_MyFunc");

            StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
            {
                path = fn,
                fromExpression = mulId,
                fromPin = "",
                toExpression = outId,
                toPin = "A",
            }), "connected", "connect Multiply -> FunctionOutput(Out).A (F_MyFunc)");

            StepOk(ctx, EditorCall(ctx, "compile_material", new { path = fn }),
                "compiled", "compile_material F_MyFunc");
            StepOk(ctx, EditorCall(ctx, "save_asset", new { path = fn }),
                "saved", "save_asset F_MyFunc");
            BlueprintTestHelper.VerifyIrSnapshot(ctx, fn);
        }

        // F_A: the doc's single unnamed input, fed with TexA.RGBA, straight through.
        BuildPassThroughFunction(ctx, "/Game/Tests/UE5_0/F_A", bMaskToRgb: false);

        // F_B: the same, masked back to RGB so the nested chain ends on a colour.
        BuildPassThroughFunction(ctx, "/Game/Tests/UE5_0/F_B", bMaskToRgb: true);
    }

    /// <summary>
    /// Build a one-input / one-output material function: FunctionInput("Input") -> optional
    /// ComponentMask(RGB) -> FunctionOutput("Out"). The same step commands build it that build
    /// the materials below, which is what makes a function asset editable through them at all.
    /// </summary>
    private static void BuildPassThroughFunction(TestContext ctx, string fn, bool bMaskToRgb)
    {
        StepOk(ctx, EditorCall(ctx, "create_material_function", new { path = fn }),
            "created", $"create_material_function {fn}");

        string inId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = fn,
            expressionClass = "MaterialExpressionFunctionInput",
            inputName = "Input",
            inputType = "Vector4",
        }), $"add FunctionInput(Input) of {fn}");

        string sourceId = inId;
        if (bMaskToRgb)
        {
            string maskId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
            {
                path = fn,
                expressionClass = "MaterialExpressionComponentMask",
                r = true,
                g = true,
                b = true,
                a = false,
            }), $"add ComponentMask(RGB) of {fn}");

            StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
            {
                path = fn,
                fromExpression = inId,
                fromPin = "",
                toExpression = maskId,
                toPin = "Input",
            }), "connected", $"connect Input -> ComponentMask.Input ({fn})");
            sourceId = maskId;
        }

        string outId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = fn,
            expressionClass = "MaterialExpressionFunctionOutput",
            outputName = "Out",
        }), $"add FunctionOutput(Out) of {fn}");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = fn,
            fromExpression = sourceId,
            fromPin = "",
            toExpression = outId,
            toPin = "A",
        }), "connected", $"connect body -> FunctionOutput(Out).A ({fn})");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = fn }),
            "compiled", $"compile_material {fn}");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = fn }),
            "saved", $"save_asset {fn}");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, fn);
    }

    // =====================================================================
    // Q1: a material calling a function asset (scalar parameter in, BaseColor out)
    // =====================================================================

    private static void BuildQ1_FunctionCall(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a1 = "/Game/Tests/UE5_0/Mat_MFC1_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a1,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_MFC1_A");

        // Step 2: add ScalarParameter (In = 0.5)
        string inId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionScalarParameter",
            name = "In",              // per task spec
            defaultValue = 0.5,       // per task spec
        }), "add MaterialExpressionScalarParameter(In)");

        // Step 3: add MaterialFunctionCall for /Game/Tests/UE5_0/F_MyFunc and feed the
        // parameter into its "In" input. The function reference is written as the node's
        // own property, so a path that does not resolve fails this step.
        string fnId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a1,
            expressionClass = "MaterialExpressionMaterialFunctionCall",
            function = "/Game/Tests/UE5_0/F_MyFunc",  // per task spec
        }), "add MaterialExpressionMaterialFunctionCall(F_MyFunc)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = inId,
            fromPin = "",
            toExpression = fnId,
            toPin = "In",             // per task spec: the function's In input
        }), "connected", "connect In -> FunctionCall.In");

        // Step 4: link function output -> BaseColor, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a1,
            fromExpression = fnId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",      // per task spec
        }), "connected", "connect FunctionCall -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a1 }),
            "compiled", "compile_material Mat_MFC1_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a1 }),
            "saved", "save_asset Mat_MFC1_A");
        ReloadAsset(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        ExpectExpressions(ctx, a1,
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionMaterialFunctionCall", 1));
        ExpectFunctionCallInput(ctx, a1, "MaterialExpressionMaterialFunctionCall_0", "In",
            "MaterialExpressionScalarParameter_0");

        // -- B-side: DSL in the documented material syntax; a function asset is called by
        // its using-alias name, which the translator lowers to a MaterialFunctionCall node
        // carrying the resolved functionAsset. --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_MFC1_B" }),
            "saved", "save_asset Mat_MFC1_B");
        ReloadAsset(ctx, "/Game/Tests/UE5_0/Mat_MFC1_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_MFC1_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_MFC1_B",
            ("MaterialExpressionScalarParameter", 1),
            ("MaterialExpressionMaterialFunctionCall", 1));
        ExpectFunctionCallInput(ctx, "/Game/Tests/UE5_0/Mat_MFC1_B",
            "MaterialExpressionMaterialFunctionCall_0", "In",
            "MaterialExpressionScalarParameter_0");
    }

    // =====================================================================
    // Q2: the function graph itself - FunctionInput / FunctionOutput around a body
    // =====================================================================

    private static void BuildQ2_FunctionInterface(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a2 = "/Game/Tests/UE5_0/MatFunc_InOut_A";

        // Step 1: create the material function asset (the doc's function F_InOut; the
        // asset is the task doc's AssetA path).
        StepOk(ctx, EditorCall(ctx, "create_material_function", new
        {
            path = a2,
        }), "created", "create_material_function MatFunc_InOut_A");

        // Step 2: add FunctionInput (Val, scalar)
        string valId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionFunctionInput",
            inputName = "Val",                    // per task spec
            inputType = "Scalar",                 // FunctionInput_Scalar
        }), "add MaterialExpressionFunctionInput(Val)");

        // Step 3: add FunctionOutput (Out)
        string outId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionFunctionOutput",
            outputName = "Out",                   // per task spec
        }), "add MaterialExpressionFunctionOutput(Out)");

        // Step 4: Multiply: A = Val, B = Constant(2.0); the product feeds the output node
        string twoId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionConstant",
            constant = "2.0",                     // per task spec
        }), "add MaterialExpressionConstant(2.0)");

        string mulId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a2,
            expressionClass = "MaterialExpressionMultiply",
        }), "add MaterialExpressionMultiply");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = valId,
            fromPin = "",
            toExpression = mulId,
            toPin = "A",
        }), "connected", "connect FunctionInput(Val) -> Multiply.A");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = twoId,
            fromPin = "",
            toExpression = mulId,
            toPin = "B",
        }), "connected", "connect Constant(2) -> Multiply.B");

        // UMaterialExpressionFunctionOutput stores the wired expression in its FExpressionInput A.
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a2,
            fromExpression = mulId,
            fromPin = "",
            toExpression = outId,
            toPin = "A",
        }), "connected", "connect Multiply -> FunctionOutput(Out).A");

        // Step 5: compile / save
        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a2 }),
            "compiled", "compile_material MatFunc_InOut_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a2 }),
            "saved", "save_asset MatFunc_InOut_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        ExpectExpressions(ctx, a2,
            ("MaterialExpressionFunctionInput", 1),
            ("MaterialExpressionFunctionOutput", 1),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionMultiply", 1));

        // -- B-side: DSL in the documented material function syntax --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/MatFunc_InOut_B" }),
            "saved", "save_asset MatFunc_InOut_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/MatFunc_InOut_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/MatFunc_InOut_B",
            ("MaterialExpressionFunctionInput", 1),
            ("MaterialExpressionFunctionOutput", 1),
            ("MaterialExpressionConstant", 1),
            ("MaterialExpressionMultiply", 1));
    }

    // =====================================================================
    // Q3: nested function calls (texture through F_A into F_B)
    // =====================================================================

    private static void BuildQ3_NestedFunctionCall(TestContext ctx)
    {
        // -- A-side: MCP step commands (per task spec) --
        const string a3 = "/Game/Tests/UE5_0/Mat_NestFn_A";

        // Step 1: create material
        StepOk(ctx, EditorCall(ctx, "create_material", new
        {
            path = a3,
            domain = "Surface",
            blendMode = "Opaque",
        }), "created", "create_material Mat_NestFn_A");

        // Step 2: add TextureSample (TexA)
        // the doc's T_A: the project's clay-brick diffuse
        string texId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionTextureSample",
            texture = "/Game/StarterContent/Textures/T_Brick_Clay_New_D",
        }), "add MaterialExpressionTextureSample(T_A)");

        // Step 3: add MaterialFunctionCall (F_A), input = TexA.RGBA
        string fnAId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMaterialFunctionCall",
            function = "/Game/Tests/UE5_0/F_A",     // per task spec
        }), "add MaterialFunctionCall(F_A)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = texId,
            fromPin = "RGBA",         // per task spec: TexA.RGBA
            toExpression = fnAId,
            toPin = "Input",          // per task spec: the single unnamed function input
        }), "connected", "connect TexA.RGBA -> F_A.Input");

        // Step 4: wrap a second MaterialFunctionCall (F_B) around F_A's output
        string fnBId = ExpressionId(ctx, EditorCall(ctx, "add_material_expression", new
        {
            path = a3,
            expressionClass = "MaterialExpressionMaterialFunctionCall",
            function = "/Game/Tests/UE5_0/F_B",     // per task spec
        }), "add MaterialFunctionCall(F_B)");

        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = fnAId,
            fromPin = "",
            toExpression = fnBId,
            toPin = "Input",          // per task spec
        }), "connected", "connect F_A -> F_B.Input");

        // Step 5: link -> BaseColor, then compile / save
        StepOk(ctx, EditorCall(ctx, "connect_material_pins", new
        {
            path = a3,
            fromExpression = fnBId,
            fromPin = "",
            toExpression = "Material",
            toPin = "BaseColor",      // per task spec
        }), "connected", "connect F_B -> Material.BaseColor");

        StepOk(ctx, EditorCall(ctx, "compile_material", new { path = a3 }),
            "compiled", "compile_material Mat_NestFn_A");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = a3 }),
            "saved", "save_asset Mat_NestFn_A");
        ReloadAsset(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        ExpectExpressions(ctx, a3,
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionMaterialFunctionCall", 2));
        ExpectFunctionCallInput(ctx, a3, "MaterialExpressionMaterialFunctionCall_0", "Input",
            "MaterialExpressionTextureSample_0", expectedOutputIndex: 5);
        ExpectFunctionCallInput(ctx, a3, "MaterialExpressionMaterialFunctionCall_1", "Input",
            "MaterialExpressionMaterialFunctionCall_0");

        // -- B-side: DSL --
        ApplyDsl(ctx, "");
        StepOk(ctx, EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/Mat_NestFn_B" }),
            "saved", "save_asset Mat_NestFn_B");
        ReloadAsset(ctx, "/Game/Tests/UE5_0/Mat_NestFn_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Mat_NestFn_B");
        ExpectExpressions(ctx, "/Game/Tests/UE5_0/Mat_NestFn_B",
            ("MaterialExpressionTextureSample", 1),
            ("MaterialExpressionMaterialFunctionCall", 2));
        ExpectFunctionCallInput(ctx, "/Game/Tests/UE5_0/Mat_NestFn_B",
            "MaterialExpressionMaterialFunctionCall_0", "Input",
            "MaterialExpressionTextureSample_0", expectedOutputIndex: 5);
        ExpectFunctionCallInput(ctx, "/Game/Tests/UE5_0/Mat_NestFn_B",
            "MaterialExpressionMaterialFunctionCall_1", "Input",
            "MaterialExpressionMaterialFunctionCall_0");
    }

    // =====================================================================
    // Response parsing helpers
    // =====================================================================

    /// <summary>
    /// Assert a step command reported success and return the expressionId it issued. An add step
    /// writes every extra payload key as a property and reports the ones it could not write in
    /// propertyErrors while still answering added=true, so that field is checked here too: the
    /// function interface is exactly what Q2 is about, and a node created without the name the
    /// step asked for is a failed step, not a pass.
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
    /// Read the finished asset back through get_asset_ir and assert that every expression class the
    /// task doc names is present with the expected multiplicity. Not a snapshot: it fails with the
    /// missing class named, which is how an interface node the handler does not build yet (see the
    /// assumed-capability note above) surfaces instead of being reported as a pass.
    /// </summary>
    private static void ExpectExpressions(TestContext ctx, string assetPath, params (string Type, int Count)[] expected)
    {
        var response = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(response, "success"), $"get_asset_ir {assetPath}: {GetStr(response, "error")}");
        var graph = response["response"]?["definition"]?["nodes"]?.AsArray();
        ctx.AssertTrue(graph is not null, $"get_asset_ir {assetPath}: definition carries no nodes: {response.ToJsonString()}");
        var nodes = graph!;

        foreach ((string type, int count) in expected)
        {
            int found = nodes.Count(n => n?["type"]?.GetValue<string>() == type);
            ctx.AssertTrue(found >= count,
                $"{assetPath}: expected {count} x {type}, found {found}. Built graph: " +
                string.Join(", ", nodes.Select(n => n?["type"]?.GetValue<string>() ?? "?")));
        }
    }

    /// <summary>
    /// Drop the asset's in-memory package and load it back from disk, so every read-back
    /// assertion that follows verifies what save_asset actually persisted instead of the
    /// still-alive build-time object. This is the automated stand-in for the visual check
    /// "reopen the project and look at the graph": a wire that only exists in memory never
    /// survives this step, and one that did survive shows up in the read-back IR.
    /// </summary>
    private static void ReloadAsset(TestContext ctx, string assetPath)
    {
        var response = EditorCall(ctx, "reload_asset", new { path = assetPath });
        ctx.AssertTrue(GetBool(response, "reloaded"),
            $"reload_asset {assetPath}: {GetStr(response, "reason")}");
    }

    /// <summary>
    /// Assert that a function call node's dynamic input wire is present in read-back IR: the node's
    /// inputs entry named pinName must point at expectedSourceId. A function call's inputs live in
    /// its FunctionInputs array (not static class members), so without the read side exposing them
    /// the wire stays invisible to verification while every build step keeps reporting success -
    /// which is exactly how Q1 / Q3 passed their checks while the editor showed no wires.
    /// When expectedOutputIndex is given (>= 0) the wire's outputIndex is asserted too: a request
    /// like TexA.RGBA must land on the engine's RGBA output (index 5 on a TextureSample), because
    /// an index-0 miswire feeds the target a float3 and only surfaces later as a shader cast error.
    /// </summary>
    private static void ExpectFunctionCallInput(TestContext ctx, string assetPath, string callId,
        string pinName, string sourceId, int expectedOutputIndex = -1)
    {
        var response = EditorCall(ctx, "get_asset_ir", new { path = assetPath, level = "graph" });
        ctx.AssertTrue(GetBool(response, "success"), $"get_asset_ir {assetPath}: {GetStr(response, "error")}");
        var nodes = response["response"]?["definition"]?["nodes"]?.AsArray();
        ctx.AssertTrue(nodes is not null, $"get_asset_ir {assetPath}: definition carries no nodes: {response.ToJsonString()}");

        var call = nodes!.FirstOrDefault(n => n?["id"]?.GetValue<string>() == callId);
        ctx.AssertTrue(call is not null, $"{assetPath}: node '{callId}' not found in read-back IR");

        var inputs = call!["inputs"]?.AsArray();
        ctx.AssertTrue(inputs is not null,
            $"{assetPath}: function call '{callId}' read back with no inputs at all - its dynamic " +
            $"FunctionInputs wires are invisible to get_asset_ir");

        var wire = inputs!.FirstOrDefault(i => i?["name"]?.GetValue<string>() == pinName);
        ctx.AssertTrue(wire is not null,
            $"{assetPath}: function call '{callId}' has no input '{pinName}' wired. Read-back inputs: " +
            string.Join(", ", inputs.Select(i => $"{i?["name"]} -> {i?["expression"]}")));

        ctx.AssertTrue(wire!["expression"]?.GetValue<string>() == sourceId,
            $"{assetPath}: '{callId}.{pinName}' wired to '{wire["expression"]?.GetValue<string>()}', " +
            $"expected '{sourceId}'");

        if (expectedOutputIndex >= 0)
        {
            ctx.AssertTrue(wire!["outputIndex"]?.GetValue<int>() == expectedOutputIndex,
                $"{assetPath}: '{callId}.{pinName}' wired from output index " +
                $"{wire["outputIndex"]?.GetValue<int?>()}, expected {expectedOutputIndex}");
        }
    }
}
