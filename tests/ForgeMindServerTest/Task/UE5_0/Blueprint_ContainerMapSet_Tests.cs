// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 39 - Blueprint Container Map/Set (UE 5.0) - from Tasks/UE_5.0/Blueprint-ContainerMapSet.md
//
// Probe (collaboration spec point 6):
// - **Library class names in 5.0.3 are UBlueprintMapLibrary / UBlueprintSetLibrary**
//   (Kismet\BlueprintMapLibrary.h:14, Kismet\BlueprintSetLibrary.h:14) -- the
//   UKismetMapLibrary / UKismetSetLibrary spellings only appear from 5.4 on, so ownerClass
//   must be "BlueprintMapLibrary" / "BlueprintSetLibrary". Real functions (all CustomThunk,
//   TargetMap / TargetSet parameter):
//     Map_Add(TargetMap, Key, Value)            BlueprintMapLibrary.h:27-28 ("Add")
//     Map_Find(TargetMap, Key, out Value)       :48-49, **BlueprintPure** -- there is **no**
//       Map_FindValue in 5.0.3, only Map_Find
//     Map_Remove(TargetMap, Key)               :37-38   Map_Contains :58-59
//     Map_Length(TargetMap)                    :85-86
//     Set_Add(TargetSet, NewItem)              BlueprintSetLibrary.h:25-26
//     Set_Remove(TargetSet, Item)              :45-46
//     Set_Contains(TargetSet, ItemToFind)      :108-109 ("Contains Item")
//     Set_Length(TargetSet)                    :98-99
//   The inconsistent parameter names (Set_Add -> NewItem, Set_Remove -> Item) are real.
// - TMap / TSet blueprint variables are supported (the CustomThunk UFUNCTIONs above take
//   TMap<int32,int32>& / TSet<int32>&: BlueprintMapLibrary.h:28, BlueprintSetLibrary.h:26).
//   The bridge's shared type parser documents container forms `X[]` / `set<X>` / `map<K,V>`
//   (ForgeMindBridgeClient_BlueprintVars.cpp:75-78) -- that is what this test uses.
// - **Wildcard timing (fix, TestRuns/20261011_1403)**: Map_Add/Map_Find Key/Value and
//   Set_Add NewItem / Set_Contains ItemToFind are AutoCreateRefTerm wildcard-by-ref pins
//   (MapParam/MapKeyParam/MapValueParam, SetParam="TargetSet|ItemToFind") -- no default is
//   settable before the Target pin is connected (same family as Array_Add NewItem,
//   Blueprint_Enum_Tests subtask 3 precedent). The MCP side therefore connects the
//   container variable getter to TargetMap/TargetSet FIRST (engine ConformContainerPins,
//   K2Node_CallFunction.cpp:3011-3037, fires via the bridge's ReconstructNode chain),
//   THEN sets pin defaults. The original order (SetPinDefault on a still-wildcard Key)
//   is rejected: "Unsupported type wildcard (by ref) on pin Key".
// - Map_Find is BlueprintPure (no exec pins): exec chain runs entry -> Add -> Print and
//   the printed value comes from Find's out "Value" pin (int, = DSL `int v = M[1]`).
//   Set_Contains is also pure; its bool "Return Value" autocasts to PrintString's
//   InString via the schema's auto conversion map (Conv_BoolToString).
//   Map_Remove is BlueprintCallable (has exec pins) and stays on the exec chain.
// - **No UK2Node_MapGet / UK2Node_MapSet exist in 5.0.3** (grep over Editor found none):
//   map get/set is only reachable through the library functions, so the doc's `M[1]`
//   indexer has no MCP equivalent -- the pure Map_Find node is used instead.
// - DSL side: `M.Add(1, 10)` / `int v = M[1]` / `S.Contains(5)` copied verbatim; the
//   translator's container support is unverified for the indexer form specifically.
//
// Note: "diff comparison" (VerifyIrSnapshot) inserted on user instruction per
// collaboration spec point 9 -- every asset in this file gets one after its
// build chain completes (same shape as Blueprint_Enum_Tests / Blueprint_Switch_Tests).
// =========================================================================

public sealed class Blueprint_ContainerMapSet : ITestCase
{
    public string Name => "Blueprint Container Map/Set (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 39;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: TMap Add / Find --
        // MCP steps: Cont_Map_A (Extends Actor) + M : map<int,int>
        //   Run() -> Add(1,10) -> Find(1) -> PrintString
        const string a1 = "/Game/Tests/UE5_0/Cont_Map_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "M", "map<int,int>");
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Run");
        var getM1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "M", 100, 0, kind: "getter");
        var add1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Map_Add", 300, -100,
            "BlueprintMapLibrary");
        var find1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "Map_Find", 300, 100,
            "BlueprintMapLibrary");
        // Connect the map getter to both TargetMap pins BEFORE defaults: Key/Value are
        // wildcard-by-ref until the MapParam group propagates the type (see header note).
        BlueprintTestHelper.Connect(ctx, a1, getM1.nodeId, "M", add1.nodeId, "TargetMap");
        BlueprintTestHelper.Connect(ctx, a1, getM1.nodeId, "M", find1.nodeId, "TargetMap");
        BlueprintTestHelper.SetPinDefault(ctx, a1, add1.nodeId, "Key", "1");
        BlueprintTestHelper.SetPinDefault(ctx, a1, add1.nodeId, "Value", "10");
        BlueprintTestHelper.SetPinDefault(ctx, a1, find1.nodeId, "Key", "1");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, 100,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", add1.nodeId, "execute");
        // Map_Find is pure -- no exec pins; the print follows Add on the exec chain and
        // reads the found value from Find's out "Value" pin (not the bool "Return Value").
        BlueprintTestHelper.Connect(ctx, a1, add1.nodeId, "then", print1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, find1.nodeId, "Value", print1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, print1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Cont_Map_B");

        // -- Subtask 2: TMap Remove --
        // MCP steps: Cont_MapRm_A (Extends Actor) + M : map<int,int>
        //   Run() -> Add(1,10) -> Remove(1)
        const string a2 = "/Game/Tests/UE5_0/Cont_MapRm_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "M", "map<int,int>");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Run");
        var getM2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "M", 100, 0, kind: "getter");
        var add2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "Map_Add", 300, -100,
            "BlueprintMapLibrary");
        var rm2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "Map_Remove", 300, 100,
            "BlueprintMapLibrary");
        // Getter -> TargetMap first (wildcard Key/Value conformance), then defaults.
        BlueprintTestHelper.Connect(ctx, a2, getM2.nodeId, "M", add2.nodeId, "TargetMap");
        BlueprintTestHelper.Connect(ctx, a2, getM2.nodeId, "M", rm2.nodeId, "TargetMap");
        BlueprintTestHelper.SetPinDefault(ctx, a2, add2.nodeId, "Key", "1");
        BlueprintTestHelper.SetPinDefault(ctx, a2, add2.nodeId, "Value", "10");
        BlueprintTestHelper.SetPinDefault(ctx, a2, rm2.nodeId, "Key", "1");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", add2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, add2.nodeId, "then", rm2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, rm2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Cont_MapRm_B");

        // -- Subtask 3: TSet Add / Contains --
        // MCP steps: Cont_Set_A (Extends Actor) + S : set<int>
        //   Run() -> Add(5) -> Contains(5) -> PrintString
        const string a3 = "/Game/Tests/UE5_0/Cont_Set_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "S", "set<int>");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Run");
        var getS3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "S", 100, 0, kind: "getter");
        var add3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "Set_Add", 300, -100,
            "BlueprintSetLibrary");
        var has3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "Set_Contains", 300, 100,
            "BlueprintSetLibrary");
        // Getter -> TargetSet first (wildcard NewItem/ItemToFind conformance), then defaults.
        BlueprintTestHelper.Connect(ctx, a3, getS3.nodeId, "S", add3.nodeId, "TargetSet");
        BlueprintTestHelper.Connect(ctx, a3, getS3.nodeId, "S", has3.nodeId, "TargetSet");
        BlueprintTestHelper.SetPinDefault(ctx, a3, add3.nodeId, "NewItem", "5");
        BlueprintTestHelper.SetPinDefault(ctx, a3, has3.nodeId, "ItemToFind", "5");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "PrintString", 600, 100,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", add3.nodeId, "execute");
        // Set_Contains is pure -- no exec pins; bool "Return Value" autocasts to InString.
        BlueprintTestHelper.Connect(ctx, a3, add3.nodeId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, has3.nodeId, "Return Value", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, print3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Cont_Set_B");

        await Task.CompletedTask;
    }
}