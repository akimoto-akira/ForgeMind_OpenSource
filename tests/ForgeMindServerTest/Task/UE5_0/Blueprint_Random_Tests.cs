// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 49 - Blueprint Random (UE 5.0) - from Tasks/UE_5.0/blueprint-random.md
//
// The random operations are BlueprintPure KismetMathLibrary functions
// (RandomIntegerInRange / RandomFloat / RandomIntegerInRangeFromStream), so the A
// (MCP) side spawns them as ordinary CallFunction nodes and wires them as DATA
// (ReturnValue -> PrintString.InString). They have NO exec pins, so they must not be
// chained into the exec flow. RandomStream is a blueprint struct; its seed is applied
// to the variable default through the bridge's FRandomStream special-case (Initialize).
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Random Integer in Range(0,9) -> PrintString
//   Task 2 - Random Float -> PrintString
//   Task 3 - RandomStream (fixed seed 1234) Generate Random Integer(0,9) -> PrintString
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed; each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run and diffs against it later
// (per-asset self-comparison, not A/B cross-comparison).
// =========================================================================

public sealed class Blueprint_Random : ITestCase
{
    public string Name => "Blueprint Random (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 49;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    private static void BuildTask1(TestContext ctx)
    {
        const string a1 = "/Game/Tests/UE5_0/Random_Int_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Run");
        // RandomIntegerInRange is a pure KismetMathLibrary function: no exec pins.
        var rnd1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "RandomIntegerInRange", 300, 0);
        BlueprintTestHelper.SetPinDefault(ctx, a1, rnd1.nodeId, "Min", "0");
        BlueprintTestHelper.SetPinDefault(ctx, a1, rnd1.nodeId, "Max", "9");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, rnd1.nodeId, "ReturnValue", print1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", print1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, print1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Random_Int_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        const string a2 = "/Game/Tests/UE5_0/Random_Float_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Run");
        // RandomFloat is a pure KismetMathLibrary function: no exec pins.
        var rnd2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "RandomFloat", 300, 0);
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, rnd2.nodeId, "ReturnValue", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Random_Float_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        const string a3 = "/Game/Tests/UE5_0/Random_Stream_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        // RandomStream struct variable; the seed is applied to the default value by the
        // bridge's FRandomStream special-case (Initialize).
        BlueprintTestHelper.AddVariable(ctx, a3, "Stream", "RandomStream", "1234");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Run");
        // RandomIntegerInRangeFromStream: pure KismetMathLibrary function (Min/Max/Stream).
        var rnd3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "RandomIntegerInRangeFromStream", 300, 0, "KismetMathLibrary");
        var getStream3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "Stream", 100, 0, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a3, getStream3.nodeId, "Stream", rnd3.nodeId, "Stream");
        BlueprintTestHelper.SetPinDefault(ctx, a3, rnd3.nodeId, "Min", "0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, rnd3.nodeId, "Max", "9");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "Run", "PrintString", 600, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, rnd3.nodeId, "ReturnValue", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, print3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Random_Stream_B");
    }
}
