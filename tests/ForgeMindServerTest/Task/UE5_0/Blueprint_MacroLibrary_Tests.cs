// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 24 · Blueprint Macro Library (UE 5.0)  — from Tasks/UE_5.0/Blueprint-MacroLib.md
// Probe (Spec Point 6): DSL [MacroLibrary] attribute is implemented in the translator (MacroLibraryTranslator,
// reusing the full BlueprintTranslator pipeline); MCP helper CreateBlueprint(parentClass=
// "BlueprintMacroLibrary") + CreateMacro already pass through to the macro library. Library macro bodies
// use real macro graph construction (create_macro_graph). DSL path [MacroLibrary]+[Macro] is isomorphic.
// Note: This file was transcribed from Task docs, visually confirmed by user; VerifyIrSnapshot (mis-touch
// comparison) has been inserted — each A asset (after CompileAndSave) and B asset (after ApplyDsl) verified once, 6 places total.
// =========================================================================

public sealed class Blueprint_MacroLibrary : ITestCase
{
    public string Name => "Blueprint Macro Library (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 24;

    public async Task Run(TestContext ctx)
    {
        // ── Task 1: Macro library static macro ──
        // MCP step: Create Blueprint Macro Library MacroLib_Log_A + macro Log(PrintString "x")
        const string ml1 = "/Game/Tests/UE5_0/MacroLib_Log_A";
        // parentClass "BlueprintMacroLibrary" → BPTYPE_MacroLibrary (bridge already allowed); the macro body
        // needs create_macro_graph (cannot use create_function_graph — the latter creates a GT_Function
        // graph in a macro library, which triggers GT_Function assertion crash when opening the blueprint).
        BlueprintTestHelper.CreateBlueprint(ctx, ml1, "BlueprintMacroLibrary");
        var fm1 = BlueprintTestHelper.CreateMacro(ctx, ml1, "Log");
        var p1 = BlueprintTestHelper.AddNode(ctx, ml1, "Log", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, ml1, p1.nodeId, "InString", "x");
        BlueprintTestHelper.Connect(ctx, ml1, fm1.entryId, "execute", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, ml1, p1.nodeId, "then", fm1.resultId, "then");
        BlueprintTestHelper.CompileAndSave(ctx, ml1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, ml1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/MacroLib_Log_B");

        // ── Task 2: Macro input parameter ──
        // MCP step: Create macro library MacroLib_In_A + macro Echo(int v) body PrintString(v)
        const string ml2 = "/Game/Tests/UE5_0/MacroLib_In_A";
        BlueprintTestHelper.CreateBlueprint(ctx, ml2, "BlueprintMacroLibrary");
        var fm2 = BlueprintTestHelper.CreateMacro(ctx, ml2, "Echo",
            inputs: new[] { new { name = "v", type = "int" } });
        var p2 = BlueprintTestHelper.AddNode(ctx, ml2, "Echo", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, ml2, fm2.entryId, "v", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, ml2, fm2.entryId, "execute", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, ml2, p2.nodeId, "then", fm2.resultId, "then");
        BlueprintTestHelper.CompileAndSave(ctx, ml2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, ml2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/MacroLib_In_B");

        // ── Task 3: Macro output return value ──
        // MCP step: Create macro library MacroLib_Out_A + macro Double(v)->r body r = v*2. The macro body
        // has data flow only (pure nodes); exec:false creates no execution pins (policy preference).
        const string ml3 = "/Game/Tests/UE5_0/MacroLib_Out_A";
        BlueprintTestHelper.CreateBlueprint(ctx, ml3, "BlueprintMacroLibrary");
        var fm3 = BlueprintTestHelper.CreateMacro(ctx, ml3, "Double", exec: false,
            inputs: new[] { new { name = "v", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "int" } });
        var mul3 = BlueprintTestHelper.AddNode(ctx, ml3, "Double", "Multiply_IntInt", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, ml3, mul3.nodeId, "B", "2");
        BlueprintTestHelper.Connect(ctx, ml3, fm3.entryId, "v", mul3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, ml3, mul3.nodeId, "ReturnValue", fm3.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, ml3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, ml3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/MacroLib_Out_B");

        await Task.CompletedTask;
    }
}
