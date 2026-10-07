// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 11 - Blueprint Local Variable (UE 5.0) - from Tasks/UE_5.0/blueprint-local-variable.md
// =========================================================================

public sealed class Blueprint_LocalVariable : ITestCase
{
    public string Name => "Blueprint Local Variable (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 11;

    public async Task Run(TestContext ctx)
    {
        // -- Support status probe conclusion (collaboration spec point 6) --
        // Supported: function graph locals (create_function_graph's locals -> FunctionEntry,
        // While_Count_A etc. cases confirmed), member variable getter/setter, Add_IntInt,
        // Array_Add/Array_Length node family, int->string auto-conversion node.
        // Gaps (written directly as "assumed supported", failure forces implementation):
        // 1. MCP event graph locals -- implemented (2026-09-30): bridge add_blueprint_node
        //    kind:"temporaryVariable" = UK2Node_TemporaryVariable read node
        //    (params[0].type passes variable type declaration); kind:"assignment" =
        //    UK2Node_AssignmentStatement write node (engine real data pins Variable/Value);
        // 2. DSL in-event-graph locals ([Event] method body's int count = 0; -- currently
        //    FM1101 puts function local vars into function graph);
        // 3. DSL int[] buf; no-init local array declaration + buf.Add(...) member call.

        // -- Subtask 1: Local Variable in Function --
        // MCP steps: LocalVar_Func_A (Extends Actor) + function F (local t : int init 0)
        //         -> t = t + 1 -> PrintString(t)
        const string a1 = "/Game/Tests/UE5_0/LocalVar_Func_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "F",
            locals: new[] { new { name = "t", type = "int", defaultValue = "0" } });

        var getT = BlueprintTestHelper.AddNode(ctx, a1, "F", "t", 100, -50, kind: "getter");
        var addT = BlueprintTestHelper.AddNode(ctx, a1, "F", "Add_IntInt", 200, -50, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, addT.nodeId, "B", "1");
        BlueprintTestHelper.Connect(ctx, a1, getT.nodeId, "t", addT.nodeId, "A");
        var setT = BlueprintTestHelper.AddNode(ctx, a1, "F", "t", 400, 0, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a1, addT.nodeId, "ReturnValue", setT.nodeId, "t");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", setT.nodeId, "execute");
        var getT2 = BlueprintTestHelper.AddNode(ctx, a1, "F", "t", 500, -50, kind: "getter");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "F", "PrintString", 700, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, setT.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, getT2.nodeId, "t", p1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc; function graph locals is supported path)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/LocalVar_Func_B");

        // -- Subtask 2: Local Variable in Event Graph --
        // MCP steps: LocalVar_Event_A (Extends Actor) + Event BeginPlay:
        //         local count : int -> count = 0 -> count = count + 1 -> PrintString(count)
        // Event graph local spec (implemented): temporaryVariable read node is both declaration
        // and reference (reference implies existence; engine registers as forced-shared local
        // terminal value in event graph); assignment write node carries the assignment.
        // Engine real names: TemporaryVariable output pin 'Variable'
        // (AllocateDefaultPins), AssignmentStatement data pins 'Variable' (target) /
        // 'Value' (value) -- target pin must be wired (FKCHandler_AssignmentStatement::
        // RegisterNets requires it); after connection, Value pin conforms to Variable pin type
        // (NotifyPinConnectionListChanged); literal defaults must be set after.
        const string a2 = "/Game/Tests/UE5_0/LocalVar_Event_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var ev2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "ReceiveBeginPlay", 0, 0);

        var declCount = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "count", 300, 200,
            kind: "temporaryVariable", eventParams: new[] { new { name = "count", type = "int" } });
        var setCount = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "count", 300, 0, kind: "assignment");
        BlueprintTestHelper.Connect(ctx, a2, declCount.nodeId, "Variable", setCount.nodeId, "Variable");
        BlueprintTestHelper.SetPinDefault(ctx, a2, setCount.nodeId, "Value", "0");

        var addCount = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "Add_IntInt", 500, 200, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, addCount.nodeId, "B", "1");
        BlueprintTestHelper.Connect(ctx, a2, declCount.nodeId, "Variable", addCount.nodeId, "A");
        var setCount2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "count", 550, 0, kind: "assignment");
        BlueprintTestHelper.Connect(ctx, a2, declCount.nodeId, "Variable", setCount2.nodeId, "Variable");
        BlueprintTestHelper.Connect(ctx, a2, addCount.nodeId, "ReturnValue", setCount2.nodeId, "Value");

        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 750, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, ev2.nodeId, "then", setCount.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, setCount.nodeId, "then", setCount2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, setCount2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, declCount.nodeId, "Variable", p2.nodeId, "InString");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc; in-event-graph locals is DSL pending spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/LocalVar_Event_B");

        // -- Subtask 3: Local Array Variable --
        // MCP steps: LocalVar_Arr_A (Extends Actor) + function B (local buf : int[])
        //         -> Add(1) then PrintString(buf.Length)
        // locals without defaultValue means no init declaration.
        const string a3 = "/Game/Tests/UE5_0/LocalVar_Arr_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "B",
            locals: new[] { new { name = "buf", type = "int[]" } });

        var getBuf = BlueprintTestHelper.AddNode(ctx, a3, "B", "buf", 100, 0, kind: "getter");
        var addBuf = BlueprintTestHelper.AddNode(ctx, a3, "B", "Array_Add", 300, -100, "KismetArrayLibrary");
        // Connect TargetArray first then set NewItem default: wildcard pins reject literal
        // defaults before type resolution (engine's IsDefaultValueValidForPinType has no wildcard
        // branch; wildcard pins in editor also have no value input box) -- same as editor's required order.
        BlueprintTestHelper.Connect(ctx, a3, getBuf.nodeId, "buf", addBuf.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a3, addBuf.nodeId, "NewItem", "1");
        var lenBuf = BlueprintTestHelper.AddNode(ctx, a3, "B", "Array_Length", 300, 100, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getBuf.nodeId, "buf", lenBuf.nodeId, "TargetArray");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "B", "PrintString", 500, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", addBuf.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, addBuf.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, lenBuf.nodeId, "ReturnValue", p3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, p3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc; no-init local array declaration and buf.Add(...) are DSL pending spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/LocalVar_Arr_B");

        await Task.CompletedTask;
    }
}
