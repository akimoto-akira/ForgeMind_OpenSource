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
//   :3947-3948). Whether the bridge's shared type parser (PinTypeTextToPinType) accepts
//   those two texts is **unverified** -- it documents base keywords + container forms +
//   UClass names only (ForgeMindBridgeClient_BlueprintVars.cpp:75-78) and has no
//   softobject/softclass branch. Written as the editor spells them; test is spec.
//   Pin *defaults* on object-ish pins are supported and include PC_SoftObject / PC_SoftClass
//   (BlueprintGraph.cpp:2999-3003), so once the type parses, an asset path default works.
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
//   declarations are copied verbatim; neither the generic soft-reference type text nor the
//   TryGet accessor is confirmed to be translatable -- test is spec.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transcribing task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
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

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: soft class reference variable --
        // MCP steps: SoftRef_Cls_A (Extends Actor) + Cls : Soft Class Reference (base Actor)
        const string a2 = "/Game/Tests/UE5_0/SoftRef_Cls_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddVariable(ctx, a2, "Cls", "Soft Class Reference");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: resolve a soft reference --
        // MCP steps: SoftRef_Resolve_A (Extends Actor) + Ref : Soft Object Reference
        //   Get() -> Resolve Soft Reference -> IsValid -> Branch -> PrintString(GetName)
        const string a3 = "/Game/Tests/UE5_0/SoftRef_Resolve_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Ref", "Soft Object Reference");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Get");
        // 5.0.3's resolve node (doc's "Try Get" has no counterpart -- see file header).
        var resolve3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "Conv_SoftObjectReferenceToObject",
            300, 0, "KismetSystemLibrary");
        var br3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "Branch", 600, 0);
        var name3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "GetName", 600, 150);
        var print3 = BlueprintTestHelper.AddNode(ctx, a3, "Get", "PrintString", 850, 150,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", resolve3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, resolve3.nodeId, "Return Value", br3.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a3, resolve3.nodeId, "then", br3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, resolve3.nodeId, "Return Value", name3.nodeId, "Target");
        BlueprintTestHelper.Connect(ctx, a3, name3.nodeId, "Return Value", print3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, br3.nodeId, "True", print3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, print3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}