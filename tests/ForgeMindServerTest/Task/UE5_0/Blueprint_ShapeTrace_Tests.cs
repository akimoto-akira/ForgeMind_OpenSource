// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 57 - Blueprint Shape Trace (UE 5.0) - from Tasks/UE_5.0/blueprint-shape-trace.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit.
// SphereTraceSingle / BoxTraceSingle / CapsuleTraceSingle are the real C++
// function names on UKismetSystemLibrary (the editor shows them as
// "Sphere/Box/Capsule Trace By Channel" via DisplayName meta).
// They are static BlueprintCallable (NOT pure) -- they have exec pins,
// so exec chains entry.then -> trace.execute -> trace.then -> result.execute.
// ETraceTypeQuery.TraceTypeQuery1 is the dotted enum-literal form for this
// project's Visibility channel (verified in LineTrace test, run 20261012_0226;
// ECC_Visibility is an engine constant outside DeclaredEnumRegistry and must
// be expressed as ETraceTypeQuery.TraceTypeQuery1 per DSL-syntax.md §4.7.1).
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - SphereTraceSingle(s, e, 20, ETraceTypeQuery.TraceTypeQuery1) -> bool
//   Task 2 - BoxTraceSingle(s, e, HalfSize, Rot, ETraceTypeQuery.TraceTypeQuery1) -> bool
//   Task 3 - CapsuleTraceSingle(s, e, 15, 40, ETraceTypeQuery.TraceTypeQuery1) -> bool
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-18 per user instruction,
// after this case was confirmed by visual inspection. One per asset -- A side after
// CompileAndSave, DSL side after ApplyDsl (6 total: Trace_Sphere_A/B, Trace_Box_A/B,
// Trace_Cap_A/B). Same convention as Blueprint_Select_Tests / Blueprint_Rotation_Tests.
// Baselines are seeded on the first run; from the second run onward they actually diff.
// =========================================================================

public sealed class Blueprint_ShapeTrace : ITestCase
{
    public string Name => "Blueprint Shape Trace (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 57;

    public async Task Run(TestContext ctx)
    {
        BuildTask1(ctx);
        BuildTask2(ctx);
        BuildTask3(ctx);
        await Task.CompletedTask;
    }

    private static void BuildTask1(TestContext ctx)
    {
        {
            const string a1 = "/Game/Tests/UE5_0/Trace_Sphere_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Probe",
                new[] { new { name = "s", type = "vector" },
                        new { name = "e", type = "vector" } },
                outputs: new[] { new { name = "ReturnValue", type = "bool" } });
            var trace = BlueprintTestHelper.AddNode(ctx, a1, "Probe", "SphereTraceSingle", 300, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "s", trace.nodeId, "Start");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "e", trace.nodeId, "End");
            BlueprintTestHelper.SetPinDefault(ctx, a1, trace.nodeId, "Radius", "20.0");
            BlueprintTestHelper.SetPinDefault(ctx, a1, trace.nodeId, "TraceChannel", "TraceTypeQuery1");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", trace.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, trace.nodeId, "then", f1.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, trace.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison (spec point 9: inserted per user instruction)
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Trace_Sphere_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Trace_Box_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Probe",
                new[] { new { name = "s", type = "vector" },
                        new { name = "e", type = "vector" } },
                outputs: new[] { new { name = "ReturnValue", type = "bool" } });
            var trace = BlueprintTestHelper.AddNode(ctx, a2, "Probe", "BoxTraceSingle", 300, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "s", trace.nodeId, "Start");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "e", trace.nodeId, "End");
            // FVector / FRotator pin defaults must be the engine's bare three-number
            // comma form (see Blueprint_Struct_Tests.cs L21-22 and bridge-side strict
            // check in ForgeMindBridgeClient_BlueprintGraph.cpp HandleSetPinDefaultValue).
            // Parenthesised import-text like "(10,10,10)" / "(Pitch=..,Yaw=..,Roll=..)"
            // is rejected by Schema->IsPinDefaultValid ("Invalid value for an FVector").
            BlueprintTestHelper.SetPinDefault(ctx, a2, trace.nodeId, "HalfSize", "10.000000,10.000000,10.000000");
            BlueprintTestHelper.SetPinDefault(ctx, a2, trace.nodeId, "Orientation", "0.000000,0.000000,0.000000");
            BlueprintTestHelper.SetPinDefault(ctx, a2, trace.nodeId, "TraceChannel", "TraceTypeQuery1");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", trace.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, trace.nodeId, "then", f2.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, trace.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Trace_Box_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Trace_Cap_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Probe",
                new[] { new { name = "s", type = "vector" },
                        new { name = "e", type = "vector" } },
                outputs: new[] { new { name = "ReturnValue", type = "bool" } });
            var trace = BlueprintTestHelper.AddNode(ctx, a3, "Probe", "CapsuleTraceSingle", 300, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "s", trace.nodeId, "Start");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "e", trace.nodeId, "End");
            BlueprintTestHelper.SetPinDefault(ctx, a3, trace.nodeId, "Radius", "15.0");
            BlueprintTestHelper.SetPinDefault(ctx, a3, trace.nodeId, "HalfHeight", "40.0");
            BlueprintTestHelper.SetPinDefault(ctx, a3, trace.nodeId, "TraceChannel", "TraceTypeQuery1");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", trace.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a3, trace.nodeId, "then", f3.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a3, trace.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Trace_Cap_B");
    }
}