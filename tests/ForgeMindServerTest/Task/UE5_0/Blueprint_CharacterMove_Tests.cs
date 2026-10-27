// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 67 - Blueprint Character Movement (UE 5.0) - from Tasks/UE_5.0/Blueprint_CharacterMove.md
//
// Probe (collaboration spec point 6):
// - ACharacter parent: create_blueprint's parentClass resolves "Character" through
//   ForgeMindFindFirstObject / blueprint-class resolution (same chain Blueprint_Spawn uses for
//   "Actor"), so all three assets extend Character.
// - Real UFUNCTION names (engine index 5.0, verified with ForgeMind.SpkTool):
//     APawn::AddMovementInput(FVector WorldDirection, float ScaleValue, bool bForce)  @Pawn.h:450
//     ACharacter::LaunchCharacter(FVector LaunchVelocity, bool bXYOverride, bool bZOverride)
//       @Character.h:701
//   Both are self members, so the MCP side passes no 'class' (skeleton-class lookup covers the
//   whole parent chain).
// - **GetCharacterMovement is not a UFUNCTION in 5.0.3**: Character.h:369 is a plain FORCEINLINE
//   getter (no UFUNCTION, no declaration site in the index). What the blueprint can reach is the
//   UPROPERTY itself — Character.h:231 declares
//   `UPROPERTY(Category=Character, VisibleAnywhere, BlueprintReadOnly, meta=(AllowPrivateAccess="true"))`
//   TObjectPtr<UCharacterMovementComponent> CharacterMovement — i.e. a component-style getter
//   node named "CharacterMovement". Subtask 3 therefore reads that property, then writes
//   UCharacterMovementComponent::MaxWalkSpeed (EditAnywhere, BlueprintReadWrite) through a
//   setter node with "__target" wired to it (the external-member form Blueprint_Particle /
//   Blueprint_ConstructionScript use for component calls).
// - DSL side (verified 2026-10-21 per spec §6 "verify first, then write"): the doc's
//   `GetCharacterMovement()` is not writable — it is a plain FORCEINLINE with no UFUNCTION
//   (SpkTool resolve: NoDeclaration). The reachable, engine-declared name is the UPROPERTY
//   itself: `CharacterMovement`. So the B side writes
//   `CharacterMovement.MaxWalkSpeed = 600.0f`, which mirrors the A-side node pair
//   (getter "CharacterMovement" -> setter "MaxWalkSpeed" with __target). Both names are real
//   UPROPERTYs (Character.h:231 read, CharacterMovementComponent.h:235 BlueprintReadWrite).
//   If the member property-set lowering is still missing, that surfaces as a behaviour FAIL —
//   which is the legitimate way this case pins the gap; the names themselves are no longer
//   invented placeholders.
// - Receiver type (user correction 2026-10-22): `CharacterMovement` must **not** be re-declared —
//   ACharacter already owns that component, so declaring it in the DSL was redundant. The
//   translator now resolves the receiver through the unified codebase channel
//   (IScanDataProvider.CollectProperties, inheritance included) to obtain
//   UCharacterMovementComponent as the setter's owner class. That requires UPROPERTY data in the
//   index: the scanner writes it (UnrealScanner → ScanData.PropertyMap) and the pack reader
//   decodes it (ScanPackReader.DtoClass.Properties). A pack without property capture leaves
//   CollectProperties empty → the member write falls back to the FM1101 degradation (unknown is
//   never guessed, spec §8).
// - Graph name (user decision 2026-10-22): the doc's `Jump` is **not usable** — it collides with
//   ACharacter::Jump() (Character.h:631, a BlueprintCallable UFUNCTION), and the translator now
//   detects that through the unified codebase query channel instead of silently emitting a
//   shadowing graph:
//   `ERROR FM1002 Function 'Jump' collides with the inherited engine function 'Jump' on parent
//   'Character'` (confirmed live in TestRuns/20261022_1321). The graph is named `DoJump` on both
//   sides; the intent (launch the character with LaunchCharacter) is unchanged.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison). The a3 baseline is taken after the final
// CompileAndSave only — the intermediate save right after CreateBlueprint is an unfinished
// graph, so snapshotting there would seed a baseline the completed graph could never match.
// =========================================================================

public sealed class Blueprint_CharacterMove : ITestCase
{
    public string Name => "Blueprint Character Movement (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 67;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: AddMovementInput --
        // MCP steps: Move_Input_A (Extends Character) + function Go(Vector dir)
        //   -> AddMovementInput(dir, ScaleValue = 1.0)
        const string a1 = "/Game/Tests/UE5_0/Move_Input_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a1, "Character");
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Go",
            new[] { new { name = "dir", type = "vector" } });
        // Self member call (APawn::AddMovementInput) — no 'class' needed.
        var move1 = BlueprintTestHelper.AddNode(ctx, a1, "Go", "AddMovementInput", 300, 0);
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "dir", move1.nodeId, "WorldDirection");
        BlueprintTestHelper.SetPinDefault(ctx, a1, move1.nodeId, "ScaleValue", "1.0");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", move1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, move1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Move_Input_B");

        // -- Subtask 2: LaunchCharacter --
        // MCP steps: Move_Launch_A (Extends Character) + function DoJump
        //   -> LaunchCharacter(LaunchVelocity = 0,0,500, bXYOverride, bZOverride=true)
        // Named DoJump, not Jump: `Jump` shadows ACharacter::Jump() and is rejected with FM1002.
        const string a2 = "/Game/Tests/UE5_0/Move_Launch_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a2, "Character");
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "DoJump");
        var launch2 = BlueprintTestHelper.AddNode(ctx, a2, "DoJump", "LaunchCharacter", 300, 0);
        // FVector pin default is the engine's own import text ("X, Y, Z").
        BlueprintTestHelper.SetPinDefault(ctx, a2, launch2.nodeId, "LaunchVelocity", "0, 0, 500");
        BlueprintTestHelper.SetPinDefault(ctx, a2, launch2.nodeId, "bXYOverride", "true");
        BlueprintTestHelper.SetPinDefault(ctx, a2, launch2.nodeId, "bZOverride", "true");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", launch2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, launch2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Move_Launch_B");

        // -- Subtask 3: set the character movement component's max walk speed --
        // MCP steps: Move_Speed_A (Extends Character) + function SetSpeed
        //   -> Get CharacterMovement -> MaxWalkSpeed = 600
        const string a3 = "/Game/Tests/UE5_0/Move_Speed_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a3, "Character");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "SetSpeed");
        // CharacterMovement is a native BlueprintReadOnly property (see file header): read it
        // with a getter node — no GetCharacterMovement UFUNCTION exists in 5.0.3.
        var getCm3 = BlueprintTestHelper.AddNode(ctx, a3, "SetSpeed", "CharacterMovement", 100, 0,
            kind: "getter");
        // MaxWalkSpeed lives on UCharacterMovementComponent, not on the Character blueprint, so the
        // setter must name its owner class (the bridge's own guidance: "for another object's member
        // pass 'class' = its owner class name") — otherwise add_blueprint_node reports
        // "No variable named 'MaxWalkSpeed' on this blueprint or its parent classes".
        var setSpeed3 = BlueprintTestHelper.AddNode(ctx, a3, "SetSpeed", "MaxWalkSpeed", 300, 0,
            "CharacterMovementComponent", kind: "setter");
        BlueprintTestHelper.Connect(ctx, a3, getCm3.nodeId, "CharacterMovement", setSpeed3.nodeId,
            "__target");
        BlueprintTestHelper.SetPinDefault(ctx, a3, setSpeed3.nodeId, "MaxWalkSpeed", "600.0");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", setSpeed3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, setSpeed3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Move_Speed_B");

        await Task.CompletedTask;
    }
}
