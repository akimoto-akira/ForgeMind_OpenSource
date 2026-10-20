// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 55 - Blueprint Select (UE 5.0) - from Tasks/UE_5.0/blueprint-select.md
//
// Converted per collaboration spec point 6: author as if supported.
// DSL forms follow the latest DSL-syntax.md rules: Mode = Create explicit on every
// create block. The ternary `b ? 1 : 0` (Task 1) is not formally specified in
// DSL-syntax.md -- written as-is so the run either verifies it or exposes the
// ternary gap as a spec item. Task 3 enum uses an [Enum(Path, Mode = Create)]
// declaration block (not the doc's bare `public enum` after the blueprint attr).
// UK2Node_Select is a pure node (no exec pins); exec goes directly entry.then
// -> result.execute. ECC_Visibility-style engine enum constants are outside
// DeclaredEnumRegistry (§4.1); written as-is.
//
// Tasks (each one-task-two-assets: MCP A + DSL B, Q4):
//   Task 1 - Select by Bool (ternary b ? 1 : 0)
//   Task 2 - Select by Int (switch, 2 options "A"/"B")
//   Task 3 - Select by Enum (EColor Red/Green/Blue, switch)
//
// False-touch comparison (VerifyIrSnapshot): inserted 2026-10-18 per user instruction,
// after this case was confirmed by visual inspection. One per asset -- A side after
// CompileAndSave, DSL side after ApplyDsl (6 total: Select_Bool_A/B, Select_Int_A/B,
// Select_Enum_A/B). Same convention as Blueprint_ParentCall_Tests /
// Blueprint_Variable_Tests / Blueprint_Inherit_Tests (case 27). Baselines are seeded on
// the first run; from the second run onward they actually diff.
//
// Translator rule this case pins down: a switch whose every section is a single `return`
// lowers to a Select node (data-driven, same shape as the A side) instead of a Switch
// node; shapes it cannot express (default label, non-zero-based case values, ...) fall
// back to the normal Switch lowering. See BlueprintTranslator.TryLowerSingleReturnSwitchAsSelect.
//
// UK2Node_Select real pin names (K2Node_Select.cpp, run 20261017_2059 corrected these):
// options are "Option 0" / "Option 1" / ... (or one pin per enum entry once an enum is
// bound), the selector input is "Index", output is "ReturnValue". The earlier "A"/"B"/
// "0"/"1" and "b" names do not exist -- "A" silently resolved through the bridge's
// positional legacy alias table onto "Option 1", which is how the mismatch was reported
// against a pin the case never named. Per collaboration spec point 8 names must be the
// queryable engine ones, not aliases or display names.
//
// Conform-before-default ordering: every option pin is created as PC_Wildcard
// (K2Node_Select.cpp:208/214). On reconstruction an option pin only takes a concrete
// type when the RETURN pin is already non-wildcard:
//   bFillTypeFromReturn = ReturnPin->PinType != PC_Wildcard;                (:398)
//   if (bTypeShouldBeFilled && bFillTypeFromReturn) Pin->PinType = ReturnPin->PinType; (:404-406)
// so the connection order decides the outcome and differs per task:
//   * options typed like the index (task 1: bool index; task 3: enum index) -- an index
//     reached first leaves the wildcard options to be filled from the index path instead,
//     and "Expected a valid number for an integer property" then rejects the string
//     default (run 20261017_2238, Task 2).
//   * so ALL THREE tasks wire ReturnValue BEFORE Index. Task 1 (run 20261017_2350):
//     wiring the bool Index first conformed the options to bool, so the int return then
//     needed a bool-to-integer conversion. Select carries the option values in their own
//     type -- no conversion of the index type is involved.
//   * task 3 also wires ReturnValue BEFORE Index (run 20261017_2259 corrected this):
//     SetEnum binds the enum at node creation, so the per-entry option pins do not
//     depend on the index connection; wiring the EColor index first let the index path
//     conform the wildcard options/return to EColor and the int return was refused
//     ("EColor enum value and integer are incompatible"). Return first → options+return int, then the index.
// Either way the option defaults go in last. Same family of rule as
// Blueprint_ArrayOps_Tests (TargetArray connects before pin defaults).
//
// Task 1 value mapping: with a bool Index, Option 0 carries the friendly name "False"
// and Option 1 "True" (:220-223). The task doc writes "A=1, B=0" but its own DSL side
// is `b ? 1 : 0` and the two assets must be equivalent, so Option 0 = 0 (false branch)
// and Option 1 = 1 (true branch). The doc's loose A/B ordering is not the engine's.
// =========================================================================

public sealed class Blueprint_Select : ITestCase
{
    public string Name => "Blueprint Select (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 55;

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
            const string a1 = "/Game/Tests/UE5_0/Select_Bool_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
            var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Pick",
                new[] { new { name = "b", type = "bool" } },
                outputs: new[] { new { name = "ReturnValue", type = "int" } });
            var sel = BlueprintTestHelper.AddNode(ctx, a1, "Pick", "Select", 300, 0);
            // ReturnValue BEFORE Index - the option pins are wildcard until the node conforms
            // (:398-406 fills them from the return pin), so wiring the bool Index first
            // conformed them to bool and the int return then needed a bool-to-integer
            // conversion. Select carries the option values in their own type - no conversion.
            BlueprintTestHelper.Connect(ctx, a1, sel.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "b", sel.nodeId, "Index");
            BlueprintTestHelper.SetPinDefault(ctx, a1, sel.nodeId, "Option 0", "0");
            BlueprintTestHelper.SetPinDefault(ctx, a1, sel.nodeId, "Option 1", "1");
            BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", f1.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a1);
            // False-touch comparison (spec point 9: inserted per user instruction after the
            // case was confirmed by visual inspection; see the 2026-10-18 record)
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Select_Bool_B");
    }

    private static void BuildTask2(TestContext ctx)
    {
        {
            const string a2 = "/Game/Tests/UE5_0/Select_Int_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
            var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Pick",
                new[] { new { name = "i", type = "int" } },
                outputs: new[] { new { name = "ReturnValue", type = "string" } });
            // No growth payload: intCases sends 'cases', which the bridge's Select branch
            // does not read, and the engine already creates 2 option pins by default.
            var sel = BlueprintTestHelper.AddNode(ctx, a2, "Pick", "Select", 300, 0);
            // ReturnValue BEFORE Index: the reconstruct must see a non-wildcard return pin
            // for :398-406 to type the option pins as string. Wiring the int Index first
            // left the options int and the "A" default was rejected
            // (run 20261017_2238: "Expected a valid number for an integer property").
            BlueprintTestHelper.Connect(ctx, a2, sel.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "i", sel.nodeId, "Index");
            BlueprintTestHelper.SetPinDefault(ctx, a2, sel.nodeId, "Option 0", "A");
            BlueprintTestHelper.SetPinDefault(ctx, a2, sel.nodeId, "Option 1", "B");
            BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", f2.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a2);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);
        }

        BlueprintTestHelper.ApplyDsl(ctx, "");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Select_Int_B");
    }

    private static void BuildTask3(TestContext ctx)
    {
        {
            // The bridge command is create_blueprint_enum (+ add_enum_entry); the earlier
            // 'create_enum' does not exist (run 20261017_1241: "Unknown command 'create_enum'").
            const string ec = "/Game/Tests/UE5_0/EColor";
            BlueprintTestHelper.EditorCall(ctx, "create_blueprint_enum", new { path = ec });
            foreach (var (ename, eval) in new[] { ("Red", 0), ("Green", 1), ("Blue", 2) })
            {
                BlueprintTestHelper.EditorCall(ctx, "add_enum_entry",
                    new { path = ec, name = ename, value = eval });
            }

            // The enum must be saved to disk BEFORE use: otherwise the three entries linger in
            // memory as a dirty state, the function parameter type cannot resolve the enum
            // asset, and the parameter pin degrades to the underlying byte ("function header
            // cannot be byte"). Same as Blueprint_Switch_Tests -- enums have no compile
            // concept, so CompileAndSave cannot be used here, only save_asset.
            SaveAsset(ctx, ec);

            const string a3 = "/Game/Tests/UE5_0/Select_Enum_A";
            BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
            var f3 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a3, "Pick",
                // The parameter type must be the enum's FULL ASSET PATH: the bare name
                // "EColor" does not resolve to the enum asset, so the function header
                // parameter pin degrades to the underlying byte ("function header cannot be
                // byte"). Same as Blueprint_Switch_Tests type = "/Game/.../EState_A".
                new[] { new { name = "c", type = ec } },
                outputs: new[] { new { name = "ReturnValue", type = "int" } });
            // enumName sends payload 'enum'; the bridge now binds it through
            // UK2Node_Select::SetEnum, which makes AllocateDefaultPins emit one option pin
            // per enum entry. Those pins' REAL names are NewEnumerator0/1/2 (SetEnum uses
            // GetNameStringByIndex, which UUserDefinedEnum does not override); the authored
            // Red/Green/Blue land in PinFriendlyName and resolve through the bridge's
            // existing GetDisplayName() pin fallback -- no new fallback was added.
            // The NODE binding uses the SHORT name "EColor" -- same as the B-side translator
            // output, whose enum payload is the short name and is verified correct. The full
            // asset path is only for the FUNCTION PARAMETER type (fixing "function header
            // cannot be byte"); the two go through different resolution entry points, so each
            // uses the form that was verified for it.
            var sel = BlueprintTestHelper.AddNode(ctx, a3, "Pick", "Select", 300, 0,
                enumName: "EColor");
            // ReturnValue BEFORE Index - same as task 2 (run 20261017_2259 corrected this).
            // SetEnum already binds the enum at node creation, so the per-entry option pins
            // do NOT depend on the index connection; wiring the EColor Index first instead
            // let the index path conform the still-wildcard options/return to EColor, and the
            // int return connection was then refused - "EColor enum value and integer are incompatible".
            // Wiring the return first conforms options+return to int (:398-406 then fills the
            // still-wildcard option pins with int); the enum Index binds afterwards.
            BlueprintTestHelper.Connect(ctx, a3, sel.nodeId, "ReturnValue", f3.resultId, "ReturnValue");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "c", sel.nodeId, "Index");
            BlueprintTestHelper.SetPinDefault(ctx, a3, sel.nodeId, "Red", "1");
            BlueprintTestHelper.SetPinDefault(ctx, a3, sel.nodeId, "Green", "2");
            BlueprintTestHelper.SetPinDefault(ctx, a3, sel.nodeId, "Blue", "3");
            BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", f3.resultId, "execute");
            BlueprintTestHelper.CompileAndSave(ctx, a3);
            // False-touch comparison
            BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);
        }

        // Enum declaration block per latest DSL-syntax.md (not the doc's bare enum after attr).
        // The A side already creates and saves /Game/Tests/UE5_0/EColor, so the B side uses its
        // own asset (same EState_A / EState_B convention); otherwise Mode = Create hits
        // "asset already exists".
        // NOTE: keep this explanation OUTSIDE the DSL source below -- leading comments inside
        // the block break domain-attribute routing ("Declaration block is missing a domain
        // attribute", run 20261018_0121).
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // The DSL-created enum is not saved on apply either (user_defined_enum has no compile
        // concept) -- save it explicitly, same as Blueprint_Switch_Tests, so no dirty state
        // is left behind.
        SaveAsset(ctx, "/Game/Tests/UE5_0/Select_EColor_B");
        // False-touch comparison
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Select_Enum_B");
    }

    /// <summary>
    /// Save non-blueprint assets (enums): enums have no compile concept, cannot use
    /// CompileAndSave (that would send compile_blueprint on enum assets); here we only issue
    /// save_asset and assert the save succeeds -- same as Blueprint_Switch_Tests /
    /// Blueprint_Enum_Tests. Key: an unsaved enum cannot be resolved as a parameter type,
    /// so the function header parameter pin degrades to the underlying byte
    /// ("function header cannot be byte").
    /// </summary>
    private static void SaveAsset(TestContext ctx, string assetPath)
    {
        var r = BlueprintTestHelper.EditorCall(ctx, "save_asset", new { path = assetPath });
        ctx.AssertTrue(r["response"]?["saved"]?.GetValue<bool>() ?? false,
            $"save_asset {assetPath}: {r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }
}