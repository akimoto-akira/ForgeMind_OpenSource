// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 30 - Blueprint Editable Variable (UE 5.0) - from Tasks/UE_5.0/blueprint-editable-variable.md
//
// Probe (collaboration spec point 6):
// - add_blueprint_variable supported editability fields: category (variable category, default
//   'Default'), instanceEditable (clears CPF_DisableEditOnInstance, i.e. detail panel's
//   Instance Editable), exposeOnSpawn (writes MD_ExposeOnSpawn metadata, compiler derives
//   CPF_ExposeOnSpawn from it) -- see HandleAddBlueprintVariable. These three fields
//   not exposed by BlueprintTestHelper.AddVariable, this file directly sends editor commands
//   (same as Blueprint_Variable_Tests' MoveSpeed approach).
// - **tooltip not implemented on bridge**: add_blueprint_variable / update_blueprint_variable
//   both lack this field (engine side is FBPVariableDescription's MD_Tooltip metadata),
//   doc task 3's Tooltip("name") written directly as "supported", missing part is pending requirement.
// - DSL side: BlueprintTranslator.ApplyVariableFlags only reads **Variable attribute's own**
//   named args (Category / Editable|EditAnywhere / BlueprintReadOnly / Replicated),
//   doc's [Variable, InstanceEditable] / [Variable, ExposeOnSpawn] /
//   [Variable, Category("Config"), Tooltip("name")] three forms (bare identifier, standalone
//   attributes) are not currently recognized -- copied from task doc, test is spec.
//
// Note: per collaboration spec point 9, "diff comparison" (VerifyIrSnapshot) not attached
// when transwriting task docs.
// =========================================================================

public sealed class Blueprint_EditableVar : ITestCase
{
    public string Name => "Blueprint Editable Variable (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 30;

    /// <summary>
    /// Directly sends add_blueprint_variable and asserts added. Variable visibility / editability
    /// modifiers (category / instanceEditable / exposeOnSpawn / tooltip) are not in
    /// BlueprintTestHelper.AddVariable's parameter list, so not using that helper.
    /// </summary>
    private static void AddVariable(TestContext ctx, string assetPath, string name, string type,
        string defaultValue, string? category = null, bool instanceEditable = false,
        bool exposeOnSpawn = false, string? tooltip = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["path"] = assetPath,
            ["name"] = name,
            ["type"] = type,
            ["default"] = defaultValue,
        };
        if (category != null) payload["category"] = category;
        if (instanceEditable) payload["instanceEditable"] = true;
        if (exposeOnSpawn) payload["exposeOnSpawn"] = true;
        if (tooltip != null) payload["tooltip"] = tooltip;

        var r = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_variable", payload);
        ctx.AssertTrue(r["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_variable {name} ({type}) @ {assetPath}: "
            + $"{r["response"]?["reason"]?.GetValue<string>() ?? ""}");
    }

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: InstanceEditable (instance editable) --
        // MCP steps: Edit_Inst_A (Extends Actor) + Speed : float (default 100,
        //   detail panel check Instance Editable -> in-level instance modifiable)
        const string a1 = "/Game/Tests/UE5_0/Edit_Inst_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        AddVariable(ctx, a1, "Speed", "float", "100.0", instanceEditable: true);
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc; [Variable, InstanceEditable] is DSL pending spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: ExposeOnSpawn (expose on spawn) --
        // MCP steps: Edit_Spawn_A (Extends Actor) + Count : int (default 0,
        //   detail panel check Expose on Spawn -> SpawnActor node shows this param)
        const string a2 = "/Game/Tests/UE5_0/Edit_Spawn_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        AddVariable(ctx, a2, "Count", "int", "0", exposeOnSpawn: true);
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc; [Variable, ExposeOnSpawn] is DSL pending spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: Category and Tooltip --
        // MCP steps: Edit_Cat_A (Extends Actor) + Label : string,
        //   Category = "Config" (detail panel group), Tooltip = "name" (hover hint)
        //   -- category supported on bridge, tooltip not yet (see file header)
        const string a3 = "/Game/Tests/UE5_0/Edit_Cat_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        AddVariable(ctx, a3, "Label", "string", "", category: "Config", tooltip: "name");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc; Category / Tooltip standalone attribute forms are DSL pending spec)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}
