// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 53 - Blueprint Spline (UE 5.0) - from Tasks/UE_5.0/blueprint-spline.md
//
// Converted per collaboration spec point 6: probe first, but author the test
// as if supported -- failures expose bridge/DSL gaps as pending requirements.
// DSL forms follow the latest DSL-syntax.md rules (Mode = Create explicit on every
// create block). ESplineCoordinateSpace.World is an ENGINE enum constant --
// outside DeclaredEnumRegistry (DSL-syntax.md §4.1); written as-is so the run
// either verifies it or exposes the engine-enum-literal gap as a spec item.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - GetLocationAtSplinePoint (Vector At(int i))
//   Task 2 - GetTangentAtSplinePoint (Vector Tan(int i))
//   Task 3 - GetTransformAtSplinePoint (Transform Xf(int i))
//
// A-side pin names (run 20261017_1241): the index pin of all three functions is
// 'PointIndex' -- the C++ parameter name (SplineComponent.h:521/533/568). The
// earlier 'SplinePointIndex' does not exist; the bridge reported
// "has no pin named 'SplinePointIndex'; its pins are: self (input),
// PointIndex (input), CoordinateSpace (input), ReturnValue (output)" and the
// thrown assertion aborted the rest of the A-side build (CompileAndSave never
// ran, so the asset stayed unsaved). Also confirmed by that pin list: these are
// 'const BlueprintCallable' functions, which the editor renders as PURE nodes
// (no execute/then pins) -- so the exec chain legitimately skips the call node
// and goes entry.then -> result.execute (same precedent as the Curve test,
// run 20261011_1108). The component target is addressed as '__target', which
// the bridge resolves to the node's 'self' pin.
//
// Note: mis-touch comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9), covering all six assets - the 3 A-side (MCP)
// and the 3 DSL-side blueprints. Each stores its own graph-level IR baseline
// on first run (no failure); later runs diff against it to catch accidental
// damage to already-tested structures (per-asset self-comparison, not A/B).
// =========================================================================

public sealed class Blueprint_Spline : ITestCase
{
    public string Name => "Blueprint Spline (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 53;

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
            const string a1 = "/Game/Tests/UE5_0/Spline_Loc_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            BlueprintTestHelper.AddComponent(ctx, a1, "Spline", "SplineComponent");
            var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "At",
                new[] { new { name = "i", type = "int" } },
                outputs: new[] { new { name = "ReturnValue", type = "vector" } });
            var getSpline = BlueprintTestHelper.AddNode(ctx, a1, "At", "Spline", 100, 0, kind: "getter");
            var loc = BlueprintTestHelper.AddNode(ctx, a1, "At", "GetLocationAtSplinePoint", 300, 0,
                "SplineComponent");
            // Member call on the Spline component: getter -> "__target".
            BlueprintTestHelper.Connect(ctx, a1, getSpline.nodeId, "Spline", loc.nodeId, "__target");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "i", loc.nodeId, "PointIndex");
            BlueprintTestHelper.SetPinDefault(ctx, a1, loc.nodeId, "CoordinateSpace", "World");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, loc.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Spline_Loc_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Spline_Tan_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            BlueprintTestHelper.AddComponent(ctx, a2, "Spline", "SplineComponent");
            var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Tan",
                new[] { new { name = "i", type = "int" } },
                outputs: new[] { new { name = "ReturnValue", type = "vector" } });
            var getSpline = BlueprintTestHelper.AddNode(ctx, a2, "Tan", "Spline", 100, 0, kind: "getter");
            var tan = BlueprintTestHelper.AddNode(ctx, a2, "Tan", "GetTangentAtSplinePoint", 300, 0,
                "SplineComponent");
            BlueprintTestHelper.Connect(ctx, a2, getSpline.nodeId, "Spline", tan.nodeId, "__target");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "i", tan.nodeId, "PointIndex");
            BlueprintTestHelper.SetPinDefault(ctx, a2, tan.nodeId, "CoordinateSpace", "World");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, tan.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Spline_Tan_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Spline_Xform_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            BlueprintTestHelper.AddComponent(ctx, a3, "Spline", "SplineComponent");
            var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Xf",
                new[] { new { name = "i", type = "int" } },
                outputs: new[] { new { name = "ReturnValue", type = "Transform" } });
            var getSpline = BlueprintTestHelper.AddNode(ctx, a3, "Xf", "Spline", 100, 0, kind: "getter");
            var xf = BlueprintTestHelper.AddNode(ctx, a3, "Xf", "GetTransformAtSplinePoint", 300, 0,
                "SplineComponent");
            BlueprintTestHelper.Connect(ctx, a3, getSpline.nodeId, "Spline", xf.nodeId, "__target");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "i", xf.nodeId, "PointIndex");
            BlueprintTestHelper.SetPinDefault(ctx, a3, xf.nodeId, "CoordinateSpace", "World");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
            BlueprintTestHelper.Connect(ctx, a3, xf.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Spline_Xform_B");
    }
}