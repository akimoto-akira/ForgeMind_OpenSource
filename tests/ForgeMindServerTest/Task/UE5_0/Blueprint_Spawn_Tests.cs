// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 4 · Blueprint SpawnActor (UE 5.0)  — from Tasks/UE_5.0/Blueprint-Spawn.md
// =========================================================================

public sealed class Blueprint_Spawn : ITestCase
{
    public string Name => "Blueprint SpawnActor (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 4;

    public async Task Run(TestContext ctx)
    {
        // ── Sub-task 1: Spawn from Class ──
        const string a1 = "/Game/Tests/UE5_0/Spawn_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Go");
        var spawn1 = BlueprintTestHelper.AddNode(ctx, a1, "Go", "SpawnActorFromClass", 300, 0);
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", spawn1.nodeId, "execute");
        BlueprintTestHelper.SetPinDefault(ctx, a1, spawn1.nodeId, "Class", "Actor");
        // SpawnActorFromClass's SpawnTransform pin has no default value; leaving it
        // unconnected will cause blueprint compile to fail (missing transform input).
        // Following Sub-task 2's approach, add a MakeTransform pure function node with
        // an identity transform connected to SpawnTransform so the compile passes.
        var makeTf1 = BlueprintTestHelper.AddNode(ctx, a1, "Go", "MakeTransform", 600, -100, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, makeTf1.nodeId, "Location", "0, 0, 0");
        BlueprintTestHelper.Connect(ctx, a1, makeTf1.nodeId, "ReturnValue", spawn1.nodeId, "SpawnTransform");
        BlueprintTestHelper.Connect(ctx, a1, spawn1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // SpawnActor must carry a transform parameter (parser enforces: missing input is
        // directly rejected with error), consistent with Sub-task 2's transform-carrying
        // form; connect identity or specified transform to SpawnTransform.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Spawn_Basic_B");

        // ── Sub-task 2: Spawn with Transform ──
        // MakeTransform is a KismetMathLibrary static function; its location pin real name is "Location".
        // Vector pin default values must use bare triple-number form "X, Y, Z" (EdGraphSchema_K2's
        // SplitPin splits on commas + Atof parse; "X=..,Y=.." would parse to all zeros).
        const string a2 = "/Game/Tests/UE5_0/Spawn_Tf_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Go");
        var spawn2 = BlueprintTestHelper.AddNode(ctx, a2, "Go", "SpawnActorFromClass", 300, 0);
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", spawn2.nodeId, "execute");
        BlueprintTestHelper.SetPinDefault(ctx, a2, spawn2.nodeId, "Class", "Actor");
        var makeTf = BlueprintTestHelper.AddNode(ctx, a2, "Go", "MakeTransform", 600, -100, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, makeTf.nodeId, "Location", "100, 0, 0");
        BlueprintTestHelper.Connect(ctx, a2, makeTf.nodeId, "ReturnValue", spawn2.nodeId, "SpawnTransform");
        BlueprintTestHelper.Connect(ctx, a2, spawn2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Spawn_Tf_B");

        // ── Sub-task 3: Spawn and save reference ──
        // DSL is `Last = SpawnActor(...)` — the stepping path uses a setter node to capture the
        // return value, and wires entry → spawn → set into an execution chain.
        const string a3 = "/Game/Tests/UE5_0/Spawn_Ref_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Last", "Actor");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Go");
        var spawn3 = BlueprintTestHelper.AddNode(ctx, a3, "Go", "SpawnActorFromClass", 300, 0);
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", spawn3.nodeId, "execute");
        BlueprintTestHelper.SetPinDefault(ctx, a3, spawn3.nodeId, "Class", "Actor");
        // SpawnActorFromClass's SpawnTransform pin has no default value; leaving it
        // unconnected will fail compile (same as Sub-task 1). The DSL-side
        // `Last = SpawnActor(typeof(Actor));` lacks the actual transform argument,
        // so use a default identity MakeTransform connected to SpawnTransform here.
        var makeTf3 = BlueprintTestHelper.AddNode(ctx, a3, "Go", "MakeTransform", 600, -100, "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, makeTf3.nodeId, "Location", "0, 0, 0");
        BlueprintTestHelper.Connect(ctx, a3, makeTf3.nodeId, "ReturnValue", spawn3.nodeId, "SpawnTransform");
        var setLast = BlueprintTestHelper.AddNode(ctx, a3, "Go", "Last", 600, 0, kind: "setter");
        BlueprintTestHelper.Connect(ctx, a3, spawn3.nodeId, "then", setLast.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, spawn3.nodeId, "ReturnValue", setLast.nodeId, "Last");
        BlueprintTestHelper.Connect(ctx, a3, setLast.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Spawn_Ref_B");

        await Task.CompletedTask;
    }
}
