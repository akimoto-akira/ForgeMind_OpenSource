// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Order 3 — a material conditional, built one command at a time.
/// <para>Mirrors the A side of <c>Material_If_Tests</c> Q1: brick texture in, threshold parameter,
/// <see cref="MaterialExpressionIf"/> choosing between (1,0,0) and (0,0,0), result into BaseColor.
/// The texture is a real StarterContent asset — a dangling reference does not fail on the sample
/// node, it fails later on everything wired downstream of it.</para>
/// <para>The two constant turns are <c>connect_material_pins</c> without a target: the bridge writes
/// the value straight onto that pin instead of pulling a wire into it.</para>
/// <para>The channel turn asks for <c>R</c> and ends with one more node than the script names: a
/// channel request is realized the way the editor draws it — a <c>MaterialExpressionComponentMask</c>
/// taking R off the sample's RGB output and feeding the target. The texture sample itself samples the
/// whole colour, so that is what its own turn says; the mask is said where it happens.</para>
/// </summary>
public sealed class VisMaterialIfDemo : IDemoScript
{
    private const string Target = "/Game/Tests/Video/Ep05/Vis_MatIf_M";

    public override string Name => "逐步骤做材质条件：阈值切换颜色";

    public override int Order => 3;

    public override IReadOnlyList<DemoTurn> Lines =>
    [
        DemoStep.Clear("先清掉上一次录制留下的材质", "Clear the material the previous recording left behind", Target),

        DemoStep.Do("建一个表面材质，不透明", "Create a surface material, opaque", "editor",
            DemoStep.Editor("create_material", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["domain"] = "Surface",
                ["blendMode"] = "Opaque",
            })),

        DemoStep.Do("取一张砖墙贴图", "Sample a brick texture", "editor",
            DemoStep.Editor("add_material_expression", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["expressionClass"] = "MaterialExpressionTextureSample",
                ["texture"] = "/Game/StarterContent/Textures/T_Brick_Clay_New_D",
            })),

        DemoStep.Do("加一个阈值参数，默认 0.5", "Add a threshold parameter, 0.5 by default", "editor",
            DemoStep.Editor("add_material_expression", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["expressionClass"] = "MaterialExpressionScalarParameter",
                ["name"] = "Threshold",
                ["defaultValue"] = 0.5,
            }), result: ["expressionId", "expressionClass"]),

        DemoStep.Do("放一个 If 节点：它比较 A 和 B", "Place an If node: it compares A against B", "editor",
            DemoStep.Editor("add_material_expression", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["expressionClass"] = "MaterialExpressionIf",
            })),

        DemoStep.Do("取贴图的 R 接到 A（桥插一个通道遮罩节点）", "Take the texture's R into A (the bridge inserts a channel mask)", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(3),
                ["fromPin"] = "R",
                ["toExpression"] = DemoStep.Ref(5),
                ["toPin"] = "A",
            })),

        DemoStep.Do("阈值参数接到 B", "Wire the threshold parameter into B", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(4),
                ["fromPin"] = "",
                ["toExpression"] = DemoStep.Ref(5),
                ["toPin"] = "B",
            })),

        DemoStep.Do("A 大于 B 时给红色", "Red when A is above B", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(5),
                ["fromPin"] = "AGreaterThanB",
                ["constant"] = "(R=1,G=0,B=0)",
            })),

        DemoStep.Do("A 小于 B 时给黑色", "Black when A is below B", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(5),
                ["fromPin"] = "ALessThanB",
                ["constant"] = "(R=0,G=0,B=0)",
            })),

        DemoStep.Do("结果接到材质的基色", "Feed the result into the material's base color", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(5),
                ["fromPin"] = "",
                ["toExpression"] = "Material",
                ["toPin"] = "BaseColor",
            })),

        DemoStep.Do("编译材质", "Compile the material", "editor",
            DemoStep.Editor("compile_material", new Dictionary<string, object>
            {
                ["path"] = Target,
            }), result: ["compiled", "materialState"]),

        DemoStep.Do("保存资产", "Save the asset", "editor",
            DemoStep.Editor("save_asset", new Dictionary<string, object>
            {
                ["path"] = Target,
            }), result: ["saved", "count"]),
    ];
}
