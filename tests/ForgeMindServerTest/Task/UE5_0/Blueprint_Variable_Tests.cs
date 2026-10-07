// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 10 · Blueprint Variables (UE 5.0)  — from Tasks/UE_5.0/Blueprint-Variables.md
// =========================================================================

public sealed class Blueprint_Variable : ITestCase
{
    public string Name => "Blueprint Variables (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 10;

    public async Task Run(TestContext ctx)
    {
        // ── Support matrix probe conclusion (Collaboration Spec Point 6) ──
        // Already supported: add_blueprint_variable's instanceEditable field, array type int[],
        // function graph locals, variable getter/setter, Array_Add/Array_Length node family.
        // Gaps (written assuming already supported; failures drive implementation):
        // ① DSL [Variable(Editable)] feature parameter + field initializer default value;
        // ② DSL TArray<int> type syntax and member array calls Inventory.Add(...).

        // ── Sub-task 1: Editable instance variable ──
        // MCP step: Var_Edit_A (Extends Actor) + MoveSpeed : int (Instance
        //         Editable, default 600) → Function Read: Get MoveSpeed → PrintString
        const string a1 = "/Game/Tests/UE5_0/Var_Edit_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        // Default value field name is bridge 'default' (C# keyword, @ escape needed) — not IR
        // 'defaultValue'; instanceEditable is directly supported by add_blueprint_variable.
        BlueprintTestHelper.EditorCall(ctx, "add_blueprint_variable",
            new { path = a1, name = "MoveSpeed", type = "int", @default = "600", instanceEditable = true });
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Read");
        var getMs = BlueprintTestHelper.AddNode(ctx, a1, "Read", "MoveSpeed", 100, 0, kind: "getter");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Read", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, getMs.nodeId, "MoveSpeed", p1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task docs; [Variable(Editable)] + initializer are DSL specs to implement)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Var_Edit_B");

        // ── Sub-task 2: Array variable ──
        // MCP step: Var_Array_A (Extends Actor) + Inventory : int[]
        //         → Function Fill: Add 1 / 2 / 3 to Inventory
        const string a2 = "/Game/Tests/UE5_0/Var_Array_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "Inventory", "int[]");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Fill");
        var getInv = BlueprintTestHelper.AddNode(ctx, a2, "Fill", "Inventory", 100, 100, kind: "getter");
        var add1 = BlueprintTestHelper.AddNode(ctx, a2, "Fill", "Array_Add", 300, -100, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getInv.nodeId, "Inventory", add1.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a2, add1.nodeId, "NewItem", "1");
        var add2 = BlueprintTestHelper.AddNode(ctx, a2, "Fill", "Array_Add", 300, 0, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getInv.nodeId, "Inventory", add2.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a2, add2.nodeId, "NewItem", "2");
        var add3 = BlueprintTestHelper.AddNode(ctx, a2, "Fill", "Array_Add", 300, 100, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getInv.nodeId, "Inventory", add3.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a2, add3.nodeId, "NewItem", "3");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", add1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, add1.nodeId, "then", add2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, add2.nodeId, "then", add3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, add3.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task docs; TArray<int> syntax and Inventory.Add(...) are DSL specs to implement)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Var_Array_B");

        // ── Sub-task 3: Function-local variable ──
        // MCP step: Var_Local_A (Extends Actor) + Function Calc (local temp : int initial 0)
        //         → temp = temp + 1 → PrintString(temp)
        // Initial 0 is carried by create_function_graph's locals defaultValue
        // (same as While_Count_A; an explicit Set 0 node is redundant).
        const string a3 = "/Game/Tests/UE5_0/Var_Local_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Calc",
            locals: new[] { new { name = "temp", type = "int", defaultValue = "0" } });

        var getTemp = BlueprintTestHelper.AddNode(ctx, a3, "Calc", "temp", 100, -50, kind: "getter");
        var addTemp = BlueprintTestHelper.AddNode(ctx, a3, "Calc", "Add_IntInt", 200, -50, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, addTemp.nodeId, "B", "1");
        BlueprintTestHelper.Connect(ctx, a3, getTemp.nodeId, "temp", addTemp.nodeId, "A");
        var setTemp = BlueprintTestHelper.AddNode(ctx, a3, "Calc", "temp", 400, 0, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a3, addTemp.nodeId, "ReturnValue", setTemp.nodeId, "temp");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", setTemp.nodeId, "execute");
        var getTemp2 = BlueprintTestHelper.AddNode(ctx, a3, "Calc", "temp", 500, -50, kind: "getter");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "Calc", "PrintString", 700, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, setTemp.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, getTemp2.nodeId, "temp", p3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, p3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task docs; function-local variables are a supported path)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Var_Local_B");

        await Task.CompletedTask;
    }
}
