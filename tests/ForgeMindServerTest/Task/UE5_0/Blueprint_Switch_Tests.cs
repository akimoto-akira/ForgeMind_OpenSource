// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 5 · Blueprint Switch (UE 5.0)  — from Tasks/UE_5.0/Blueprint-Switch.md
// =========================================================================

public sealed class Blueprint_Switch : ITestCase
{
    public string Name => "Blueprint Switch (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 5;

    private static readonly object[] InputMode =
        new[] { new { name = "Mode", type = "int" } };

    private static readonly object[] InputKey =
        new[] { new { name = "Key", type = "string" } };

    /// <summary>
    /// Save non-blueprint assets (enums, etc.): enums have no compile concept, cannot use CompileAndSave
    /// (that would issue compile_blueprint on enum assets); here we only issue save_asset and
    /// assert the save succeeds, preventing assets from lingering in memory in a dirty state.
    /// </summary>
    private static void SaveAsset(TestContext ctx, string assetPath)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(r["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {assetPath}: {r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: Switch on Int ──
        // Pin names are engine real names: Switch input is "Selection", branches are "0"/"1"/"Default";
        // function parameters are taken from the entry node's output pin of the same name ("Mode").
        const string a1 = "/Game/Tests/UE5_0/Switch_Int_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Run", InputMode);
        var sw = BlueprintTestHelper.AddNode(ctx, a1, "Run", "SwitchOnInt", 300, 0, intCases: new[] { 0, 1 });
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", sw.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "Mode", sw.nodeId, "Selection");

        var p0 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, -100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p0.nodeId, "InString", "Zero");
        BlueprintTestHelper.Connect(ctx, a1, sw.nodeId, "0", p0.nodeId, "execute");

        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "One");
        BlueprintTestHelper.Connect(ctx, a1, sw.nodeId, "1", p1.nodeId, "execute");

        var pd = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, 100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, pd.nodeId, "InString", "Other");
        BlueprintTestHelper.Connect(ctx, a1, sw.nodeId, "Default", pd.nodeId, "execute");

        BlueprintTestHelper.Connect(ctx, a1, p0.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, pd.nodeId, "then", f1.resultId, "execute");

        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Switch_Int_B");

        // ── Sub-task 2: Switch on Enum ──
        const string a2 = "/Game/Tests/UE5_0/Switch_Enum_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);

        // Create enum asset EState
        var enumResult = BlueprintTestHelper.EditorCall(ctx, "create_blueprint_enum",
            new { path = "/Game/Tests/UE5_0/EState_A" });
        BlueprintTestHelper.EditorCall(ctx, "add_enum_entry",
            new { path = "/Game/Tests/UE5_0/EState_A", name = "Idle" });
        BlueprintTestHelper.EditorCall(ctx, "add_enum_entry",
            new { path = "/Game/Tests/UE5_0/EState_A", name = "Run" });
        BlueprintTestHelper.EditorCall(ctx, "add_enum_entry",
            new { path = "/Game/Tests/UE5_0/EState_A", name = "Jump" });

        // Enum assets have no compile concept; CompileAndSave only covers blueprints — EState_A
        // needs explicit save, otherwise the three enum entries linger in memory in a dirty state
        SaveAsset(ctx, "/Game/Tests/UE5_0/EState_A");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EState_A");

        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Run",
            new[] { new { name = "s", type = "/Game/Tests/UE5_0/EState_A" } });
        var swe = BlueprintTestHelper.AddNode(ctx, a2, "Run", "SwitchOnEnum", 300, 0,
            enumName: "/Game/Tests/UE5_0/EState_A");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", swe.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "s", swe.nodeId, "Selection");

        // SwitchOnEnum case output pin FNames are enum entry internal names (NewEnumerator0/1/2);
        // UI display names Idle/Run/Jump are DisplayName mappings and cannot be used for connect.
        var pe0 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "PrintString", 600, -100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, pe0.nodeId, "InString", "Idle");
        BlueprintTestHelper.Connect(ctx, a2, swe.nodeId, "NewEnumerator0", pe0.nodeId, "execute");

        var pe1 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, pe1.nodeId, "InString", "Run");
        BlueprintTestHelper.Connect(ctx, a2, swe.nodeId, "NewEnumerator1", pe1.nodeId, "execute");

        var pe2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "PrintString", 600, 100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, pe2.nodeId, "InString", "Jump");
        BlueprintTestHelper.Connect(ctx, a2, swe.nodeId, "NewEnumerator2", pe2.nodeId, "execute");

        BlueprintTestHelper.Connect(ctx, a2, pe0.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, pe1.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, pe2.nodeId, "then", f2.resultId, "execute");

        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");

        // ApplyDsl only compiles + saves assets with assetType == "blueprint"; the DSL-created
        // EState_B is not included — add an explicit save instruction
        SaveAsset(ctx, "/Game/Tests/UE5_0/EState_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EState_B");

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Switch_Enum_B");

        // ── Sub-task 3: Switch on String ──
        const string a3 = "/Game/Tests/UE5_0/Switch_Str_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Run", InputKey);
        var sws = BlueprintTestHelper.AddNode(ctx, a3, "Run", "SwitchOnString", 300, 0,
            stringCases: new[] { "A" });
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", sws.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "Key", sws.nodeId, "Selection");

        var pa = BlueprintTestHelper.AddNode(ctx, a3, "Run", "PrintString", 600, -50, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, pa.nodeId, "InString", "Alpha");
        BlueprintTestHelper.Connect(ctx, a3, sws.nodeId, "A", pa.nodeId, "execute");

        var pu = BlueprintTestHelper.AddNode(ctx, a3, "Run", "PrintString", 600, 100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, pu.nodeId, "InString", "Unknown");
        BlueprintTestHelper.Connect(ctx, a3, sws.nodeId, "Default", pu.nodeId, "execute");

        BlueprintTestHelper.Connect(ctx, a3, pa.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, pu.nodeId, "then", f3.resultId, "execute");

        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Switch_Str_B");

        await Task.CompletedTask;
    }
}
