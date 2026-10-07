// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 23 · Blueprint Macro (UE 5.0)  — from Tasks/UE_5.0/Blueprint-Macro.md
// Probe (Spec Point 6): DSL [Macro] supported in translator (BlueprintTranslator.cs
// line 579/589/876, macro graph type="macro"). Bridge create_macro_graph is implemented
// (HandleCreateMacroGraph), helper CreateMacro is exposed — AssetA uses real macro graph
// for macro body construction, call sites use kind:"macro" to add macro instances. DSL path [Macro] is isomorphic.
// Note: This file was transcribed from Task docs, visually confirmed by user; VerifyIrSnapshot (mis-touch comparison)
// has been inserted — each A asset (after CompileAndSave) and B asset (after ApplyDsl) verified once, 6 places total.
// Coverage (2026-10-04 completed): bridge get_asset_ir now exports macros[] (macro signature + macro body graph)
// and functions[].locals (function locals); snapshot normalization covers macros[] simultaneously,
// so internals of macro bodies and local variables are both within comparison scope.
// =========================================================================

public sealed class Blueprint_Macro : ITestCase
{
    public string Name => "Blueprint Macro (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 23;

    public async Task Run(TestContext ctx)
    {
        // ── Task 1: Parameterless macro ──
        // MCP step: Macro_Basic_A (Extends Actor) + Macro Flash(PrintString "Flash")
        //   + Event BeginPlay calling Flash
        const string m1 = "/Game/Tests/UE5_0/Macro_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, m1);
        // Macro graph Flash: create_macro_graph creates real macro graph; entry/exit tunnel located by helper.
        var fm1 = BlueprintTestHelper.CreateMacro(ctx, m1, "Flash");
        var pf1 = BlueprintTestHelper.AddNode(ctx, m1, "Flash", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, m1, pf1.nodeId, "InString", "Flash");
        BlueprintTestHelper.Connect(ctx, m1, fm1.entryId, "execute", pf1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, m1, pf1.nodeId, "then", fm1.resultId, "then");
        // Event BeginPlay → call Flash (kind:"macro" expresses macro instance intent).
        var bp1 = BlueprintTestHelper.AddNode(ctx, m1, "EventGraph", "ReceiveBeginPlay", 0, 0, kind: "event");
        var call1 = BlueprintTestHelper.AddNode(ctx, m1, "EventGraph", "Flash", 300, 0, kind: "macro");
        BlueprintTestHelper.Connect(ctx, m1, bp1.nodeId, "then", call1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, m1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, m1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Macro_Basic_B");

        // ── Task 2: Macro with input/output ──
        // MCP step: macro Add(a,b)->r body a+b + function Use calling Add(1,2)
        const string m2 = "/Game/Tests/UE5_0/Macro_IO_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, m2);
        // Macro Add: create_macro_graph creates real macro graph (with input a/b, output ReturnValue).
        // exec:false — macro body has only pure nodes (integer + integer), no execution flow, macro should not
        // have execution pins (2026-10-04 user visual: policy preference no execution pins; and pure node macro
        // execution passthrough is empty wiring).
        var fm2 = BlueprintTestHelper.CreateMacro(ctx, m2, "Add", exec: false,
            inputs: new[] { new { name = "a", type = "int" }, new { name = "b", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "int" } });
        var add2 = BlueprintTestHelper.AddNode(ctx, m2, "Add", "Add_IntInt", 300, 0, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, m2, fm2.entryId, "a", add2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, m2, fm2.entryId, "b", add2.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, m2, add2.nodeId, "ReturnValue", fm2.resultId, "ReturnValue");
        // Function Use calling Add(1,2) (kind:"macro" expresses macro instance intent) — macro has no execution
        // pins; call site does not drag execution wires: execution chain is entry → PrintString → result,
        // macro only provides data.
        var fu2 = BlueprintTestHelper.CreateFunction(ctx, m2, "Use");
        var call2 = BlueprintTestHelper.AddNode(ctx, m2, "Use", "Add", 300, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, m2, call2.nodeId, "a", "1");
        BlueprintTestHelper.SetPinDefault(ctx, m2, call2.nodeId, "b", "2");
        var pu2 = BlueprintTestHelper.AddNode(ctx, m2, "Use", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, m2, call2.nodeId, "ReturnValue", pu2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, m2, fu2.entryId, "then", pu2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, m2, pu2.nodeId, "then", fu2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, m2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, m2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Macro_IO_B");

        // ── Task 3: Macro with loop ──
        // MCP step: Macro Count(n) body ForLoop(0..n-1) wired to PrintString(Index)
        //   + function Use calling Count(3)
        const string m3 = "/Game/Tests/UE5_0/Macro_Loop_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, m3);
        // Macro Count: create_macro_graph creates real macro graph (with input n).
        var fm3 = BlueprintTestHelper.CreateMacro(ctx, m3, "Count",
            inputs: new[] { new { name = "n", type = "int" } });
        var fl3 = BlueprintTestHelper.AddNode(ctx, m3, "Count", "ForLoop", 300, 0, kind: "macro");
        BlueprintTestHelper.Connect(ctx, m3, fm3.entryId, "n", fl3.nodeId, "LastIndex");
        BlueprintTestHelper.Connect(ctx, m3, fm3.entryId, "execute", fl3.nodeId, "execute");
        var p3 = BlueprintTestHelper.AddNode(ctx, m3, "Count", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, m3, fl3.nodeId, "Index", p3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, m3, fl3.nodeId, "LoopBody", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, m3, fl3.nodeId, "Completed", fm3.resultId, "then");
        // Function Use calling Count(3) (kind:"macro" expresses macro instance intent).
        var fu3 = BlueprintTestHelper.CreateFunction(ctx, m3, "Use");
        var call3 = BlueprintTestHelper.AddNode(ctx, m3, "Use", "Count", 300, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, m3, call3.nodeId, "n", "3");
        BlueprintTestHelper.Connect(ctx, m3, fu3.entryId, "then", call3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, m3, call3.nodeId, "then", fu3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, m3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, m3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Macro_Loop_B");

        await Task.CompletedTask;
    }
}
