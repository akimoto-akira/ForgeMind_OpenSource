// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 40 - Blueprint Soft Reference (UE 5.0) - from Tasks/UE_5.0/Blueprint-SoftReference.md
//
// Probe (collaboration spec point 6):
// - Soft object / soft class references are real blueprint variable types in 5.0.3:
//   FSoftObjectProperty / FSoftClassProperty (UnrealType.h:2537 / :2720) with K2 pin
//   categories PC_SoftObject / PC_SoftClass (EdGraphSchema_K2.cpp:686-687, property->pin
//   mapping :3361/:3366). The editor's type texts are **"Soft Object Reference"** and
//   **"Soft Class Reference"** (EdGraphSchema_K2.cpp:3705-3706, type-tree labels
//   :3947-3948). First run 20261011_1813 failed exactly here: the bridge's shared type
//   parser had no softobject/softclass branch -- now added (ForgeMindBlueprintUtils.cpp:
//   BaseTypeTextToPinType keywords + tsoftobjectptr<>/tsoftclassptr<> template forms in
//   PinTypeTextToPinType). Pin *defaults* on object-ish pins are supported and include
//   PC_SoftObject / PC_SoftClass (BlueprintGraph.cpp:2999-3003), so asset path defaults work.
// - **Doc's "Try Get" does not exist**: no TryGet / UKismetKismetLibrary /
//   UKismetAssetLibrary / LoadSynchronous anywhere in Runtime\Engine (LoadSynchronous is
//   only UStreamableManager's C++ method, StreamableManager.h:485-504). What 5.0.3 really
//   offers:
//     "Resolve Soft Reference" node -- UK2Node_ConvertAsset (K2Node_ConvertAsset.h, title at
//       K2Node_ConvertAsset.cpp:327), lowering to
//       UKismetSystemLibrary::Conv_SoftObjectReferenceToObject (KismetSystemLibrary.h:307);
//       class flavour: Conv_SoftClassReferenceToClass (:310)
//     UKismetSystemLibrary::LoadAsset_Blocking (:281-282, DeterminesOutputType="Asset") /
//       LoadClassAsset_Blocking (:301-302)
//     IsValidSoftObjectReference (:266) / IsValidSoftClassReference (:286), both BlueprintPure
//   subtask 3 therefore uses "Resolve Soft Reference" instead of the doc's `Ref.TryGet()`.
// - DSL side: `Ref.TryGet()` and `TSoftObjectPtr<Actor>` / `TSoftClassPtr<Actor>` member
//   declarations are copied verbatim. First run 20261011_1813 predates the translator
//   support: TryGet now lowers to Conv_SoftObjectReferenceToObject (pure, KismetSystemLibrary),
//   and `a != null` reuses the IsValid null-comparison form (local vars included).
// - Name resolution (user decision 2026/10/11): NO hardcoded aliases in translator or
//   validator -- function names must resolve through the scan index (the comprehensive
//   function lookup interface), otherwise FM1102 stands. The doc's `a.GetName()` was
//   therefore a DSL bug (AActor chain has no GetName UFUNCTION; earlier alias attempt
//   silently whitewashed it) -- corrected to the bare library call `GetDisplayName(a)`,
//   same engine function the MCP wiring uses.
//
// Note: "diff comparison" (VerifyIrSnapshot, mis-touch detection) inserted per user
// instruction 2026/10/11 after user visual confirmation of cases and results
// (collaboration spec point 9) -- covers all six assets (_A MCP path / _B DSL path).
// =========================================================================

public sealed class Blueprint_SoftRef : ITestCase
{
    public string Name => "Blueprint Soft Reference (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 40;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: soft object reference variable --
        // MCP steps: SoftRef_Obj_A (Extends Actor) + Ref : Soft Object Reference (base Actor)
        const string a1 = "/Game/Tests/UE5_0/SoftRef_Obj_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        // Type text is the editor's own ("Soft Object Reference", EdGraphSchema_K2.cpp:3705).
        // Pending: the bridge type parser may not know this text yet (see file header).
        BlueprintTestHelper.AddVariable(ctx, a1, "Ref", "Soft Object Reference");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/SoftRef_Obj_B");

        // -- Subtask 2: soft class reference variable --
        // MCP steps: SoftRef_Cls_A (Extends Actor) + Cls : Soft Class Reference (base Actor)
        const string a2 = "/Game/Tests/UE5_0/SoftRef_Cls_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "Cls", "Soft Class Reference");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/SoftRef_Cls_B");

        // -- Subtask 3: resolve a soft reference --
        // MCP steps: SoftRef_Resolve_A (Extends Actor) + Ref : Soft Object Reference
        //   Get() -> Resolve Soft Reference -> IsValid -> Branch -> PrintString(GetName)
        const string a3 = "/Game/Tests/UE5_0/SoftRef_Resolve_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Ref", "Soft Object Reference");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Get");
        // 5.0.3's resolve node (doc's "Try Get" has no counterpart -- see file header).
        // Conv_SoftObjectReferenceToObject is BlueprintPure (BlueprintInternalUseOnly,
        // KismetSystemLibrary.h:307) -- no exec pins; the "Resolve Soft Reference" editor
        // node (UK2Node_ConvertAsset) expands to exactly this call.
        var resolve3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "Conv_SoftObjectReferenceToObject",
            300, 0, "KismetSystemLibrary");
        var valid3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "IsValidSoftObjectReference",
            500, 0, "KismetSystemLibrary");
        var br3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "Branch", 700, 0);
        var name3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "GetDisplayName", 700, 150,
            "KismetSystemLibrary");
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "PrintString", 950, 150,
            "KismetSystemLibrary");
        // Data wires first (resolve/valid/name are pure); exec chain: entry -> Branch -> Print.
        // Input pins are real UFUNCTION parameter names (SoftObject / SoftObjectReference /
        // Object), outputs match the tested "Return Value" display-name convention.
        BlueprintTestHelper.Connect(ctx, a3, resolve3.nodeId, "Return Value", valid3.nodeId, "SoftObjectReference");
        BlueprintTestHelper.Connect(ctx, a3, resolve3.nodeId, "Return Value", name3.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a3, valid3.nodeId, "Return Value", br3.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a3, name3.nodeId, "Return Value", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", br3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, br3.nodeId, "then", print3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, print3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/SoftRef_Resolve_B");

        await Task.CompletedTask;
    }
}