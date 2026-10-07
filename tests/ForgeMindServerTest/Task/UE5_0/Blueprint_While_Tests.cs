// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 6 · Blueprint While Loop (UE 5.0)  — from Tasks/UE_5.0/Blueprint-While.md
// =========================================================================

public sealed class Blueprint_While : ITestCase
{
    public string Name => "Blueprint While Loop (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 6;

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: Counter while loop ──
        // DSL is `int i = 0; while (i < 10) { PrintString(i); i = i + 1; }` —
        // counter uses function local variables (create_function_graph locals) + getter/setter,
        // condition is restored using Less_IntInt (KismetMathLibrary).
        const string a1 = "/Game/Tests/UE5_0/While_Count_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Run",
            locals: new[] { new { name = "i", type = "int", defaultValue = "0" } });
        var getI1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "i", 100, 0, kind: "getter");
        var lt1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Less_IntInt", 200, -100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, getI1.nodeId, "i", lt1.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a1, lt1.nodeId, "B", "10");
        var wl = BlueprintTestHelper.AddNode(ctx, a1, "Run", "WhileLoop", 300, 0, kind: "macro");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", wl.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, lt1.nodeId, "ReturnValue", wl.nodeId, "Condition");
        // Loop body: PrintString(i) + i = i + 1
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, -50, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, wl.nodeId, "LoopBody", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, getI1.nodeId, "i", p1.nodeId, "InString");
        var inc1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Add_IntInt", 600, 100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a1, getI1.nodeId, "i", inc1.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a1, inc1.nodeId, "B", "1");
        var setI1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "i", 800, 100, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", setI1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, inc1.nodeId, "ReturnValue", setI1.nodeId, "i");
        BlueprintTestHelper.Connect(ctx, a1, wl.nodeId, "Completed", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/While_Count_B");

        // ── Sub-task 2: while loop with break ──
        // DSL is `while (true) { if (i == 5) break; PrintString(i); i = i + 1; }`.
        // UE 5.0 StandardMacros has no WhileLoopWithBreak (20260930_2101 verified: bridge
        // reports No macro named 'WhileLoopWithBreak' found; the WhileLoop macro also has no
        // Break input pin). A breakable while is restored using the ForLoopWithBreak base
        // (editor's established form for "breakable continuous loop", isomorphic with translator
        // B-side): FirstIndex=0 / LastIndex=int.MaxValue, condition literal true needs no Branch
        // guard, LoopBody directly wires the loop body; Branch(i==5).then → "Break" pin exits —
        // consistent with the manual form of For_Break_A (LoopBody → Branch → then → Break).
        const string a2 = "/Game/Tests/UE5_0/While_Break_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Find",
            locals: new[]
            {
                new { name = "i", type = "int", defaultValue = "0" },
            });
        var getI2 = BlueprintTestHelper.AddNode(ctx, a2, "Find", "i", 100, 0, kind: "getter");
        var wl2 = BlueprintTestHelper.AddNode(ctx, a2, "Find", "ForLoopWithBreak", 300, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, a2, wl2.nodeId, "LastIndex", "2147483647");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", wl2.nodeId, "execute");
        // Loop body: if i==5 → Break, else PrintString(i) + i = i + 1
        var b2 = BlueprintTestHelper.AddNode(ctx, a2, "Find", "Branch", 600, 0);
        BlueprintTestHelper.Connect(ctx, a2, wl2.nodeId, "LoopBody", b2.nodeId, "execute");
        var eq2 = BlueprintTestHelper.AddNode(ctx, a2, "Find", "EqualEqual_IntInt", 500, -100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getI2.nodeId, "i", eq2.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a2, eq2.nodeId, "B", "5");
        BlueprintTestHelper.Connect(ctx, a2, eq2.nodeId, "ReturnValue", b2.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a2, b2.nodeId, "then", wl2.nodeId, "Break");
        // PrintString(i) + i = i + 1 on false
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Find", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, b2.nodeId, "else", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, getI2.nodeId, "i", p2.nodeId, "InString");
        var inc2 = BlueprintTestHelper.AddNode(ctx, a2, "Find", "Add_IntInt", 1100, 100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getI2.nodeId, "i", inc2.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a2, inc2.nodeId, "B", "1");
        var setI2 = BlueprintTestHelper.AddNode(ctx, a2, "Find", "i", 1300, 100, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", setI2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, inc2.nodeId, "ReturnValue", setI2.nodeId, "i");
        BlueprintTestHelper.Connect(ctx, a2, wl2.nodeId, "Completed", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/While_Break_B");

        // ── Sub-task 3: Collection traversal while loop ──
        // DSL is `while (i < Arr.Length) { PrintString(Arr[i]); i = i + 1; }`.
        // KismetArrayLibrary real names are Array_Length / Array_Get.
        const string a3 = "/Game/Tests/UE5_0/While_Coll_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Arr", "int[]");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Walk",
            locals: new[] { new { name = "i", type = "int", defaultValue = "0" } });
        var getI3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "i", 100, 0, kind: "getter");
        var getArr3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "Arr", 100, 100, kind: "getter");
        var wl3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "WhileLoop", 300, 0, kind: "macro");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", wl3.nodeId, "execute");
        // Condition: i < Arr.Length
        var len3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "Array_Length", 500, -100, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getArr3.nodeId, "Arr", len3.nodeId, "TargetArray");
        var lt3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "Less_IntInt", 600, -100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getI3.nodeId, "i", lt3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, len3.nodeId, "ReturnValue", lt3.nodeId, "B");
        BlueprintTestHelper.Connect(ctx, a3, lt3.nodeId, "ReturnValue", wl3.nodeId, "Condition");
        // Loop body: PrintString(Arr[i]) + i = i + 1
        var get3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "Array_Get", 600, 100, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getArr3.nodeId, "Arr", get3.nodeId, "TargetArray");
        BlueprintTestHelper.Connect(ctx, a3, getI3.nodeId, "i", get3.nodeId, "Index");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "PrintString", 900, 100, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, wl3.nodeId, "LoopBody", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, get3.nodeId, "ReturnValue", p3.nodeId, "InString");
        var inc3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "Add_IntInt", 900, 200, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getI3.nodeId, "i", inc3.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a3, inc3.nodeId, "B", "1");
        var setI3 = BlueprintTestHelper.AddNode(ctx, a3, "Walk", "i", 1100, 200, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a3, p3.nodeId, "then", setI3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, inc3.nodeId, "ReturnValue", setI3.nodeId, "i");
        BlueprintTestHelper.Connect(ctx, a3, wl3.nodeId, "Completed", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/While_Coll_B");

        await Task.CompletedTask;
    }
}
