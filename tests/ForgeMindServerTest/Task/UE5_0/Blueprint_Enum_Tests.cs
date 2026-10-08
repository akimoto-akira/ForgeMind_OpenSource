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
// - Enum comparison: UK2Node_EnumLiteral exists (K2Node_EnumLiteral.cpp:70) with input pin
//   `Enum` and output `Return Value`, but it is **not** in the bridge's native node
//   vocabulary, so subtask 2 uses UKismetMathLibrary::EqualEqual_ByteByte
//   ("Equal (Byte)", KismetMathLibrary.h:288-289, pins A / B / Return Value) with the
//   enum value on A and literal 0 on B (enum entry order defines the byte value).
// - Enum array: TArray<TEnum> is supported (Array_Add, KismetArrayLibrary.h:29-30,
//   pins TargetArray / NewItem -> Return Value). Doc's DSL `Palette.Add(...)` is copied
//   verbatim; the MCP side uses the Array_Add node explicitly.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transcribing task docs -- pending user visual confirmation of cases and results,
// then separately inserted per user instruction.
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

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: create an enum and use it as a variable type --
        // MCP steps: EColor (Red/Green/Blue) + Enum_Var_A (Extends Actor) + C : EColor = Red
        CreateColorEnum(ctx);
        const string a1 = "/Game/Tests/UE5_0/Enum_Var_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "C", "EColor_A", "Red");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: compare an enum value --
        // MCP steps: Enum_Cmp_A (Extends Actor) + Show(EColor c)
        //   -> c == Red -> Branch -> True: PrintString("R")
        const string a2 = "/Game/Tests/UE5_0/Enum_Cmp_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Show",
            new[] { new { name = "c", type = "EColor_A" } });
        var eq2 = BlueprintTestHelper.AddNode(ctx, a2, "Show", "EqualEqual_ByteByte", 300, 0,
            "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, eq2.nodeId, "B", "0");
        var br2 = BlueprintTestHelper.AddNode(ctx, a2, "Show", "Branch", 500, 0);
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "Show", "PrintString", 750, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a2, print2.nodeId, "InString", "R");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "c", eq2.nodeId, "A");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", br2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, eq2.nodeId, "Return Value", br2.nodeId, "Condition");
        BlueprintTestHelper.Connect(ctx, a2, br2.nodeId, "True", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: enum array --
        // MCP steps: Enum_Arr_A (Extends Actor) + Palette : EColor[]
        //   Fill() -> Add(Red) / Add(Green)
        const string a3 = "/Game/Tests/UE5_0/Enum_Arr_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Palette", "EColor_A[]");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Fill");
        var addR = BlueprintTestHelper.AddNode(ctx, a3, "Fill", "Array_Add", 300, -100,
            "KismetArrayLibrary");
        var addG = BlueprintTestHelper.AddNode(ctx, a3, "Fill", "Array_Add", 300, 100,
            "KismetArrayLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, addR.nodeId, "NewItem", "0");
        BlueprintTestHelper.SetPinDefault(ctx, a3, addG.nodeId, "NewItem", "1");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", addR.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, addR.nodeId, "Return Value", addG.nodeId, "TargetArray");
        BlueprintTestHelper.Connect(ctx, a3, addG.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}