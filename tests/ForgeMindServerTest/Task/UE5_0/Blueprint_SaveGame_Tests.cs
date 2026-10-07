// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 13 · Blueprint SaveGame (UE 5.0)  — from Tasks/UE_5.0/Blueprint-SaveGame.md
// Probe (Spec Point 6): already supports create_blueprint's parentClass; GameplayStatics
// class auto U-prefix resolution (CreateSaveGameObject / SaveGameToSlot /
// LoadGameFromSlot / DoesSaveGameExist real names); Cast node; object/class pin
// default values loaded by asset path; create_function_graph outputs.
// Gaps (written assuming already supported): ① DSL class-qualified call + typeof class argument
// `UGameplayStatics.CreateSaveGameObject(typeof(BP_Save))` (NewObject family docs
// legacy form fictional — UE blueprint real name is CreateSaveGameObject, see Save_Create_A;
// 2026/10/01 15:00 replaced per user instruction); ② DSL member assignment `s.Score = 10;`;
// ③ DSL class-qualified call `UGameplayStatics.Xxx(...)`; ④ DSL `as BP_Save` conversion.
// ⑤ A-side setter/getter for external object variable access already implemented (2026-09-30):
// add_blueprint_node's getter/setter supports 'class' field (variable owning class name, e.g., BP_Save) —
// bridge via ConfigureVarNode → SetExternalMember creates non-self-context variable node, engine auto-generates
// Target input pin (real name self), hit by existing "__target" wire parsing.
// =========================================================================

public sealed class Blueprint_SaveGame : ITestCase
{
    public string Name => "Blueprint SaveGame (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 13;

    public async Task Run(TestContext ctx)
    {
        // Prerequisite: SaveGame class BP_Save (variable Score : int). GroupLifecycle clears
        // Content/Tests on every run, so recreated each time.
        const string bpSave = "/Game/Tests/UE5_0/BP_Save";
        var createSave = BlueprintTestHelper.EditorCall(ctx, "create_blueprint",
            new { path = bpSave, parentClass = "SaveGame" });
        ctx.AssertTrue(createSave["response"]?["created"]?.GetValue<bool>() ?? false,
            $"create_blueprint {bpSave}: {createSave["response"]?["reason"]?.GetValue<string>() ?? ""}");
        BlueprintTestHelper.AddVariable(ctx, bpSave, "Score", "int", defaultValue: "0");
        BlueprintTestHelper.CompileAndSave(ctx, bpSave);

        // ── Sub-task 1: Create and write save ──
        const string a1 = "/Game/Tests/UE5_0/Save_Create_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "Write");
        var create1 = BlueprintTestHelper.AddNode(ctx, a1, "Write", "CreateSaveGameObject", 200, 0, "GameplayStatics");
        BlueprintTestHelper.SetPinDefault(ctx, a1, create1.nodeId, "SaveGameClass", "/Game/Tests/UE5_0/BP_Save.BP_Save_C");
        var setScore1 = BlueprintTestHelper.AddNode(ctx, a1, "Write", "Score", 450, 0, "BP_Save", kind: "setter");
        BlueprintTestHelper.SetPinDefault(ctx, a1, setScore1.nodeId, "Score", "10");
        BlueprintTestHelper.Connect(ctx, a1, create1.nodeId, "ReturnValue", setScore1.nodeId, "__target");
        var save1 = BlueprintTestHelper.AddNode(ctx, a1, "Write", "SaveGameToSlot", 700, 0, "GameplayStatics");
        BlueprintTestHelper.SetPinDefault(ctx, a1, save1.nodeId, "SlotName", "S1");
        BlueprintTestHelper.SetPinDefault(ctx, a1, save1.nodeId, "UserIndex", "0");
        BlueprintTestHelper.Connect(ctx, a1, create1.nodeId, "ReturnValue", save1.nodeId, "SaveGameObject");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", create1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, create1.nodeId, "then", setScore1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, setScore1.nodeId, "then", save1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, save1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // BP_Save asset already created by MCP prerequisite step (create mode reports
        // ASSET_ALREADY_EXISTS for existing assets; IR rule §6.5 prevents overwrite) — DSL only
        // issues consumer blueprints; BP_Save referenced by type name (resolution lands on bridge editor side).
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Save_Create_B");

        // ── Sub-task 2: Load save ──
        const string a2 = "/Game/Tests/UE5_0/Save_Load_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Read");
        var load2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "LoadGameFromSlot", 200, 0, "GameplayStatics");
        BlueprintTestHelper.SetPinDefault(ctx, a2, load2.nodeId, "SlotName", "S1");
        BlueprintTestHelper.SetPinDefault(ctx, a2, load2.nodeId, "UserIndex", "0");
        var cast2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "Cast<BP_Save>", 450, 0);
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", load2.nodeId, "execute");
        // Load → Cast execution chain (task docs "LoadGameFromSlot → Cast to BP_Save"
        // → chain has execution flow; previously missed wire, Cast's exec-in left hanging;
        // compile-time "node never executed" warning silently swallowed by CompileAndSave —
        // user visually "LoadGameFromSlot and Cast<BP_Save> have no link" = this issue)
        BlueprintTestHelper.Connect(ctx, a2, load2.nodeId, "then", cast2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, load2.nodeId, "ReturnValue", cast2.nodeId, "Object");
        var getScore2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "Score", 700, 100, "BP_Save", kind: "getter");
        BlueprintTestHelper.Connect(ctx, a2, cast2.nodeId, "ReturnValue", getScore2.nodeId, "__target");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "Read", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, cast2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, getScore2.nodeId, "Score", p2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, p2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Save_Load_B");

        // ── Sub-task 3: Check save exists ──
        const string a3 = "/Game/Tests/UE5_0/Save_Has_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Has",
            Array.Empty<object>(), outputs: new[] { new { name = "ReturnValue", type = "bool" } });
        var has3 = BlueprintTestHelper.AddNode(ctx, a3, "Has", "DoesSaveGameExist", 200, 0, "GameplayStatics");
        BlueprintTestHelper.SetPinDefault(ctx, a3, has3.nodeId, "SlotName", "S1");
        BlueprintTestHelper.SetPinDefault(ctx, a3, has3.nodeId, "UserIndex", "0");
        // Has → result execution chain (task docs "DoesSaveGameExist → return" → chain has execution flow;
        // previously entry direct to result, bypassing has3 — same family as Save_Load_A's Load→Cast missed wire;
        // user visually "DoesSaveGameExist in Save_Has_A/B is not linked to the function header" = this issue)
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", has3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, has3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, has3.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Save_Has_B");

        await Task.CompletedTask;
    }
}
