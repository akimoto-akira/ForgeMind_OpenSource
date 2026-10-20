// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 34 - Blueprint Enum (UE 5.0) - from Tasks/UE_5.0/Blueprint-Enum.md
//
// Probe (collaboration spec point 6):
// - Enum asset creation goes through the bridge exactly like Blueprint_Switch_Tests'
//   EState_A (create_blueprint_enum { path } then add_enum_entry { path, name }).
//   Engine side (5.0.3): FEnumEditorUtils::CreateUserDefinedEnum
//   (EnumEditorUtils.h:40) -- there is **no** UUserDefinedEnum::CreateEnumFromTemplate;
//   a fresh user enum has **zero** entries (EnumEditorUtils.cpp:50-51) and each added
//   entry gets internal name NewEnumerator%u with a separate display name
//   (UserDefinedEnum.cpp:126-132, DisplayNameMap at UserDefinedEnum.h:34-39).
// - **Enum-typed variable / pin text is unverified**: the bridge's shared type parser
//   (PinTypeTextToPinType, used by add_blueprint_variable per BlueprintVars.cpp:75-82)
//   documents base keywords + container forms (X[] / set<X> / map<K,V>) + UClass names --
//   it has no UEnum branch. A user-defined enum is a UEnum, not a UClass, so declaring
//   `C : EColor` may be rejected until that branch exists. Written as the doc says; test
//   is spec.
// - Enum comparison: the engine has a dedicated enum node family --
//   UK2Node_EnumEquality / UK2Node_EnumInequality ("Equal (Enum)" / "!=", pins
//   A / B / Return Value) and UK2Node_EnumLiteral (entry lives in the "Enum"
//   INPUT pin's DefaultValue, output "Return Value"). Bridge supports all three
//   via the native-flow context-menu names EqualEnum / NotEqualEnum / EnumLiteral
//   (visual bug feedback 20261010: EqualEqual_ByteByte compiles the same but hides
//   the enum semantics). A/B are wildcards until a connection propagates the enum
//   type, so the MCP side connects the enum parameter to A BEFORE setting B's
//   entry default.
// - Enum array: TArray<TEnum> is supported (Array_Add, KismetArrayLibrary.h:29-30,
//   pins TargetArray / NewItem -> Return Value). Doc's DSL `Palette.Add(...)` is copied
//   verbatim; the MCP side uses the Array_Add node explicitly.
// - **DSL block shape (FM1001 fix, 20261010_1907)**: the transcription had [Blueprint] /
//   [Extends] attached to the `public enum EColor` declaration, which left the class block
//   without any domain attribute and aborted the whole batch with "FM1001 Declaration block
//   is missing a domain attribute". The enum is its own **[Enum] domain block** (same as
//   Blueprint_Switch_Tests' EState_B), declared **once** in subtask 1 and referenced by its
//   DSL name in subtasks 2/3 via the session-level enum alias that enum creation registers
//   (CreateEnumAsset -> RegisterUserTypeEnumAlias). A re-declaration per subtask would
//   re-create the same asset path, which the bridge refuses to clobber.
// - Enums are not saved by the DSL apply, so the [Enum] asset gets an explicit save_asset
//   (same as Blueprint_Switch_Tests / Blueprint_Struct_Tests).
//
// Note: "diff comparison" (VerifyIrSnapshot) inserted on user instruction per
// collaboration spec point 9 -- every asset in this file gets one after its
// build chain completes (enum assets included, same as Blueprint_Switch_Tests).
// =========================================================================

public sealed class Blueprint_Enum : ITestCase
{
    public string Name => "Blueprint Enum (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 34;

    /// <summary>
    /// Enum asset EColor (Red / Green / Blue), recreated per round because GroupLifecycle
    /// wipes Content/Tests. Enum assets have no compile concept, so save_asset is explicit
    /// (same as Blueprint_Switch_Tests' EState_A).
    /// </summary>
    private static void CreateColorEnum(TestContext ctx)
    {
        const string ecolor = "/Game/Tests/UE5_0/EColor_A";
        BlueprintTestHelper.EditorCall(ctx, "create_blueprint_enum", new { path = ecolor });
        foreach (var entry in new[] { "Red", "Green", "Blue" })
        {
            BlueprintTestHelper.EditorCall(ctx, "add_enum_entry", new { path = ecolor, name = entry });
        }
        var save = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = ecolor });
        ctx.AssertTrue(save["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {ecolor}: {save["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    /// <summary>
    /// Save non-blueprint assets (enums): enums have no compile concept, cannot use
    /// CompileAndSave (that would send compile_blueprint) -- same as Blueprint_Switch_Tests.
    /// </summary>
    private static void SaveAsset(TestContext ctx, string assetPath)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(r["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {assetPath}: {r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: create an enum and use it as a variable type --
        // MCP steps: EColor (Red/Green/Blue) + Enum_Var_A (Extends Actor) + C : EColor = Red
        CreateColorEnum(ctx);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EColor_A");
        const string a1 = "/Game/Tests/UE5_0/Enum_Var_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "C", "EColor_A", "Red");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (per doc). The enum is its own [Enum] domain block -- the previous
        // transcription attached [Blueprint]/[Extends] to the `public enum EColor` declaration,
        // leaving the class block without a domain attribute (FM1001, batch aborted).
        // EColor_B is declared once here and referenced by name in subtasks 2/3 via the
        // session-level enum alias, same as Blueprint_Switch_Tests' EState_B. Enums are not
        // saved by the DSL apply, so the [Enum] asset gets an explicit save_asset.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        SaveAsset(ctx, "/Game/Tests/UE5_0/EColor_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EColor_B");

        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Enum_Var_B");

        // -- Subtask 2: compare an enum value --
        // MCP steps: Enum_Cmp_A (Extends Actor) + Show(EColor c)
        //   -> c == Red -> Branch -> True: PrintString("R")
        //
        // Comparison uses the engine's dedicated enum node UK2Node_EnumEquality
        // ("Equal (Enum)", bridge side specializes by context-menu name EqualEnum)
        // — not EqualEqual_ByteByte: compilation-wise both are equivalent
        // (the engine ExpandNode also lands on EqualEqual_ByteByte), but visually a
        // byte compare cannot express enum semantics (visual feedback: should use the
        // enum version for comparison). A/B are wildcard pins; must connect the enum
        // parameter to A first (engine NotifyPinConnectionListChanged propagates the
        // type to B), then set B's entry default value (editor order: wire first, then
        // select from dropdown); the entry is written by display name "Red"; the
        // bridge-side enum default-value translation maps to the internal entry name.
        const string a2 = "/Game/Tests/UE5_0/Enum_Cmp_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Show",
            new[] { new { name = "c", type = "EColor_A" } });
        var eq2 = BlueprintTestHelper.AddNode(ctx, a2, "Show", "EqualEnum", 300, 0);
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "c", eq2.nodeId, "A");
        BlueprintTestHelper.SetPinDefault(ctx, a2, eq2.nodeId, "B", "Red");
        var br2 = BlueprintTestHelper.AddNode(ctx, a2, "Show", "Branch", 500, 0);
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "Show", "PrintString", 750, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, print2.nodeId, "InString", "R");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", br2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, eq2.nodeId, "Return Value", br2.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a2, br2.nodeId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path: EColor was declared once in subtask 1 (alias EColor -> EColor_B) and is
        // referenced here by its DSL name -- re-declaring it would re-create the same asset
        // path, which the bridge refuses to clobber.
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Enum_Cmp_B");

        // -- Subtask 3: enum array --
        // MCP steps: Enum_Arr_A (Extends Actor) + Palette : EColor[]
        //   Fill() -> Add(Red) / Add(Green)
        //
        // Array_Add's TargetArray / NewItem are wildcard (const-ref) pins; no default
        // value can be set before wiring (IsPinDefaultValid: wildcard goes through
        // DefaultValueSimpleValidation, reports "Unsupported type"; ref pins are also
        // rejected) — must connect TargetArray first so NewItem resolves to EColor_A
        // via ArrayTypeDependentParams, then set the enum entry default value.
        // The bridge-side connect wildcard propagation (PropagateWildcardTypes)
        // handles type resolution; set_pin_default_value's enum translation handles
        // "Red"/"Green" → internal entry name.
        const string a3 = "/Game/Tests/UE5_0/Enum_Arr_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Palette", "EColor_A[]");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Fill");
        var getPalette = BlueprintTestHelper.AddNode(ctx, a3, "Fill", "Palette", 100, 0, kind: "getter");
        var addR = BlueprintTestHelper.AddNode(ctx, a3, "Fill", "Array_Add", 350, -100,
            "KismetArrayLibrary");
        var addG = BlueprintTestHelper.AddNode(ctx, a3, "Fill", "Array_Add", 350, 100,
            "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getPalette.nodeId, "Palette", addR.nodeId, "TargetArray");
        BlueprintTestHelper.Connect(ctx, a3, getPalette.nodeId, "Palette", addG.nodeId, "TargetArray");
        BlueprintTestHelper.SetPinDefault(ctx, a3, addR.nodeId, "NewItem", "Red");
        BlueprintTestHelper.SetPinDefault(ctx, a3, addG.nodeId, "NewItem", "Green");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", addR.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, addR.nodeId, "then", addG.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, addG.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path: EColor via the session-level alias declared in subtask 1
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Enum_Arr_B");

        await Task.CompletedTask;
    }
}