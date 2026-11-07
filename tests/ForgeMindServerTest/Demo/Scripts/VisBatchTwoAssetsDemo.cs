// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Order 5 — two assets in one session, switching between them as the work demands.
/// <para>This replaces the old "two DSL blocks in one call" scene, which had nothing left to show
/// once every demo became step-by-step: the visible claim is no longer "one call covers two
/// domains" but "one session holds both, and going back and forth costs no round trip". Steps 3-4
/// finish the blueprint, 5-7 the material, then 8-11 close both out — the interleaving is the point,
/// so do not reorder it into two tidy halves.</para>
/// <para>Asset names are the ones the old scene used, so a re-recording overwrites its own
/// leftovers; the two clearing turns at the top are what makes that safe.</para>
/// <para>The variable turn writes <c>default</c>, not <c>defaultValue</c>: the step command
/// <c>add_blueprint_variable</c> reads the former (<c>ForgeMindBridgeClient_BlueprintVars.cpp:92</c>),
/// while <c>defaultValue</c> is the Asset-IR spelling the DSL side uses, which the bridge itself
/// converts to <c>default</c> before calling the same handler. Sent under the wrong name the value is
/// simply not read — the variable is added with an empty default and the step still answers
/// <c>added</c>, so the turn looks like it worked. The material parameter two turns later keeps
/// <c>defaultValue</c>: that command (<c>add_material_expression</c>) really is spelled that way.</para>
/// </summary>
public sealed class VisBatchTwoAssetsDemo : IDemoScript
{
    private const string BlueprintPath = "/Game/Tests/Video/Ep05/Vis_Batch_B";
    private const string MaterialPath = "/Game/Tests/Video/Ep05/Vis_Batch_M";

    public override string Name => "两资产一场连打：蓝图加变量、材质接世界坐标";

    public override int Order => 5;

    public override IReadOnlyList<DemoTurn> Lines =>
    [
        DemoStep.Clear("先清掉旧蓝图", "Clear the old blueprint first", BlueprintPath),

        DemoStep.Clear("旧材质也一起清掉", "Clear the old material too", MaterialPath),

        DemoStep.Do("建蓝图：Actor 子类", "Create the blueprint: an Actor subclass", "editor",
            DemoStep.Editor("create_blueprint", new Dictionary<string, object>
            {
                ["path"] = BlueprintPath,
                ["parentClass"] = "Actor",
            })),

        DemoStep.Do("给它加一个变量 Speed，默认 300", "Give it a Speed variable, 300 by default", "editor",
            DemoStep.Editor("add_blueprint_variable", new Dictionary<string, object>
            {
                ["path"] = BlueprintPath,
                ["name"] = "Speed",
                ["type"] = "float",
                ["default"] = "300",
            }), result: ["added", "variable", "type", "default"]),

        DemoStep.Do("换到材质：建一个表面材质", "Switch to the material: create a surface material", "editor",
            DemoStep.Editor("create_material", new Dictionary<string, object>
            {
                ["path"] = MaterialPath,
                ["domain"] = "Surface",
                ["blendMode"] = "Opaque",
            })),

        DemoStep.Do("取世界坐标", "Take the world position", "editor",
            DemoStep.Editor("add_material_expression", new Dictionary<string, object>
            {
                ["path"] = MaterialPath,
                ["expressionClass"] = "MaterialExpressionWorldPosition",
            })),

        DemoStep.Do("世界坐标接到基色", "Feed the world position into base color", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = MaterialPath,
                ["fromExpression"] = DemoStep.Ref(6),
                ["fromPin"] = "",
                ["toExpression"] = "Material",
                ["toPin"] = "BaseColor",
            })),

        DemoStep.Do("回到蓝图：编译", "Back to the blueprint: compile", "editor",
            DemoStep.Editor("compile_blueprint", new Dictionary<string, object>
            {
                ["path"] = BlueprintPath,
            })),

        DemoStep.Do("再编译材质", "Then compile the material", "editor",
            DemoStep.Editor("compile_material", new Dictionary<string, object>
            {
                ["path"] = MaterialPath,
            }), result: ["compiled", "materialState"]),

        DemoStep.Do("保存蓝图", "Save the blueprint", "editor",
            DemoStep.Editor("save_asset", new Dictionary<string, object>
            {
                ["path"] = BlueprintPath,
            }), result: ["saved", "count"]),

        DemoStep.Do("保存材质，两个资产都落盘", "Save the material: both assets are on disk", "editor",
            DemoStep.Editor("save_asset", new Dictionary<string, object>
            {
                ["path"] = MaterialPath,
            }), result: ["saved", "count"]),
    ];
}
