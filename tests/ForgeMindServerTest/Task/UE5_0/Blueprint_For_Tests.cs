// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 2 · Blueprint For Loop (UE 5.0)  — from Tasks/UE_5.0/Blueprint-For.md
// =========================================================================

public sealed class Blueprint_For : ITestCase
{
    public string Name => "Blueprint For Loop (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 2;

    private static readonly string[] Assets =
    [
        "/Game/Tests/UE5_0/For_Basic_A",    "/Game/Tests/UE5_0/For_Basic_B",
        "/Game/Tests/UE5_0/For_Break_A",   "/Game/Tests/UE5_0/For_Break_B",
        "/Game/Tests/UE5_0/ForEach_Basic_A", "/Game/Tests/UE5_0/ForEach_Basic_B",
        "/Game/Tests/UE5_0/ForEach_Break_A",  "/Game/Tests/UE5_0/ForEach_Break_B",
    ];

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: For Loop Basics ──
        // In UE 5.0, ForLoop/WhileLoop/ForEachLoop are StandardMacros (not
        // K2Node classes); instantiated via kind:"macro". The macro instance's
        // exec-in pin is "execute", and outputs are LoopBody/Index/Completed.
        const string a1 = "/Game/Tests/UE5_0/For_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Run");
        var fl = BlueprintTestHelper.AddNode(ctx, a1, "Run", "ForLoop", 300, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, a1, fl.nodeId, "LastIndex", "9");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", fl.nodeId, "execute");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, fl.nodeId, "LoopBody", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, fl.nodeId, "Index", p1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, fl.nodeId, "Completed", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/For_Basic_B");

        // ── Sub-task 2: For Loop with Break ──
        // DSL condition is `if (i == 5) break;` — restored via Equal_IntInt comparing the loop index.
        const string a2 = "/Game/Tests/UE5_0/For_Break_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Run");
        var fb = BlueprintTestHelper.AddNode(ctx, a2, "Run", "ForLoopWithBreak", 300, 0, kind: "macro");
        BlueprintTestHelper.SetPinDefault(ctx, a2, fb.nodeId, "LastIndex", "9");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", fb.nodeId, "execute");
        var eq2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "EqualEqual_IntInt", 500, -100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a2, fb.nodeId, "Index", eq2.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a2, eq2.nodeId, "B", "5");
        var b2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "Branch", 600, 0);
        BlueprintTestHelper.Connect(ctx, a2, fb.nodeId, "LoopBody", b2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, eq2.nodeId, "ReturnValue", b2.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a2, b2.nodeId, "then", fb.nodeId, "Break");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, b2.nodeId, "else", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, fb.nodeId, "Index", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, fb.nodeId, "Completed", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/For_Break_B");

        // ── Sub-task 3: For Each Loop (array iteration) ──
        // ForEachLoop family pin real names differ from ForLoop: exec-in is "Exec" (not
        // "execute"), element/index output pins have spaces ("Array Element"/"Array Index").
        const string a3 = "/Game/Tests/UE5_0/ForEach_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Items", "int[]");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Run");
        var get3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "Items", 100, 0, kind: "getter");
        var fe = BlueprintTestHelper.AddNode(ctx, a3, "Run", "ForEachLoop", 300, 0, kind: "macro");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", fe.nodeId, "Exec");
        BlueprintTestHelper.Connect(ctx, a3, get3.nodeId, "Items", fe.nodeId, "Array");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, fe.nodeId, "LoopBody", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, fe.nodeId, "Array Element", p3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, fe.nodeId, "Completed", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/ForEach_Basic_B");

        // ── Sub-task 4: For Each Loop with Break ──
        // DSL condition is `if (item == 5) break;` — restored via Equal_IntInt comparing the element.
        const string a4 = "/Game/Tests/UE5_0/ForEach_Break_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a4);
        BlueprintTestHelper.AddVariable(ctx, a4, "Items", "int[]");
        var f4 = BlueprintTestHelper.CreateFunction(ctx, a4, "Run");
        var get4 = BlueprintTestHelper.AddNode(ctx, a4, "Run", "Items", 100, 0, kind: "getter");
        var feb = BlueprintTestHelper.AddNode(ctx, a4, "Run", "ForEachLoopWithBreak", 300, 0, kind: "macro");
        BlueprintTestHelper.Connect(ctx, a4, f4.entryId, "then", feb.nodeId, "Exec");
        BlueprintTestHelper.Connect(ctx, a4, get4.nodeId, "Items", feb.nodeId, "Array");
        var eq4 = BlueprintTestHelper.AddNode(ctx, a4, "Run", "EqualEqual_IntInt", 500, -100, "KismetMathLibrary");
        BlueprintTestHelper.Connect(ctx, a4, feb.nodeId, "Array Element", eq4.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a4, eq4.nodeId, "B", "5");
        var b4 = BlueprintTestHelper.AddNode(ctx, a4, "Run", "Branch", 600, 0);
        BlueprintTestHelper.Connect(ctx, a4, feb.nodeId, "LoopBody", b4.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a4, eq4.nodeId, "ReturnValue", b4.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a4, b4.nodeId, "then", feb.nodeId, "Break");
        var p4 = BlueprintTestHelper.AddNode(ctx, a4, "Run", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a4, b4.nodeId, "else", p4.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a4, feb.nodeId, "Array Element", p4.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a4, feb.nodeId, "Completed", f4.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a4);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a4);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/ForEach_Break_B");

        await Task.CompletedTask;
    }
}
