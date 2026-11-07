// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Order 2 — a spline query, built one command at a time.
/// <para>Mirrors <c>Blueprint_Spline_Tests</c> task 1: a component, a function with one int input
/// and a vector return, the component's getter as the call's target, and
/// <c>GetLocationAtSplinePoint</c> set to world space.</para>
/// <para>Two engine facts the step list has to respect, both learned from that test: the index pin
/// is <c>PointIndex</c> (the C++ name, not the documented one), and the three spline queries are
/// <c>const BlueprintCallable</c> — the editor draws them as pure nodes, so the exec path runs
/// entry → result and the query hangs off it by data pins only.</para>
/// </summary>
public sealed class VisSplineQueryDemo : IDemoScript
{
    private const string Target = "/Game/Tests/Video/Ep05/Vis_Spline_B";

    public override string Name => "逐步骤做样条查询：世界坐标取第 i 个点";

    public override int Order => 2;

    public override IReadOnlyList<DemoTurn> Lines =>
    [
        DemoStep.Clear("先清掉上一次录制留下的蓝图", "Clear the blueprint the previous recording left behind", Target),

        DemoStep.Do("建一个 Actor 子类蓝图", "Create an Actor subclass blueprint", "editor",
            DemoStep.Editor("create_blueprint", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["parentClass"] = "Actor",
            }), result: ["created", "path", "parentClass"]),

        DemoStep.Do("加一个样条组件", "Add a spline component", "editor",
            DemoStep.Editor("add_blueprint_component", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["class"] = "SplineComponent",
                ["name"] = "Spline",
            })),

        DemoStep.Do("声明函数 At：参数是第几个点，返回一个向量", "Declare At: takes a point index, returns a vector", "editor",
            DemoStep.Editor("create_function_graph", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["name"] = "At",
                ["params"] = new object[]
                {
                    new Dictionary<string, object> { ["name"] = "i", ["type"] = "int" },
                },
                ["outputs"] = new object[]
                {
                    new Dictionary<string, object> { ["name"] = "ReturnValue", ["type"] = "vector" },
                },
            }), result: ["created", "compileMessages"]),

        DemoStep.Do("看一眼这张图：入口和返回节点", "Look at the graph: its entry and return node", "editor",
            DemoStep.Editor("list_blueprint_graph", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["graphName"] = "At",
            }), result: ["found", "graphs"]),

        DemoStep.Do("取到那个样条组件本身", "Reach the spline component itself", "editor",
            DemoStep.Editor("add_blueprint_node", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["graphName"] = "At",
                ["name"] = "Spline",
                ["kind"] = "getter",
                ["x"] = 0,
                ["y"] = 0,
            }), result: ["nodeId", "nodeClass", "graph"]),

        DemoStep.Do("放一个取点位置的节点", "Place a Get Location at Spline Point node", "editor",
            DemoStep.Editor("add_blueprint_node", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["graphName"] = "At",
                ["name"] = "GetLocationAtSplinePoint",
                ["class"] = "SplineComponent",
                ["x"] = 300,
                ["y"] = 0,
            }), result: ["nodeId", "nodeClass", "graph"]),

        DemoStep.Do("让它对准那个组件", "Point it at that component", "editor",
            DemoStep.Editor("connect_blueprint_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromNode"] = DemoStep.Ref(6),
                ["fromPin"] = "Spline",
                ["toNode"] = DemoStep.Ref(7),
                ["toPin"] = "__target",
            })),

        DemoStep.Do("函数的 i 接进去，用的是引擎真实引脚名 PointIndex", "Feed the function's i in: the engine's real pin is PointIndex", "editor",
            DemoStep.Editor("connect_blueprint_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromNode"] = DemoStep.Entry(5),
                ["fromPin"] = "i",
                ["toNode"] = DemoStep.Ref(7),
                ["toPin"] = "PointIndex",
            })),

        DemoStep.Do("坐标空间选世界：不做组件变换", "Pick world space: no component transform applied", "editor",
            DemoStep.Editor("set_pin_default_value", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["node"] = DemoStep.Ref(7),
                ["pin"] = "CoordinateSpace",
                ["value"] = "World",
            })),

        DemoStep.Do("执行入口接到返回节点", "Wire the entry into the return node", "editor",
            DemoStep.Editor("connect_blueprint_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromNode"] = DemoStep.Entry(5),
                ["fromPin"] = "then",
                ["toNode"] = DemoStep.Result(5),
                ["toPin"] = "execute",
            })),

        DemoStep.Do("把取到的位置返回出去", "Return the point it read", "editor",
            DemoStep.Editor("connect_blueprint_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromNode"] = DemoStep.Ref(7),
                ["fromPin"] = "ReturnValue",
                ["toNode"] = DemoStep.Result(5),
                ["toPin"] = "ReturnValue",
            })),

        DemoStep.Do("编译：让引擎检查这张图", "Compile: let the engine check the graph", "editor",
            DemoStep.Editor("compile_blueprint", new Dictionary<string, object>
            {
                ["path"] = Target,
            })),

        DemoStep.Do("保存资产", "Save the asset", "editor",
            DemoStep.Editor("save_asset", new Dictionary<string, object>
            {
                ["path"] = Target,
            }), result: ["saved", "count"]),
    ];
}
