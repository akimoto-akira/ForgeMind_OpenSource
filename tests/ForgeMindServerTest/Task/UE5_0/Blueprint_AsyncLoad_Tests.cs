// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 63 - Blueprint Async Load (UE 5.0) - from Tasks/UE_5.0/Blueprint_AsyncLoad.md
//
// Probe (collaboration spec point 6):
// - Soft reference variables are a supported bridge variable type: "Soft Object Reference"
//   / "Soft Class Reference" are the editor's own type texts (EdGraphSchema_K2.cpp type-tree
//   labels; ForgeMindBlueprintUtils.cpp BaseTypeTextToPinType softobject / softclass branch),
//   already exercised by Blueprint_SoftRef (Order 40).
// - **The doc's node names have no 5.0.3 counterpart** — there is no UK2Node_AsyncAction
//   "Async Load Asset" / "Async Load Class" and no UStreamableManager blueprint surface
//   (StreamableManager.h:485-504 is C++ only). What 5.0.3 really exposes in KismetSystemLibrary:
//     LoadAsset(WorldContextObject, TSoftObjectPtr<UObject> Asset, FOnAssetLoaded OnLoaded,
//               FLatentActionInfo LatentInfo)  — the latent "Load Asset" node (:320)
//     LoadAsset_Blocking(TSoftObjectPtr<UObject> Asset) : UObject*      (:281)
//     LoadClassAsset_Blocking(TSoftClassPtr<UObject> AssetClass) : UClass* (:301)
//   All three subtasks use the blocking variants. The latent form was wired in subtask 1's
//   first revision (LoadAsset + CreateDelegate -> OnLoaded custom event) but placed in the
//   "Load" FUNCTION graph — wrong placement: latent call nodes are asynchronous entry
//   points (KismetCompilerMisc.cpp FEventGraphUtils::IsEntryPointNode lists them next to
//   Event/Timeline) and the world's latent action manager only exists on the event graph.
//   User verdict 2026-10-21 (editor inspection): the OnLoaded form can only be used in the
//   event graph, so the A side was switched to the same LoadAsset_Blocking shape as the B side.
// - FOnAssetLoaded is a one-param dynamic delegate (UObject* Loaded), so the custom event
//   takes a single Object parameter — the doc's OnLoaded(UObject o) shape is engine-correct
//   and remains the reference form for an event-graph wiring (not built by this case).
// - DSL side (narrowed 2026-10-21 per spec §6 "verify first, then write"): `AsyncLoad(...)`,
//   `StreamableManager.LoadAsset(...)` and `AsyncLoadClass(...)` are invented verbs — SpkTool
//   resolve reports NoDeclaration for all three, so they are no longer written. The B side uses
//   only the verified accessors `LoadAsset_Blocking` / `LoadClassAsset_Blocking` (both
//   UKismetSystemLibrary, resolve → Resolved).
//   Intent narrowing: the latent `LoadAsset` node needs an FOnLoaded delegate plus a
//   FLatentActionInfo, and the translator has no general "wrap this.CustomEvent into a delegate"
//   lowering (only the timer aliases do that). So the async-completion shape the doc asked for is
//   not expressible today; the B side covers the two blocking loads and consumes the returned
//   object, and the A side mirrors the same blocking shape (see the latent-placement note above).
//   The gap is recorded here rather than papered over with an invented verb.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison).
// =========================================================================

public sealed class Blueprint_AsyncLoad : ITestCase
{
    public string Name => "Blueprint Async Load (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 63;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: load asset (soft object reference -> object) --
        // MCP steps: Async_Load_A (Extends Actor) + SoftRef : Soft Object Reference
        //   function Load: LoadAsset_Blocking(SoftRef) -> GetDisplayName -> PrintString
        // Same shape as the B side's DSL and subtask 2 — see the latent-placement note in
        // the header: the OnLoaded (latent) form belongs to the event graph only.
        const string a1 = "/Game/Tests/UE5_0/Async_Load_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "SoftRef", "Soft Object Reference");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Load");
        var getRef1 = BlueprintTestHelper.AddNode(ctx, a1, "Load", "SoftRef", 100, 0, kind: "getter");
        var load1 = BlueprintTestHelper.AddNode(ctx, a1, "Load", "LoadAsset_Blocking", 300, 0,
            "KismetSystemLibrary");
        var name1 = BlueprintTestHelper.AddNode(ctx, a1, "Load", "GetDisplayName", 500, -100,
            "KismetSystemLibrary");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "Load", "PrintString", 700, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, getRef1.nodeId, "SoftRef", load1.nodeId, "Asset");
        BlueprintTestHelper.Connect(ctx, a1, load1.nodeId, "ReturnValue", name1.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a1, name1.nodeId, "Return Value", print1.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", load1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, load1.nodeId, "then", print1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, print1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Async_Load_B");

        // -- Subtask 2: load asset (soft object reference -> object) --
        // MCP steps: Async_Mgr_A (Extends Actor) + SoftRef : Soft Object Reference
        //   function Load: LoadAsset_Blocking(SoftRef) -> GetDisplayName -> PrintString
        const string a2 = "/Game/Tests/UE5_0/Async_Mgr_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "SoftRef", "Soft Object Reference");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Load");
        var getRef2 = BlueprintTestHelper.AddNode(ctx, a2, "Load", "SoftRef", 100, 0, kind: "getter");
        var load2 = BlueprintTestHelper.AddNode(ctx, a2, "Load", "LoadAsset_Blocking", 300, 0,
            "KismetSystemLibrary");
        var name2 = BlueprintTestHelper.AddNode(ctx, a2, "Load", "GetDisplayName", 500, -100,
            "KismetSystemLibrary");
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "Load", "PrintString", 700, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getRef2.nodeId, "SoftRef", load2.nodeId, "Asset");
        BlueprintTestHelper.Connect(ctx, a2, load2.nodeId, "ReturnValue", name2.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a2, name2.nodeId, "Return Value", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", load2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, load2.nodeId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Async_Mgr_B");

        // -- Subtask 3: load class asset (soft class reference -> class) --
        // MCP steps: Async_Class_A (Extends Actor) + SoftCls : Soft Class Reference
        //   function Load: LoadClassAsset_Blocking(SoftCls) -> GetClassDisplayName -> PrintString
        // GetClassDisplayName (not GetDisplayName): the blocking class load returns
        // UClass* — a class reference pin, which the K2 schema refuses to wire into
        // GetDisplayName's const UObject* object pin (no autocast, no cast path;
        // verified in run 20261021_1344 subtask 3: "Return Value" is not compatible with "Object").
        // UKismetSystemLibrary::GetClassDisplayName(UClass*) @KismetSystemLibrary.h:120
        // takes the class reference directly (SpkTool-verified before writing).
        const string a3 = "/Game/Tests/UE5_0/Async_Class_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "SoftCls", "Soft Class Reference");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Load");
        var getCls3 = BlueprintTestHelper.AddNode(ctx, a3, "Load", "SoftCls", 100, 0, kind: "getter");
        var load3 = BlueprintTestHelper.AddNode(ctx, a3, "Load", "LoadClassAsset_Blocking", 300, 0,
            "KismetSystemLibrary");
        var name3 = BlueprintTestHelper.AddNode(ctx, a3, "Load", "GetClassDisplayName", 500, -100,
            "KismetSystemLibrary");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "Load", "PrintString", 700, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getCls3.nodeId, "SoftCls", load3.nodeId, "AssetClass");
        BlueprintTestHelper.Connect(ctx, a3, load3.nodeId, "ReturnValue", name3.nodeId, "Class");
        BlueprintTestHelper.Connect(ctx, a3, name3.nodeId, "Return Value", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", load3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, load3.nodeId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, print3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Async_Class_B");

        await Task.CompletedTask;
    }
}
