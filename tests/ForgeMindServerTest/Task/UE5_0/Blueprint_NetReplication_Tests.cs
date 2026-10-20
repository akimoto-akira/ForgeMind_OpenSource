// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 50 - Blueprint Network Replication (UE 5.0) - from Tasks/UE_5.0/blueprint-net-replication.md
//
// The DSL (B) side is fully implemented across translator + IR + bridge and genuinely
// applies net replication (no longer a silent drop):
//   - [Replicated] variable   -> CPF_Net (add_blueprint_variable 'replication')
//   - [Replicated(RepNotify)]  -> CPF_Net|CPF_RepNotify + RepNotifyFunc=OnRep_<var> (auto).
//     The OnRep_<var> callback is created as a FUNCTION by the translator (UE makes OnRep a
//     function); the translator decides this kind, so the DSL does not mark it [Function] and
//     it is never an event.
//   - [Replicates] class       -> Actor CDO bReplicates (create_blueprint 'replicates')
//   - [Event, Replicate(Server/Client/Multicast, Reliable)] -> custom event node FUNC_Net|<dir>
//     [|FUNC_NetReliable] (add_blueprint_node kind=customevent 'rpc'/'reliable'). RPC is an EVENT
//     (a function cannot be replicated); mode is one of Server/Client/Multicast, omit Replicate =
//     not replicated, Reliable optional (UE defaults to Unreliable).
// The A-side RPC is built as a replicated CUSTOM EVENT (add_blueprint_node kind=customevent with
// 'rpc'/'reliable'); its parameter is read from the event node's own output pin (not a variable
// getter - event params are not blueprint variables). Task 3 is therefore built directly via MCP
// (no longer wrapped), matching the B-side [Event, Replicate(Server, Reliable)] construction.
// Task 1 builds its replicated variable via add_blueprint_variable 'replication' and its
// bReplicates via create_blueprint 'replicates'; the build is unwrapped, so a bridge
// rejection aborts the case.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Replicated variable (CPF_Net)
//   Task 2 - RepNotify variable (auto OnRep_Ammo)
//   Task 3 - Server RPC (Run on Server, Reliable)
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (spec point 9) after
// visual confirmation; each A-side (MCP) and DSL-side asset stores its own graph-level IR
// baseline on first run and diffs against it later (per-asset self-comparison, not A/B).
// =========================================================================

public sealed class Blueprint_NetReplication : ITestCase
{
    public string Name => "Blueprint Network Replication (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 50;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    // add_blueprint_variable with optional 'replication' ('none' | 'replicated'). A rejected
    // replication mode (e.g. RepNotify on an unsupported bridge) fails the assertion and aborts
    // the case -- it is never swallowed.
    private static void AddVar(TestContext ctx, string path, string name, string type,
        string defaultValue, string? replication = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["path"] = path, ["name"] = name, ["type"] = type, ["default"] = defaultValue,
        };
        if (replication != null) payload["replication"] = replication;
        var r = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_variable", payload);
        ctx.AssertTrue(r["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_variable {name} ({type}) @ {path}: "
            + $"{r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    private static void BuildTask1(TestContext ctx)
    {
        {
            const string a1 = "/Game/Tests/UE5_0/Net_Rep_A";
            // create_blueprint now honors 'replicates' -> Actor CDO bReplicates (class [Replicates]).
            BlueprintTestHelper.CreateBlueprint(ctx, a1, "Actor", replicates: true);
            AddVar(ctx, a1, "Health", "int", "100", replication: "replicated");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
        }
        // The IR snapshot runs only once the A-side build completed; a build failure already
        // aborted the case rather than passing silently.
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Net_Rep_A");

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Net_Rep_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        const string a2 = "/Game/Tests/UE5_0/Net_Notify_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a2, "Actor", replicates: true);
        // RepNotify variable - add_blueprint_variable now applies 'repNotify' (+ optional onRep).
        AddVar(ctx, a2, "Ammo", "int", "0", replication: "repNotify");
        // OnRep_Ammo function: PrintString(Ammo) - built faithfully (function graph itself is supported).
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "OnRep_Ammo");
        var getAmmo = BlueprintTestHelper.AddNode(ctx, a2, "OnRep_Ammo", "Ammo", 100, 0, kind: "getter");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "OnRep_Ammo", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getAmmo.nodeId, "Ammo", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Net_Notify_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        const string a3 = "/Game/Tests/UE5_0/Net_RPC_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a3, "Actor", replicates: true);
        // Server RPC = a replicated CUSTOM EVENT (a function cannot be replicated). Created in the
        // event graph with an 'n' parameter and FUNC_Net|FUNC_NetServer|FUNC_NetReliable; the event's
        // own 'n' output pin feeds PrintString (params are read from the event pin, not a getter).
        var rpc3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "TakeDamage", 0, 0,
            kind: "customevent", eventParams: new[] { new { name = "n", type = "int" } },
            rpc: "server", reliable: true);
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "PrintString", 300, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, rpc3.nodeId, "n", p3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, rpc3.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Net_RPC_B");
    }
}
