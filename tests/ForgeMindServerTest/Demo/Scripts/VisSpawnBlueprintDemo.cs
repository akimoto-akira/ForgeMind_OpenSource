// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Order 1 — a blueprint built one command at a time.
/// <para>Same shape as <c>Blueprint_Spawn_Tests</c>: an <see cref="Actor"/> subclass whose
/// <c>Go</c> spawns an actor at (100, 0, 0). Where the DSL form of this scene was one call, every
/// command is now its own turn — create, declare, look, place, wire, fill, compile, save — and the
/// ids the bridge answers with travel between those turns (<c>${n.id}</c>), which is exactly what a
/// one-block DSL call hides.</para>
/// <para>Ordering note: <c>create_function_graph</c> answers with the graph, not with its node ids,
/// so turn 4 lists the graph and turns 6 / 11 take the terminals from that listing
/// (<see cref="DemoStep.Entry"/> / <see cref="DemoStep.Result"/>).</para>
/// </summary>
public sealed class VisSpawnBlueprintDemo : IDemoScript
{
    private const string Target = "/Game/Tests/Video/Ep05/Vis_Spawn_B";

    public override string Name => "逐步骤生成蓝图：Actor 子类里生成 Actor";

    public override int Order => 1;

    public override IReadOnlyList<DemoTurn> Lines =>
    [
        DemoStep.Clear("先清掉上一次录制留下的蓝图", "Clear the blueprint the previous recording left behind", Target),

        DemoStep.Do("建一个 Actor 子类蓝图", "Create an Actor subclass blueprint", "editor",
            DemoStep.Editor("create_blueprint", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["parentClass"] = "Actor",
            }), result: ["created", "path", "parentClass"]),

        DemoStep.Do("声明函数 Go：演示的入口", "Declare the Go function: where this demo starts", "editor",
            DemoStep.Editor("create_function_graph", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["name"] = "Go",
            }), result: ["created", "compileMessages"]),

        DemoStep.Do("看一眼这张图：入口节点和返回节点", "Look at the graph: its entry and its return node", "editor",
            DemoStep.Editor("list_blueprint_graph", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["graphName"] = "Go",
            }), result: ["found", "graphs"]),

        DemoStep.Do("放一个生成 Actor 的节点", "Place a Spawn Actor node", "editor",
            DemoStep.Editor("add_blueprint_node", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["graphName"] = "Go",
                ["name"] = "SpawnActorFromClass",
                ["x"] = 300,
                ["y"] = 0,
            }), result: ["nodeId", "nodeClass", "graph"]),

        DemoStep.Do("把 Go 的执行入口接到它上面", "Wire Go's execution entry into it", "editor",
            DemoStep.Editor("connect_blueprint_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromNode"] = DemoStep.Entry(4),
                ["fromPin"] = "then",
                ["toNode"] = DemoStep.Ref(5),
                ["toPin"] = "execute",
            })),

        DemoStep.Do("告诉它要生成哪个类：Actor", "Tell it which class to spawn: Actor", "editor",
            DemoStep.Editor("set_pin_default_value", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["node"] = DemoStep.Ref(5),
                ["pin"] = "Class",
                ["value"] = "Actor",
            })),

        DemoStep.Do("再放一个 Make Transform：生成点不能没有坐标", "Place a Make Transform: a spawn needs a location", "editor",
            DemoStep.Editor("add_blueprint_node", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["graphName"] = "Go",
                ["name"] = "MakeTransform",
                ["class"] = "KismetMathLibrary",
                ["x"] = 600,
                ["y"] = -100,
            }), result: ["nodeId", "nodeClass"]),

        DemoStep.Do("坐标定在 (100, 0, 0)", "Pin the location to (100, 0, 0)", "editor",
            DemoStep.Editor("set_pin_default_value", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["node"] = DemoStep.Ref(8),
                ["pin"] = "Location",
                ["value"] = "100, 0, 0",
            })),

        DemoStep.Do("把这个 Transform 接到生成节点的坐标口", "Feed that transform into the spawn node", "editor",
            DemoStep.Editor("connect_blueprint_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromNode"] = DemoStep.Ref(8),
                ["fromPin"] = "ReturnValue",
                ["toNode"] = DemoStep.Ref(5),
                ["toPin"] = "SpawnTransform",
            })),

        DemoStep.Do("最后把它接回函数返回节点", "Close the path into the function's return node", "editor",
            DemoStep.Editor("connect_blueprint_pins", new Dictionary<string, object>
            {
                ["path"] = Target,
                ["fromNode"] = DemoStep.Ref(5),
                ["fromPin"] = "then",
                ["toNode"] = DemoStep.Result(4),
                ["toPin"] = "execute",
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
