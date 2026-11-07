// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Order 4 — a facing term, built one command at a time.
/// <para>Mirrors the A side of <c>Material_WorldPosition_Tests</c> Q3: camera vector dotted with the
/// pixel normal, fed into EmissiveColor. Three expressions, three wires, one compile — the smallest
/// material that still shows data flowing through a node instead of around it.</para>
/// </summary>
public sealed class VisMaterialDotNormalDemo : IDemoScript
{
    private const string Target = "/Game/Tests/Video/Ep05/Vis_CamDot_M";

    public override string Name => "逐步骤做点乘：相机方向 · 像素法线";

    public override int Order => 4;

    public override IReadOnlyList<DemoTurn> Lines =>
    [
        DemoStep.Clear("先清掉上一次录制留下的材质", "Clear the material the previous recording left behind", Target),

        DemoStep.Do("建一个表面材质，不透明", "Create a surface material, opaque", "editor",
            DemoStep.Editor("create_material", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["domain"] = "Surface",
                ["blendMode"] = "Opaque",
            }), result: ["created", "path"]),

        DemoStep.Do("取像素指向相机的方向", "Take the direction from the pixel to the camera", "editor",
            DemoStep.Editor("add_material_expression", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["expressionClass"] = "MaterialExpressionCameraVectorWS",
            })),

        DemoStep.Do("取像素自己的法线", "Take the pixel's own normal", "editor",
            DemoStep.Editor("add_material_expression", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["expressionClass"] = "MaterialExpressionPixelNormalWS",
            })),

        DemoStep.Do("放一个点乘节点", "Place a Dot Product node", "editor",
            DemoStep.Editor("add_material_expression", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["expressionClass"] = "MaterialExpressionDotProduct",
            }), result: ["expressionId", "expressionClass"]),

        DemoStep.Do("相机方向接到 A", "Wire the camera direction into A", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(3),
                ["fromPin"] = "",
                ["toExpression"] = DemoStep.Ref(5),
                ["toPin"] = "A",
            })),

        DemoStep.Do("法线接到 B", "Wire the normal into B", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(4),
                ["fromPin"] = "",
                ["toExpression"] = DemoStep.Ref(5),
                ["toPin"] = "B",
            })),

        DemoStep.Do("点乘结果接到自发光", "Feed the dot product into emissive", "editor",
            DemoStep.Editor("connect_material_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromExpression"] = DemoStep.Ref(5),
                ["fromPin"] = "",
                ["toExpression"] = "Material",
                ["toPin"] = "EmissiveColor",
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
