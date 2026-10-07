// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 3 - Blueprint Cast (UE 5.0) - from Tasks/UE_5.0/blueprint-cast.md
// =========================================================================

public sealed class Blueprint_Cast : ITestCase
{
    public string Name => "Blueprint Cast (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 3;

    private static readonly string[] Assets =
    [
        "/Game/Tests/UE5_0/Cast_Basic_A",  "/Game/Tests/UE5_0/Cast_Basic_B",
        "/Game/Tests/UE5_0/Cast_Fail_A",   "/Game/Tests/UE5_0/Cast_Fail_B",
        "/Game/Tests/UE5_0/Cast_Chain_A", "/Game/Tests/UE5_0/Cast_Chain_B",
    ];

    private static readonly object[] InputOther =
        new[] { new { name = "other", type = "Actor" } };

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: Basic Cast --
        // Pin names are engine real names (UE 5.0 EdGraphSchema_K2): entry's exec-out is
        // "then", call node exec-in is "execute"; Cast success branch is also "then",
        // failure branch is "CastFailed"; function parameter pin name is the param name ("other").
        const string a1 = "/Game/Tests/UE5_0/Cast_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Check", InputOther);
        var c1 = BlueprintTestHelper.AddNode(ctx, a1, "Check", "Cast<ACharacter>", 300, 0);
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", c1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "other", c1.nodeId, "Object");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Check", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, p1.nodeId, "InString", "Char");
        BlueprintTestHelper.Connect(ctx, a1, c1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, c1.nodeId, "CastFailed", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Cast_Basic_B");

        // -- Subtask 2: Cast Failure Branch --
        const string a2 = "/Game/Tests/UE5_0/Cast_Fail_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Check", InputOther);
        var c2 = BlueprintTestHelper.AddNode(ctx, a2, "Check", "Cast<ACharacter>", 300, 0);
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", c2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "other", c2.nodeId, "Object");
        var p2t = BlueprintTestHelper.AddNode(ctx, a2, "Check", "PrintString", 600, -50, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, p2t.nodeId, "InString", "Char");
        BlueprintTestHelper.Connect(ctx, a2, c2.nodeId, "then", p2t.nodeId, "execute");
        var p2f = BlueprintTestHelper.AddNode(ctx, a2, "Check", "PrintString", 600, 100, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, p2f.nodeId, "InString", "Other");
        BlueprintTestHelper.Connect(ctx, a2, c2.nodeId, "CastFailed", p2f.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2t.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2f.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Cast_Fail_B");

        // -- Subtask 3: Chained Cast --
        const string a3 = "/Game/Tests/UE5_0/Cast_Chain_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Check", InputOther);

        // Outer Cast a -> APawn
        var outer = BlueprintTestHelper.AddNode(ctx, a3, "Check", "Cast<APawn>", 300, 0);
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", outer.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "other", outer.nodeId, "Object");

        // Inner Cast -> ACharacter (inside success body)
        var inner = BlueprintTestHelper.AddNode(ctx, a3, "Check", "Cast<ACharacter>", 600, 0);
        BlueprintTestHelper.Connect(ctx, a3, outer.nodeId, "then", inner.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, outer.nodeId, "AsPawn", inner.nodeId, "Object");

        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "Check", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, p3.nodeId, "InString", "Char");
        BlueprintTestHelper.Connect(ctx, a3, inner.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, p3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, inner.nodeId, "CastFailed", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, outer.nodeId, "CastFailed", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Cast_Chain_B");

        await Task.CompletedTask;
    }
}
