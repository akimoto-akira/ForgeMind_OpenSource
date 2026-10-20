// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 41 - Blueprint Line Trace (UE 5.0) - from Tasks/UE_5.0/blueprint-ray.md
//
// Probe (collaboration spec point 6):
// - No LineTrace support anywhere: the bridge has no UK2Node_LineTraceByChannel
//   handling, and neither the bridge nor the DSL translator references
//   LineTraceSingle / LineTraceMulti / LineTraceByChannel (full-tree search, 0 hits).
// - A-side expresses the editor's "Line Trace By Channel" node through its engine-real
//   function UKismetSystemLibrary::LineTraceSingle (ownerClass function-call path, same
//   as other ownerClass call nodes; its engine true name / DisplayName meta verified in
//   UE 5.0.3 KismetSystemLibrary.h (DisplayName="Line Trace By Channel" — which is why the
//   doc's bare 'LineTrace' fell back to an unrelated GeoReferencing 'LineTrace').
//   ETraceTypeQuery enumerators are named TraceTypeQuery1..N (UE 5.0.3 EngineTypes.h) and
//   are all UMETA(Hidden): their DisplayName comes from the PROJECT's collision settings —
//   in this project TraceTypeQuery1 displays as "Visibility" (project assumption, not an
//   engine constant).
//   Task 2 uses LineTraceMulti (OutHits array). Task 3 breaks the hit through the engine's
//   native break function node GameplayStatics::BreakHitResult (NOT a generic BreakStruct —
//   FHitResult carries HasNativeBreak meta, see the task 3 comment below).
//   A-side confirmed working (run 20261011_2348: task 1 A-side create/create_function_graph
//   all passed; run 20261012_0226: tasks 1 and 2 A-side + B-side all passed).
// - Bridge review (2026-10-12, run 20261011_2348): doc draft DSL `LineTrace(...,
//   ECC_Visibility, h)` failed on two form issues:
//   1) bare name "LineTrace" is no KismetSystemLibrary function - the scan-index loose
//      fallback resolved it to GeoReferencingEditorBPLibrary (a real but wrong "LineTrace"
//      match; editor class not loadable -> BP_NODE_NOT_FOUND);
//   2) bare `ECC_Visibility` was emitted as VariableGet (no enum-literal recognition).
//   The call shape itself (entry pins -> named pins, trailing out arg h -> OutHit pin,
//   ReturnValue -> PrintString) was structurally assembled - only the two forms failed.
// - Per the verified DSL grammar (DSL-syntax.md §2.9 actual function names / §4.7.1 dotted
//   enum literals) and the LevelStreaming colloquialism precedent, B-side DSL is corrected:
//   LineTrace -> LineTraceSingle (engine real name, A-side-proven host), ECC_Visibility ->
//   ETraceTypeQuery.TraceTypeQuery1 (dotted literal, EEasingFunc.ExpoOut precedent),
//   h.Actor.GetName() -> GetDisplayName(h.HitActor) (engine real member HitActor;
//   GetDisplayName verified in BindInstanceEvent B-side).
// - Implemented (2026/10/12): the call-site `out h` form. The translator skips out/ref
//   arguments in positional mapping and resolves the OUT parameter pin from the scan
//   index's IsOut flag (derived from the C++ declaration: non-const reference "T&",
//   verified against UE 5.0.3 KismetSystemLibrary.h), then wires
//   "call node out pin -> variable write" (direction matters: the reverse produced the
//   h -> bTraceComplex BP_PIN_TYPE_MISMATCH). Missing middle params (bTraceComplex /
//   ActorsToIgnore / DrawDebugType) keep pin defaults (LoadStreamLevel 3-of-4-args
//   precedent; ActorsToIgnore is AutoCreateRefTerm so it stays valid unwired).
//   NOTE: activation requires a rebuilt engine.spk — the index must carry IsOut, which
//   older packs lack (defaults to false / input pin).
// - Closed since the probe (for the record): engine enum literals now land as pin
//   defaults (2026-10-12: ETraceTypeQuery.TraceTypeQuery1 -> TraceChannel default,
//   no EnumLiteral node); member reads on HasNativeBreak structs lower to the native
//   break function (2026-10-12 11:25: h.HitActor -> GameplayStatics::BreakHitResult's
//   HitActor output pin, see NativeBreakFunctions in the translator); call conditions
//   on the if path now build the call node and wire its ReturnValue into the Branch
//   condition (2026-10-12 17:xx, user visual feedback: Trace_Hit_B had no
//   LineTraceSingle node because TryConnectBranchCondition had no invocation case).
// - A-side tasks 1/2 now print the trace's boolean return (2026-10-12, user visual
//   feedback "no PrintString for the collision bool, reference the same-source B side"):
//   trace.ReturnValue -> PrintString.InString (bool -> string via Conv_BoolToString,
//   BlueprintAutocast meta), with the exec chain running through PrintString.
//
// Note: mis-touch comparison (VerifyIrSnapshot) inserted per user instruction
// (collaboration spec point 9), covering all A-side and B-side assets.
// =========================================================================

public sealed class Blueprint_LineTrace : ITestCase
{
    public string Name => "Blueprint Line Trace (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 41;

    public async Task Run(TestContext ctx)
    {
        // -- Task 1: LineTraceByChannel --
        // MCP steps: Trace_Single_A (Extends Actor) + function Shoot(Start, End)
        //   -> LineTraceSingle (TraceChannel = Visibility), OutHit output exists per doc.
        const string a1 = "/Game/Tests/UE5_0/Trace_Single_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Shoot",
            new[] { new { name = "Start", type = "vector" }, new { name = "End", type = "vector" } });
        var trace1 = BlueprintTestHelper.AddNode(ctx, a1, "Shoot", "LineTraceSingle", 300, 0,
            "KismetSystemLibrary");
        // TraceTypeQuery1 = this project's first trace channel (displays as "Visibility");
        // enumerator naming verified in UE 5.0.3 EngineTypes.h (entries are UMETA(Hidden),
        // DisplayName comes from project collision settings)
        BlueprintTestHelper.SetPinDefault(ctx, a1, trace1.nodeId, "TraceChannel", "TraceTypeQuery1");
        // Print the trace's boolean return value (same-source B-side: PrintString(b);
        // bool -> string auto-converts through Conv_BoolToString, which carries the
        // BlueprintAutocast meta - KismetStringLibrary.h)
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "Shoot", "PrintString", 550, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "Start", trace1.nodeId, "Start");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "End", trace1.nodeId, "End");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", trace1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, trace1.nodeId, "ReturnValue", p1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, trace1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, p1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (doc DSL corrected per verified grammar: engine real name
        // LineTraceSingle + dotted ETraceTypeQuery literal + call-site `out h`.
        // The out argument resolves to the OUT parameter pin ("OutHit") from the scan
        // index's IsOut flag, so it is NOT mapped positionally onto bTraceComplex.)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Trace_Single_B");

        // -- Task 2: LineTraceMultiByChannel --
        // MCP steps: Trace_Multi_A + function Shoot(Start, End)
        //   -> LineTraceMulti (TraceChannel = Visibility), OutHits (array) output exists per doc.
        const string a2 = "/Game/Tests/UE5_0/Trace_Multi_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Shoot",
            new[] { new { name = "Start", type = "vector" }, new { name = "End", type = "vector" } });
        var trace2 = BlueprintTestHelper.AddNode(ctx, a2, "Shoot", "LineTraceMulti", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, trace2.nodeId, "TraceChannel", "TraceTypeQuery1");
        // Print the trace's boolean return value (same-source B-side: PrintString(b);
        // bool -> string auto-converts through Conv_BoolToString, BlueprintAutocast)
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Shoot", "PrintString", 550, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "Start", trace2.nodeId, "Start");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "End", trace2.nodeId, "End");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", trace2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, trace2.nodeId, "ReturnValue", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, trace2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (same corrections; out argument targets the OUT parameter "OutHits")
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Trace_Multi_B");

        // -- Task 3: Hit result handling --
        // MCP steps: Trace_Hit_A + function Shoot(Start, End)
        //   -> LineTraceSingle, Break Hit Result, HitActor -> PrintString.
        //
        // Engine review (2026/10/12, run 20261012_0226): the generic BreakStruct node carries
        // NO member pins for HitResult — FHitResult is declared with
        // meta=(HasNativeBreak="Engine.GameplayStatics.BreakHitResult") (EngineTypes.h:2204),
        // so the editor's "Break Hit Result" node is that native function node, not a struct
        // operation. Its inputs/outputs are the C++ parameter names: Hit (in), then
        // bBlockingHit / bInitialOverlap / Time / Distance / Location / ImpactPoint / Normal /
        // ImpactNormal / PhysMat / HitActor / HitComponent / HitBoneName / BoneName / HitItem /
        // ElementIndex / FaceIndex / TraceStart / TraceEnd (GameplayStatics.h:1000).
        // A generic BreakStruct(HitResult) reported "has no pin named 'HitActor'" and must not
        // be used here.
        const string a3 = "/Game/Tests/UE5_0/Trace_Hit_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Shoot",
            new[] { new { name = "Start", type = "vector" }, new { name = "End", type = "vector" } });
        var trace3 = BlueprintTestHelper.AddNode(ctx, a3, "Shoot", "LineTraceSingle", 300, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, trace3.nodeId, "TraceChannel", "TraceTypeQuery1");
        // "Break Hit Result" = the native break function node (BlueprintPure, no exec pins)
        var brk3 = BlueprintTestHelper.AddNode(ctx, a3, "Shoot", "BreakHitResult", 550, 0,
            "GameplayStatics");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "Shoot", "PrintString", 800, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, trace3.nodeId, "OutHit", brk3.nodeId, "Hit");
        BlueprintTestHelper.Connect(ctx, a3, brk3.nodeId, "HitActor", p3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "Start", trace3.nodeId, "Start");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "End", trace3.nodeId, "End");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", trace3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, trace3.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, p3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (doc DSL corrected per verified grammar: engine real name + dotted
        // enum literal + verified GetDisplayName printer. `if (call(out h))` also exercises
        // call-condition + out wiring on the branch-condition path - both lowered since
        // the original probe: the condition builds the LineTraceSingle node (ReturnValue
        // -> Branch.Condition) and `out h` wires OutHit -> the variable write.
        // `h.HitActor` lowers through the native break function (FHitResult carries
        // HasNativeBreak="Engine.GameplayStatics.BreakHitResult"; the generic BreakStruct
        // has no member pins for this struct, so the translator routes it to that native
        // function's HitActor output - see NativeBreakFunctions in BlueprintTranslator).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Trace_Hit_B");

        await Task.CompletedTask;
    }
}
