// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 44 - Blueprint Data Table (UE 5.0) - from Tasks/UE_5.0/blueprint-datatable.md
//
// Probe (collaboration spec point 6):
// - Row struct creation is SUPPORTED: create_blueprint_struct (same pattern as
//   Blueprint_Struct_Tests); structs have no compile concept, save via save_asset only.
// - DataTable asset creation: NO bridge support (no DataTableFactory include and no
//   create_data_table action in ForgeMindBridgeClient_AssetIR.cpp). Task 1 / task 3
//   steps written with a direct create_data_table editor call per the "already
//   supported" assumption; the unknown-action rejection IS the pinned gap.
// - "Get Data Table Row" node: expressed through engine-real
//   UDataTableFunctionLibrary::GetDataTableRowFromName (ownerClass function-call path),
//   Table pin default = DataTable asset path, RowName pin default per doc. The bridge
//   aliases the documented name "GetDataTableRow" onto that function. There is NO
//   index-based row read in the engine (rows are keyed by FName; GetDataTableRowByIndex
//   has zero hits in the engine source), so task 2 is "list every row name" instead:
//   UDataTableFunctionLibrary::GetDataTableRowNames + the ForEachLoop macro. Row
//   breaking via native BreakStruct (input pin name = struct asset name, member output
//   pins, same as Blueprint_Struct_Tests).
// - DSL side: a DataTable is an OBJECT - it lives in a variable of DataTable type
//   (UDataTable*, the same object-reference form any other asset-typed member uses),
//   and its row type is NOT spelled on the declaration: UK2Node_GetDataTableRow
//   derives the "Out Row" struct from the DataTable pin's DefaultObject
//   (K2Node_GetDataTableRow.cpp GetDataTableRowStructType -> DataTable->RowStruct,
//   refreshed by PinDefaultValueChanged when the Table pin gets its asset).
//   The doc's `[DataTable] FItemRow DT_Items;` is therefore wrong twice over - the
//   attribute carries no asset binding, and FItemRow is the row struct, not the
//   table type - so the field is written as a plain [Variable] of type DataTable
//   bound to the table asset; DT_Items.GetRow(...) keeps its doc form (no translator
//   support yet, per spec point 6). Doc declares `public struct FItemRow` inside the
//   blueprint block; per Blueprint_Struct_Tests probe finding structs are standalone
//   [Struct] domain blocks, so each task's struct is split out. Doc task 3 "row
//   struct extended to FItemRow (Value + Name)" maps to a fresh FItemRow2 pair
//   (struct member update unverified; FHealth_A/B precedent) and a fresh DT asset
//   DT_Items2 (row-struct update equally unsupported) - task 3 must bind to
//   DT_Items2, since DT_Items only carries the single-field row.
//
// Mis-touch comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9): blueprint A/B assets and the row structs. The
// DataTable assets themselves (DT_Items / DT_Items2) are not snapshotted - they
// have no offline blueprint-graph IR baseline to compare against.
// =========================================================================

public sealed class Blueprint_DataTable : ITestCase
{
    public string Name => "Blueprint Data Table (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 44;

    /// <summary>
    /// Directly sends add_blueprint_node to build BreakStruct for a row struct and
    /// retrieve nodeId. The native BreakStruct node must carry a 'struct' field,
    /// which is not in BlueprintTestHelper.AddNode's parameter list.
    /// </summary>
    private static string AddRowBreakNode(TestContext ctx, string assetPath, string graphName,
        string structName, int x, int y)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_node", new
        {
            path = assetPath,
            graphName,
            name = "BreakStruct",
            x,
            y,
            @struct = structName,
        });
        ctx.AssertTrue(r["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_node BreakStruct({structName}) @ {graphName}: "
            + $"{r["response"]?["reason"]?.GetValue<string>() ?? ""}");
        return r["response"]?["nodeId"]?.GetValue<string>() ?? "";
    }

    /// <summary>
    /// Structs and DataTables have no compile concept - save_asset only
    /// (same as Blueprint_Struct_Tests / Blueprint_Switch_Tests).
    /// </summary>
    private static void SaveAsset(TestContext ctx, string assetPath)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(r["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {assetPath}: {r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    public async Task Run(TestContext ctx)
    {
        // -- Task 1: Read row by name --
        // MCP step 1: row struct asset FItemRow_A (Value : int) - supported.
        const string rowA1 = "/Game/Tests/UE5_0/FItemRow_A";
        var struct1 = BlueprintTestHelper.EditorCall(ctx, "create_blueprint_struct", new
        {
            path = rowA1,
            members = new[]
            {
                new { name = "Value", type = "int" },
            },
        });
        ctx.AssertTrue(struct1["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_blueprint_struct {rowA1}: "
            + $"{struct1["response"]?["reason"]?.GetValue<string>() ?? ""}");
        SaveAsset(ctx, rowA1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, rowA1);

        // MCP step 2: DataTable asset DT_Items (row struct FItemRow_A) - no bridge support;
        // written per spec point 6, unknown-action rejection = pinned gap.
        const string dt1 = "/Game/Tests/UE5_0/DT_Items";
        var dtCreated1 = BlueprintTestHelper.EditorCall(ctx, "create_data_table", new
        {
            path = dt1,
            rowStruct = rowA1,
        });
        ctx.AssertTrue(dtCreated1["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_data_table {dt1}: "
            + $"{dtCreated1["response"]?["reason"]?.GetValue<string>() ?? ""}");
        SaveAsset(ctx, dt1);

        // MCP step 3: DataTable_Name_A + function Read -> GetDataTableRow
        //   (Table = DT_Items, RowName = "Sword"), Break row -> Value -> PrintString.
        const string a1 = "/Game/Tests/UE5_0/DataTable_Name_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        // The table is an object held in a variable - the graph reads it through a
        // getter node, so the Table pin is fed by a link, not by a bare default.
        BlueprintTestHelper.AddVariable(ctx, a1, "DT_Items", "DataTable", dt1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Read");
        var get1 = BlueprintTestHelper.AddNode(ctx, a1, "Read", "DT_Items", 100, 0,
            kind: "getter");
        var row1 = BlueprintTestHelper.AddNode(ctx, a1, "Read", "GetDataTableRow", 300, 0,
            "KismetSystemLibrary");
        // UK2Node_GetDataTableRow's real pin names: "DataTable" / "RowName" /
        // "ReturnValue" (the Out Row). The table arrives over the getter link, so
        // the DataTable pin carries no default (a wired pin ignores one anyway).
        BlueprintTestHelper.Connect(ctx, a1, get1.nodeId, "DT_Items", row1.nodeId, "DataTable");
        BlueprintTestHelper.SetPinDefault(ctx, a1, row1.nodeId, "RowName", "Sword");
        var brk1 = AddRowBreakNode(ctx, a1, "Read", rowA1, 550, 0);
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Read", "PrintString", 800, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, row1.nodeId, "ReturnValue", brk1, "FItemRow_A");
        BlueprintTestHelper.Connect(ctx, a1, brk1, "Value", p1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", row1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, row1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (doc DSL, struct split into standalone [Struct] block per
        // Blueprint_Struct_Tests probe finding; the table field is the object form
        // documented in the file header, GetRow kept verbatim - no translator support
        // for either yet, test is spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        // Structs are not saved on DSL apply - save explicitly (Blueprint_Struct_Tests pattern)
        SaveAsset(ctx, "/Game/Tests/UE5_0/FItemRow_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/FItemRow_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/DataTable_Name_B");

        // -- Task 2: List every row name --
        // Replaces the doc's "read row by index": the engine has no index-based row read
        // (rows are keyed by FName - GetDataTableRowByIndex has zero hits in the engine
        // source), so the only "all rows" accessor is
        // UDataTableFunctionLibrary::GetDataTableRowNames (UE 5.0.3
        // DataTableFunctionLibrary.h). Reuse DT_Items (per doc). ForEachLoop pin names
        // are the macro's real ones (Blueprint_For_Tests: Exec / Array / LoopBody /
        // "Array Element" / Completed).
        const string a2 = "/Game/Tests/UE5_0/DataTable_Rows_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "DT_Items", "DataTable", dt1);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Read");
        var get2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "DT_Items", 100, 0,
            kind: "getter");
        var names2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "GetDataTableRowNames", 250, 0,
            "DataTableFunctionLibrary");
        BlueprintTestHelper.Connect(ctx, a2, get2.nodeId, "DT_Items", names2.nodeId, "Table");
        var fe2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "ForEachLoop", 500, 0,
            kind: "macro");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "PrintString", 750, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", names2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, names2.nodeId, "then", fe2.nodeId, "Exec");
        BlueprintTestHelper.Connect(ctx, a2, names2.nodeId, "OutRowNames", fe2.nodeId, "Array");
        BlueprintTestHelper.Connect(ctx, a2, fe2.nodeId, "LoopBody", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, fe2.nodeId, "Array Element", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, fe2.nodeId, "Completed", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path: GetRowNames() returns the whole row-name array; the local it lands in
        // is iterated with the same foreach form Blueprint_For_Tests uses.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/DataTable_Rows_B");

        // -- Task 3: Multi-field row access --
        // MCP step 1: doc "row struct extended to FItemRow (Value + Name)" maps to a fresh
        //   struct asset FItemRow2_A (struct member update unverified) and a fresh DataTable
        //   DT_Items2 carrying the extended row struct (row-struct update equally unsupported).
        const string rowA3 = "/Game/Tests/UE5_0/FItemRow2_A";
        var struct3 = BlueprintTestHelper.EditorCall(ctx, "create_blueprint_struct", new
        {
            path = rowA3,
            members = new[]
            {
                new { name = "Value", type = "int" },
                new { name = "Name", type = "string" },
            },
        });
        ctx.AssertTrue(struct3["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_blueprint_struct {rowA3}: "
            + $"{struct3["response"]?["reason"]?.GetValue<string>() ?? ""}");
        SaveAsset(ctx, rowA3);

        const string dt3 = "/Game/Tests/UE5_0/DT_Items2";
        var dtCreated3 = BlueprintTestHelper.EditorCall(ctx, "create_data_table", new
        {
            path = dt3,
            rowStruct = rowA3,
        });
        ctx.AssertTrue(dtCreated3["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_data_table {dt3}: "
            + $"{dtCreated3["response"]?["reason"]?.GetValue<string>() ?? ""}");
        SaveAsset(ctx, dt3);

        // MCP step 2: DataTable_Fields_A - read row, break Value + Name, two PrintStrings.
        const string a3 = "/Game/Tests/UE5_0/DataTable_Fields_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "DT_Items2", "DataTable", dt3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Read");
        var get3 = BlueprintTestHelper.AddNode(ctx, a3, "Read", "DT_Items2", 100, 0,
            kind: "getter");
        var row3 = BlueprintTestHelper.AddNode(ctx, a3, "Read", "GetDataTableRow", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, get3.nodeId, "DT_Items2", row3.nodeId, "DataTable");
        BlueprintTestHelper.SetPinDefault(ctx, a3, row3.nodeId, "RowName", "Sword");
        var brk3 = AddRowBreakNode(ctx, a3, "Read", rowA3, 550, 0);
        var pVal3 = BlueprintTestHelper.AddNode(ctx, a3, "Read", "PrintString", 800, -100,
            "KismetSystemLibrary");
        var pName3 = BlueprintTestHelper.AddNode(ctx, a3, "Read", "PrintString", 800, 100,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, row3.nodeId, "ReturnValue", brk3, "FItemRow2_A");
        BlueprintTestHelper.Connect(ctx, a3, brk3, "Value", pVal3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, brk3, "Name", pName3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", row3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, row3.nodeId, "then", pVal3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, pVal3.nodeId, "then", pName3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, pName3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, rowA3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (doc DSL with distinct struct type name FItemRow2 to avoid ambiguity with
        // task 1's FItemRow; [DataTable] field + GetRow kept verbatim - test is spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        SaveAsset(ctx, "/Game/Tests/UE5_0/FItemRow2_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/FItemRow2_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/DataTable_Fields_B");

        await Task.CompletedTask;
    }
}
