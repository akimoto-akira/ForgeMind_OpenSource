// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 52 - Blueprint Bitmask (UE 5.0) - from Tasks/UE_5.0/blueprint-bitmask.md
//
// Bitmask usage in blueprints (user guidance 2026-10-16):
//   - Variable type is int (NOT the enum type). The variable has MD_Bitmask
//     metadata and MD_BitmaskEnum pointing to the flag enum.
//   - Variable declaration default is a SINGLE flag value (e.g. A), not a
//     combination (A|C). Set nodes may assign combinations at runtime.
//   - DSL: [Variable(Bitmask)] EMyFlags Flags = EMyFlags.A;
//     The translator sees [Bitmask] + enum type → emits type=int + bitmaskEnum.
//
// Three spec-6 gaps eliminated (2026-10-16):
//   - Bitflags enum meta: create_blueprint_enum accepts 'bitflags' payload;
//     add_enum_entry accepts 'value' (explicit power-of-two).
//   - MakeBitmask node: UK2Node_BitmaskLiteral in bridge whitelist; enum from
//     payload 'enum' → BitflagsEnum before AllocateDefaultPins.
//   - Bitmask variable default: EnsureCompiledVariableDefault handles int +
//     MD_BitmaskEnum → TranslateEnumDefaultText → write numeric value to CDO.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Bitmask variable with single-flag default (Flags = A)
//   Task 2 - Bitmask OR combination (int Combine() = A | B)
//   Task 3 - Bit test / HasFlag (bool HasA() = (Flags & A) != 0)
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction after visual
// confirmation. Every built asset - both Bitmask_* blueprints and both EMyFlags_* enums,
// A-side (MCP) and DSL-side - stores its own graph-level IR baseline on first run and
// diffs against it later (per-asset self-comparison, not A/B cross-comparison). A-side
// snapshots run right after the A-side build; the build is unwrapped, so a bridge
// failure aborts the case instead of being swallowed.
// =========================================================================

public sealed class Blueprint_Bitmask : ITestCase
{
    public string Name => "Blueprint Bitmask (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 52;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    private static void BuildTask1(TestContext ctx)
    {
        const string emy = "/Game/Tests/UE5_0/EMyFlags_A";
        const string a1 = "/Game/Tests/UE5_0/Bitmask_Lit_A";

        {
            // Bitflags enum with explicit power-of-two values (A=1, B=2, C=4).
            BlueprintTestHelper.EditorCall(ctx, "create_blueprint_enum", new { path = emy, bitflags = true });
            var flagEntries = new[] { ("A", 1), ("B", 2), ("C", 4) };
            foreach (var (ename, eval) in flagEntries)
            {
                BlueprintTestHelper.EditorCall(ctx, "add_enum_entry", new { path = emy, name = ename, value = eval });
            }
            var save = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = emy });
            ctx.AssertTrue(save["response"]?["saved"]?.GetValue<bool>() ?? false,
                $"save_asset {emy}: {save["response"]?["reason"]?.GetValue<string>() ?? ""}");

            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            // Bitmask variable: type is int, not the enum. MD_Bitmask + MD_BitmaskEnum
            // associate it with the flag enum. Declaration default is a single flag (A).
            BlueprintTestHelper.AddVariable(ctx, a1, "Flags", "int", "A",
                bitmask: true, bitmaskEnum: emy);
            BlueprintTestHelper.CompileAndSave(ctx, a1);
        }

        // Mis-touch comparison (IR snapshot) - runs only after the A-side build succeeded,
        // so a graph/asset regression aborts the case instead of passing silently.
        BlueprintTestHelper.VerifyIrSnapshot(ctx, emy);   // EMyFlags_A
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);    // Bitmask_Lit_A

        // DSL: declare EMyFlags as a Bitflags enum, then a blueprint with a bitmask
        // variable. DSL writes the enum type; translator converts to int + bitmaskEnum.
        // Declaration default is a single flag (A), not a combination.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // DSL enums are not saved by the apply, so EMyFlags_B gets an explicit save_asset
        // before its snapshot (same as Blueprint_Enum_Tests' EColor_B).
        BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = "/Game/Tests/UE5_0/EMyFlags_B" });
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EMyFlags_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Bitmask_Lit_B");

        // A/B equivalence read-back: the variable's bitmaskEnum metadata must carry
        // the resolved enum's SHORT name (the exact value the details panel's
        // "Bitmask Enum" combo matches). A-side enum FName EMyFlags_A, B-side EMyFlags_B.
        var aEnum = BlueprintTestHelper.GetVariableBitmaskEnum(ctx, a1, "Flags");
        var bEnum = BlueprintTestHelper.GetVariableBitmaskEnum(ctx, "/Game/Tests/UE5_0/Bitmask_Lit_B", "Flags");
        ctx.Logger.Info(null, "[Bitmask] Lit read-back bitmaskEnum: A='{0}' B='{1}'", aEnum, bEnum);
        ctx.AssertTrue(aEnum == "EMyFlags_A", $"Bitmask_Lit_A Flags.bitmaskEnum expected 'EMyFlags_A' got '{aEnum}'");
        ctx.AssertTrue(bEnum == "EMyFlags_B", $"Bitmask_Lit_B Flags.bitmaskEnum expected 'EMyFlags_B' got '{bEnum}'");

        // Default value read-back: Flags = A (value 1). The CDO default must be the
        // resolved numeric flag, not 0 - this is what the details panel shows as the
        // bitmask checkbox state for entry A.
        var aDef = BlueprintTestHelper.GetVariableField(ctx, a1, "Flags", "defaultValue");
        var bDef = BlueprintTestHelper.GetVariableField(ctx, "/Game/Tests/UE5_0/Bitmask_Lit_B", "Flags", "defaultValue");
        ctx.Logger.Info(null, "[Bitmask] Lit read-back defaultValue: A='{0}' B='{1}'", aDef, bDef);
        ctx.AssertTrue(aDef == "1", $"Bitmask_Lit_A Flags.defaultValue expected '1' (A) got '{aDef}'");
        ctx.AssertTrue(bDef == "1", $"Bitmask_Lit_B Flags.defaultValue expected '1' (A) got '{bDef}'");
    }

    private static void BuildTask2(TestContext ctx)
    {
        const string emy = "/Game/Tests/UE5_0/EMyFlags_A";
        const string a2 = "/Game/Tests/UE5_0/Bitmask_Or_A";

        {
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            // Return type is int (bitmask value), not the enum type.
            var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Combine",
                inputs: new object[0],
                outputs: new[] { new { name = "ReturnValue", type = "int" } });
            // MakeBitmask(A=1) OR MakeBitmask(B=2): UK2Node_BitmaskLiteral with
            // BitflagsEnum from payload 'enum'. Bitmask pin default is numeric.
            var mkA = BlueprintTestHelper.AddNode(ctx, a2, "Combine", "MakeBitmask", 300, -100, enumName: emy);
            var mkB = BlueprintTestHelper.AddNode(ctx, a2, "Combine", "MakeBitmask", 300, 100, enumName: emy);
            BlueprintTestHelper.SetPinDefault(ctx, a2, mkA.nodeId, "Bitmask", "1");
            BlueprintTestHelper.SetPinDefault(ctx, a2, mkB.nodeId, "Bitmask", "2");
            var or2 = BlueprintTestHelper.AddNode(ctx, a2, "Combine", "Or_IntInt", 600, 0, "KismetMathLibrary");
            BlueprintTestHelper.Connect(ctx, a2, mkA.nodeId, "Return Value", or2.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a2, mkB.nodeId, "Return Value", or2.nodeId, "B");
            BlueprintTestHelper.Connect(ctx, a2, or2.nodeId, "Return Value", f2.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
        }

        // Mis-touch comparison (IR snapshot) - after the A-side build.
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);   // Bitmask_Or_A

        // DSL: Combine returns int (bitmask value). The body A | B is a combination
        // expression — allowed in function bodies (Set/return), not in variable defaults.
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // Mis-touch comparison (IR snapshot) for the DSL-side blueprint.
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Bitmask_Or_B");

        // B-side body read-back: `EMyFlags.A | EMyFlags.B` must lower to a real graph
        // (MakeBitmask | MakeBitmask → Or_IntInt → return), not an empty function header.
        var orB = BlueprintTestHelper.ListGraph(ctx, "/Game/Tests/UE5_0/Bitmask_Or_B", "Combine");
        ctx.Logger.Info(null, "[Bitmask] Bitmask_Or_B Combine node count: {0}", orB.nodes.Length);
        ctx.AssertTrue(orB.nodes.Length >= 4,
            $"Bitmask_Or_B Combine body should carry computation nodes (entry+result+Or+2×MakeBitmask); got {orB.nodes.Length}");
    }

    private static void BuildTask3(TestContext ctx)
    {
        const string emy = "/Game/Tests/UE5_0/EMyFlags_A";
        const string a3 = "/Game/Tests/UE5_0/Bitmask_Test_A";

        {
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            // Bitmask variable: int type + MD_Bitmask + MD_BitmaskEnum. Default is
            // single flag A (value 1).
            BlueprintTestHelper.AddVariable(ctx, a3, "Flags", "int", "A",
                bitmask: true, bitmaskEnum: emy);
            var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "HasA",
                inputs: new object[0],
                outputs: new[] { new { name = "ReturnValue", type = "bool" } });
            var getFlags = BlueprintTestHelper.AddNode(ctx, a3, "HasA", "Flags", 100, 0, kind: "getter");
            // (Flags & A) != 0 : MakeBitmask(A=1) → And_IntInt → NotEqual_IntInt(0).
            var mkA3 = BlueprintTestHelper.AddNode(ctx, a3, "HasA", "MakeBitmask", 300, 100, enumName: emy);
            BlueprintTestHelper.SetPinDefault(ctx, a3, mkA3.nodeId, "Bitmask", "1");
            var and3 = BlueprintTestHelper.AddNode(ctx, a3, "HasA", "And_IntInt", 500, 0, "KismetMathLibrary");
            BlueprintTestHelper.Connect(ctx, a3, getFlags.nodeId, "Flags", and3.nodeId, "A");
            BlueprintTestHelper.Connect(ctx, a3, mkA3.nodeId, "Return Value", and3.nodeId, "B");
            var eq3 = BlueprintTestHelper.AddNode(ctx, a3, "HasA", "NotEqual_IntInt", 700, 0, "KismetMathLibrary");
            BlueprintTestHelper.Connect(ctx, a3, and3.nodeId, "Return Value", eq3.nodeId, "A");
            BlueprintTestHelper.SetPinDefault(ctx, a3, eq3.nodeId, "B", "0");
            BlueprintTestHelper.Connect(ctx, a3, eq3.nodeId, "Return Value", f3.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
        }

        // Mis-touch comparison (IR snapshot) - after the A-side build.
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);   // Bitmask_Test_A

        BlueprintTestHelper.ApplyDsl(ctx, "");

        // Mis-touch comparison (IR snapshot) for the DSL-side blueprint.
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Bitmask_Test_B");

        // B-side body read-back: `(Flags & EMyFlags.A) != 0` must lower to a real graph
        // (getter Flags | MakeBitmask → And_IntInt → NotEqual_IntInt → return).
        var testB = BlueprintTestHelper.ListGraph(ctx, "/Game/Tests/UE5_0/Bitmask_Test_B", "HasA");
        ctx.Logger.Info(null, "[Bitmask] Bitmask_Test_B HasA node count: {0}", testB.nodes.Length);
        ctx.AssertTrue(testB.nodes.Length >= 5,
            $"Bitmask_Test_B HasA body should carry computation nodes (getter+MakeBitmask+And+NotEqual+entry+result); got {testB.nodes.Length}");
    }
}
