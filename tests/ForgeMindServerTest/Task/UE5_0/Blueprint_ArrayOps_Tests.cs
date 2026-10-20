// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 48 - Blueprint Array Operations (UE 5.0) - from Tasks/UE_5.0/blueprint-array-ops.md
//
// Tasks (each: one MCP-stepped asset + one DSL asset, "one task two assets" Q4):
//   Task 1 - Add / Get / Num : Run() => Buf.Add(10); int x = Buf[0]; int n = Buf.Num(); PrintString(n)
//   Task 2 - Find / Contains: bool Has(TArray<int> a, int v) => a.Contains(v)
//   Task 3 - Remove          : Run(TArray<int> a) => a.Remove(10); a.RemoveAt(0)
//
// Array support is already in place (per user; corroborated by Blueprint_Variable_Tests
// probe: array type "int[]", variable getter/setter, Array_Add/Array_Length node family).
//
// Node family - KismetArrayLibrary (UK2Node_CallArrayFunction), verified against
// Engine/Source/Runtime/Engine/Classes/Kismet/KismetArrayLibrary.h (5.0.3):
//   Array_Add(TargetArray, NewItem)              -> int   (mutator, exec)
//   Array_Get(TargetArray, Index, Item&)         -> void  (pure; out pin "Item")
//   Array_Length(TArray)                         -> int   (pure; "Return Value")
//   Array_Find(TargetArray, ItemToFind)          -> int   (pure; "Return Value")
//   Array_Contains(TargetArray, ItemToFind)      -> bool  (pure; "Return Value")
//   Array_Remove(TargetArray, IndexToRemove)     -> void  (mutator, exec; BY INDEX)
//   Array_RemoveItem(TargetArray, Item)          -> bool  (mutator, exec; BY VALUE)
//
// IMPORTANT mapping: the task doc's DSL uses C#-style a.Remove(10) (by VALUE) and
// a.RemoveAt(0) (by INDEX). The engine has no "Array_RemoveIndex" node - it is the
// reverse of the doc's loose wording:
//   doc a.Remove(10)      -> Array_RemoveItem (TargetArray, Item = 10)
//   doc a.RemoveAt(0)     -> Array_Remove    (TargetArray, IndexToRemove = 0)
//
// Wildcard timing: TargetArray (and the by-ref Item pin on RemoveItem/Add) are wildcards
// until the container type is known. The getter is connected to TargetArray FIRST, then
// pin defaults are set (same conformance order as Blueprint_Variable_Tests / ContainerMapSet:
// engine ConformContainerPins fires via the bridge's ReconstructNode chain). Setting a
// default before the TargetArray connection is rejected ("Unsupported type wildcard").
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison).
// =========================================================================

public sealed class Blueprint_ArrayOps : ITestCase
{
    public string Name => "Blueprint Array Operations (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 48;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    // ── Task 1: Add / Get / Num ──

    private static void BuildTask1(TestContext ctx)
    {
        // AssetA (MCP): Arr_Basic_A - Buf : int[]; Run() adds 10, reads index 0, prints length.
        const string a1 = "/Game/Tests/UE5_0/Arr_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "Buf", "int[]");

        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Run");
        // Shared Buf getter (fans out to Add/Get/Length TargetArray pins).
        var getBuf = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Buf", 100, 0, kind: "getter");

        // Array_Add (mutator, exec): TargetArray <- Buf, NewItem = 10.
        var add1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Array_Add", 300, -160, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a1, getBuf.nodeId, "Buf", add1.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a1, add1.nodeId, "NewItem", "10");

        // Array_Get (pure): TargetArray <- Buf, Index = 0. Out "Item" unused (mirrors DSL x = Buf[0]).
        var get1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Array_Get", 300, 0, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a1, getBuf.nodeId, "Buf", get1.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a1, get1.nodeId, "Index", "0");

        // Array_Length / Num (pure): TargetArray <- Buf; Return Value -> PrintString(InString).
        var len1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Array_Length", 300, 160, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a1, getBuf.nodeId, "Buf", len1.nodeId, "TargetArray");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, 160, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, len1.nodeId, "Return Value", print1.nodeId, "InString");

        // Exec chain runs the mutator then prints; pure Get/Length are off the exec chain (data only).
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", add1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, add1.nodeId, "then", print1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, print1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // AssetB (DSL): copied from task doc (Mode = Create added for asset creation).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Arr_Basic_B");
    }

    // ── Task 2: Find / Contains ──

    private static void BuildTask2(TestContext ctx)
    {
        // AssetA (MCP): Arr_Find_A - bool Has(TArray<int> a, int v) => a.Contains(v).
        const string a2 = "/Game/Tests/UE5_0/Arr_Find_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Has",
            inputs: new[] { new { name = "a", type = "int[]" }, new { name = "v", type = "int" } },
            outputs: new[] { new { name = "ReturnValue", type = "bool" } });

        // Array_Contains (pure): TargetArray <- a, ItemToFind <- v; Return Value -> function return.
        var contains2 = BlueprintTestHelper.AddNode(ctx, a2, "Has", "Array_Contains", 300, 0, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "a", contains2.nodeId, "TargetArray");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "v", contains2.nodeId, "ItemToFind");
        BlueprintTestHelper.Connect(ctx, a2, contains2.nodeId, "Return Value", f2.resultId, "ReturnValue");
        // Pure-only graph: entry -> result directly (no exec nodes besides terminators).
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // AssetB (DSL): copied from task doc (Mode = Create added for asset creation).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Arr_Find_B");
    }

    // ── Task 3: Remove ──

    private static void BuildTask3(TestContext ctx)
    {
        // AssetA (MCP): Arr_Remove_A - Run(TArray<int> a) => a.Remove(10); a.RemoveAt(0).
        const string a3 = "/Game/Tests/UE5_0/Arr_Remove_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Run",
            inputs: new[] { new { name = "a", type = "int[]" } });

        // a.Remove(10) (BY VALUE) -> Array_RemoveItem: TargetArray <- a, Item = 10 (mutator, exec).
        var rmItem3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "Array_RemoveItem", 300, -100, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "a", rmItem3.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a3, rmItem3.nodeId, "Item", "10");

        // a.RemoveAt(0) (BY INDEX) -> Array_Remove: TargetArray <- a, IndexToRemove = 0 (mutator, exec).
        var rmIdx3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "Array_Remove", 300, 100, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "a", rmIdx3.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a3, rmIdx3.nodeId, "IndexToRemove", "0");

        // Exec chain: entry -> Array_RemoveItem -> Array_Remove -> result.
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", rmItem3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, rmItem3.nodeId, "then", rmIdx3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, rmIdx3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // AssetB (DSL): copied from task doc (Mode = Create added for asset creation).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Arr_Remove_B");
    }
}
