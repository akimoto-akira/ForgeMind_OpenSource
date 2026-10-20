// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 60 - Blueprint Camera (UE 5.0) - from Tasks/UE_5.0/blueprint-camera.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit.
// GetPlayerController (UGameplayStatics::GetPlayerController, GameplayStatics.h L189)
// and GetPlayerCameraManager (GameplayStatics.h L216) are BlueprintPure -- no exec
// pins, only data pins (self / WorldContextObject / PlayerIndex in / ReturnValue out).
// The A-side exec chain therefore skips them and their ReturnValue wires straight
// into the target node's __target pin. SetViewTargetWithBlend (PlayerController.h
// L1563) and ClientStartCameraShake (PlayerController.h L995) are BlueprintCallable
// with exec pins; the call node's execute/then are on the exec chain.
// SetViewTargetWithBlend's NewViewTarget pin is AActor* -- the blueprint's own self
// node is wired there so the PlayerCamera uses the actor's default CameraComponent
// (see run 20261019_0105 where passing the Camera getter (CameraComponent) was
// rejected as "CameraComponent object reference and Actor object reference are incompatible").
// ShakeAsset is a CameraShakeBase variable; the DSL passes an instance to
// ClientStartCameraShake (which expects a TSubclassOf) -- written as-is per
// "supported" assumption; the test is spec. The first data pin's real name on
// ClientStartCameraShake is "Shake" (from the C++ parameter name
// `TSubclassOf<UCameraShakeBase> Shake` at PlayerController.h L995), not
// "ShakeClass" (that spelling was a test-side mis-transcription; bridge
// rejects with "has no pin named 'ShakeClass'", run 20261019_1056).
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - SetViewTargetWithBlend (self, BlendTime=1.0)
//   Task 2 - GetPlayerCameraManager().GetCameraLocation() -> PrintString
//   Task 3 - ClientStartCameraShake(ShakeAsset)
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-19 per user instruction,
// after this case was confirmed by visual inspection. One per asset -- A side after
// CompileAndSave, DSL side after ApplyDsl (6 total: Camera_View_A/B, Camera_Mgr_A/B,
// Camera_Shake_A/B). Same convention as Blueprint_Select_Tests / Blueprint_Rotation_Tests /
// Blueprint_ShapeTrace_Tests / Blueprint_Destroy_Tests / Blueprint_Vector_Tests.
// Baselines are seeded on the first run; from the second run onward they actually diff.
// =========================================================================

public sealed class Blueprint_Camera : ITestCase
{
    public string Name => "Blueprint Camera (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 60;

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
            const string a1 = "/Game/Tests/UE5_0/Camera_View_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            BlueprintTestHelper.AddComponent(ctx, a1, "Camera", "CameraComponent");
            var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Focus");
            // NewViewTarget is AActor* (PlayerController.h L1563), not a component pin --
            // wire this blueprint's own self reference so the PlayerCamera uses the actor's
            // default CameraComponent. Camera getter (CameraComponent) would be rejected by
            // the bridge as "component object reference and Actor object reference are not
            // compatible" (see run 20261019_0105).
            var selfNode = BlueprintTestHelper.AddNode(ctx, a1, "Focus", "self", 100, 150,
                kind: "self");
            var getPC = BlueprintTestHelper.AddNode(ctx, a1, "Focus", "GetPlayerController", 100, 0,
                "GameplayStatics");
            var setView = BlueprintTestHelper.AddNode(ctx, a1, "Focus", "SetViewTargetWithBlend", 400, 0,
                "PlayerController");
            BlueprintTestHelper.Connect(ctx, a1, getPC.nodeId, "ReturnValue", setView.nodeId, "__target");
            BlueprintTestHelper.Connect(ctx, a1, selfNode.nodeId, "self", setView.nodeId, "NewViewTarget");
            BlueprintTestHelper.SetPinDefault(ctx, a1, setView.nodeId, "BlendTime", "1.0");
            // GetPlayerController is BlueprintPure (GameplayStatics.h L189): no exec pins.
            // The exec chain enters setView directly; the controller reaches it as data
            // through the ReturnValue -> __target wire above. Chain "entry.then ->
            // getPC.execute" would be rejected with "has no pin named 'execute'"
            // (see run 20261019_0950).
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", setView.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a1, setView.nodeId, "then", f1.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison (spec point 9: inserted per user instruction)
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Camera_View_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Camera_Mgr_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Info");
            var getMgr = BlueprintTestHelper.AddNode(ctx, a2, "Info", "GetPlayerCameraManager", 100, 0,
                "GameplayStatics");
            var getLoc = BlueprintTestHelper.AddNode(ctx, a2, "Info", "GetCameraLocation", 300, 0,
                "PlayerCameraManager");
            var print = BlueprintTestHelper.AddNode(ctx, a2, "Info", "PrintString", 500, 0,
                "KismetSystemLibrary");
            BlueprintTestHelper.Connect(ctx, a2, getMgr.nodeId, "ReturnValue", getLoc.nodeId, "__target");
            BlueprintTestHelper.Connect(ctx, a2, getLoc.nodeId, "ReturnValue", print.nodeId, "InString");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", print.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a2, print.nodeId, "then", f2.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Camera_Mgr_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            const string a3 = "/Game/Tests/UE5_0/Camera_Shake_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            BlueprintTestHelper.AddVariable(ctx, a3, "ShakeAsset", "CameraShakeBase");
            var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Shake");
            var getShake = BlueprintTestHelper.AddNode(ctx, a3, "Shake", "ShakeAsset", 100, 150,
                kind: "getter");
            var getPC = BlueprintTestHelper.AddNode(ctx, a3, "Shake", "GetPlayerController", 100, 0,
                "GameplayStatics");
            var shake = BlueprintTestHelper.AddNode(ctx, a3, "Shake", "ClientStartCameraShake", 400, 0,
                "PlayerController");
            BlueprintTestHelper.Connect(ctx, a3, getPC.nodeId, "ReturnValue", shake.nodeId, "__target");
            BlueprintTestHelper.Connect(ctx, a3, getShake.nodeId, "ShakeAsset", shake.nodeId, "Shake");
            // GetPlayerController is BlueprintPure (GameplayStatics.h L189): no exec pins.
            // Skip it on the exec chain; the controller reaches the call node through the
            // ReturnValue -> __target wire above.
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", shake.nodeId, "execute");
            BlueprintTestHelper.Connect(ctx, a3, shake.nodeId, "then", f3.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Camera_Shake_B");
    }
}