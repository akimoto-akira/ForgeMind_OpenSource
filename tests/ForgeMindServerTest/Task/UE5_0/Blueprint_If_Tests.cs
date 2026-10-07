// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 1 · Blueprint If Branch (UE 5.0)  — from Tasks/UE_5.0/Blueprint-If.md
// =========================================================================

public sealed class Blueprint_If : ITestCase
{
    public string Name => "Blueprint If Branch (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 1;

    private static readonly string[] Assets =
    [
        "/Game/Tests/UE5_0/If_Single_A",  "/Game/Tests/UE5_0/If_Single_B",
        "/Game/Tests/UE5_0/If_Else_A",    "/Game/Tests/UE5_0/If_Else_B",
        "/Game/Tests/UE5_0/If_ElseIf_A",  "/Game/Tests/UE5_0/If_ElseIf_B",
        "/Game/Tests/UE5_0/If_Nested_A", "/Game/Tests/UE5_0/If_Nested_B",
        "/Game/Tests/UE5_0/If_Cast_A",    "/Game/Tests/UE5_0/If_Cast_B",
    ];

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: Single if ──
        // Pin names are engine real names (UE 5.0 EdGraphSchema_K2): entry's exec-out is
        // "then", call node's exec-in is "execute"; Branch split real names are
        // "then"/"else" (the editor shows True/False via PinFriendlyName).
        const string a1 = "/Game/Tests/UE5_0/If_Single_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "IsLocked", "bool", false);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "CheckLock");
        var g1 = BlueprintTestHelper.AddNode(ctx, a1, "CheckLock", "IsLocked", 100, 0, kind: "getter");
        var b1 = BlueprintTestHelper.AddNode(ctx, a1, "CheckLock", "Branch", 300, 0);
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", b1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, g1.nodeId, "IsLocked", b1.nodeId, "Condition");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "CheckLock", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "Locked!");
        BlueprintTestHelper.Connect(ctx, a1, b1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, b1.nodeId, "else", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/If_Single_B");

        // ── Sub-task 2: if-else ──
        const string a2 = "/Game/Tests/UE5_0/If_Else_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "IsLocked", "bool", false);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Interact");
        var g2 = BlueprintTestHelper.AddNode(ctx, a2, "Interact", "IsLocked", 100, 0, kind: "getter");
        var b2 = BlueprintTestHelper.AddNode(ctx, a2, "Interact", "Branch", 300, 0);
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", b2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, g2.nodeId, "IsLocked", b2.nodeId, "Condition");
        var pt = BlueprintTestHelper.AddNode(ctx, a2, "Interact", "PrintString", 600, -50, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, pt.nodeId, "InString", "Locked!");
        BlueprintTestHelper.Connect(ctx, a2, b2.nodeId, "then", pt.nodeId, "execute");
        var pf = BlueprintTestHelper.AddNode(ctx, a2, "Interact", "PrintString", 600, 100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, pf.nodeId, "InString", "Open!");
        BlueprintTestHelper.Connect(ctx, a2, b2.nodeId, "else", pf.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, pt.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, pf.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/If_Else_B");

        // ── Sub-task 3: else-if chain ──
        // DSL conditions are Health <= 0 / Health < 30; the stepping path uses
        // LessEqual_DoubleDouble / Less_DoubleDouble (KismetMathLibrary) + literals to restore.
        const string a3 = "/Game/Tests/UE5_0/If_ElseIf_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Health", "float", 100f);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "OnHealthChanged");
        var g3 = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "Health", 100, 0, kind: "getter");
        var le3 = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "LessEqual_DoubleDouble", 200, -100, "KismetMathLibrary");
        var mf0 = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "MakeLiteralFloat", 200, -200, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, mf0.nodeId, "Value", "0");
        BlueprintTestHelper.Connect(ctx, a3, g3.nodeId, "Health", le3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, mf0.nodeId, "ReturnValue", le3.nodeId, "B");
        var o3 = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "Branch", 300, 0);
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", o3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, le3.nodeId, "ReturnValue", o3.nodeId, "Condition");
        var pd = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "PrintString", 600, -100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, pd.nodeId, "InString", "Dead");
        BlueprintTestHelper.Connect(ctx, a3, o3.nodeId, "then", pd.nodeId, "execute");
        var lt3 = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "Less_DoubleDouble", 400, 100, "KismetMathLibrary");
        var mf30 = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "MakeLiteralFloat", 400, 200, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, mf30.nodeId, "Value", "30");
        BlueprintTestHelper.Connect(ctx, a3, g3.nodeId, "Health", lt3.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a3, mf30.nodeId, "ReturnValue", lt3.nodeId, "B");
        var i3 = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "Branch", 600, 100);
        BlueprintTestHelper.Connect(ctx, a3, o3.nodeId, "else", i3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, lt3.nodeId, "ReturnValue", i3.nodeId, "Condition");
        var ph = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "PrintString", 900, 50, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, ph.nodeId, "InString", "Hurt");
        BlueprintTestHelper.Connect(ctx, a3, i3.nodeId, "then", ph.nodeId, "execute");
        var pi = BlueprintTestHelper.AddNode(ctx, a3, "OnHealthChanged", "PrintString", 900, 200, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, pi.nodeId, "InString", "Idle");
        BlueprintTestHelper.Connect(ctx, a3, i3.nodeId, "else", pi.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, pd.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, ph.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, pi.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/If_ElseIf_B");

        // ── Sub-task 4: Nested if ──
        const string a4 = "/Game/Tests/UE5_0/If_Nested_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a4);
        BlueprintTestHelper.AddVariable(ctx, a4, "IsLocked", "bool", false);
        BlueprintTestHelper.AddVariable(ctx, a4, "HasKey", "bool", false);
        var f4 = BlueprintTestHelper.CreateFunction(ctx, a4, "TryOpen");
        var gLocked4 = BlueprintTestHelper.AddNode(ctx, a4, "TryOpen", "IsLocked", 100, 0, kind: "getter");
        var gKey4 = BlueprintTestHelper.AddNode(ctx, a4, "TryOpen", "HasKey", 100, 100, kind: "getter");
        var o4 = BlueprintTestHelper.AddNode(ctx, a4, "TryOpen", "Branch", 300, 0);
        BlueprintTestHelper.Connect(ctx, a4, f4.entryId, "then", o4.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a4, gLocked4.nodeId, "IsLocked", o4.nodeId, "Condition");
        var i4 = BlueprintTestHelper.AddNode(ctx, a4, "TryOpen", "Branch", 600, 0);
        BlueprintTestHelper.Connect(ctx, a4, o4.nodeId, "then", i4.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a4, gKey4.nodeId, "HasKey", i4.nodeId, "Condition");
        var po = BlueprintTestHelper.AddNode(ctx, a4, "TryOpen", "PrintString", 900, -50, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a4, po.nodeId, "InString", "Opened");
        BlueprintTestHelper.Connect(ctx, a4, i4.nodeId, "then", po.nodeId, "execute");
        var pn = BlueprintTestHelper.AddNode(ctx, a4, "TryOpen", "PrintString", 900, 100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a4, pn.nodeId, "InString", "NoKey");
        BlueprintTestHelper.Connect(ctx, a4, i4.nodeId, "else", pn.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a4, o4.nodeId, "else", f4.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a4, po.nodeId, "then", f4.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a4, pn.nodeId, "then", f4.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a4);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a4);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/If_Nested_B");

        // ── Sub-task 5: if + Cast guard ──
        // DSL is `if (other is ACharacter c)` — the stepping path uses an executed Cast node to restore:
        // success (then) enters the print, failure (CastFailed) is left unconnected, matching DSL semantics.
        const string a5 = "/Game/Tests/UE5_0/If_Cast_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a5);
        var f5 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a5, "OnOverlap",
            new[] { new { name = "other", type = "Actor" } });
        var c5 = BlueprintTestHelper.AddNode(ctx, a5, "OnOverlap", "Cast<ACharacter>", 300, 0);
        BlueprintTestHelper.Connect(ctx, a5, f5.entryId, "then", c5.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a5, f5.entryId, "other", c5.nodeId, "Object");
        var p5 = BlueprintTestHelper.AddNode(ctx, a5, "OnOverlap", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a5, p5.nodeId, "InString", "Character overlapped");
        BlueprintTestHelper.Connect(ctx, a5, c5.nodeId, "then", p5.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a5, p5.nodeId, "then", f5.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a5, c5.nodeId, "CastFailed", f5.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a5);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a5);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/If_Cast_B");

        await Task.CompletedTask;
    }
}
