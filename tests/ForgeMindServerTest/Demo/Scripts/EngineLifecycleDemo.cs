// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Order 0 — launch the engine, hold it online for ten seconds, close it.
/// <para>The tool names and parameters mirror the ones the UE5.0 lifecycle cases use
/// (<c>launch</c>, <c>editor status</c>, <c>editor quit_editor</c>), so the demo drives the
/// same real path. There is no build step: building is not part of what this shows.</para>
/// <para>This is the one script that opts out of the base class's engine session
/// (<see cref="IDemoScript.StartsOwnEngine"/>): starting and closing the editor *is* what it
/// demonstrates, so it plays those steps itself. Every other script gets the session handed to it
/// — starting a second editor on the same project would be refused.</para>
/// <para>The three turns that reach the editor name the fields that prove them — the process id a
/// launch reported, the <c>online</c> flag a status carries, the <c>exitRequested</c> a quit answers
/// with — so the recording shows the evidence instead of a bare &quot;执行成功&quot;.</para>
/// </summary>
public sealed class EngineLifecycleDemo : IDemoScript
{
    protected override bool StartsOwnEngine => false;

    public override string Name => "启动引擎 → 上线 10 秒后关闭";

    public override int Order => 0;

    public override IReadOnlyList<DemoTurn> Lines =>
    [
        DemoStep.Do("启动 ForgeMind 编辑器", "Launch ForgeMind editor", "launch",
            new Dictionary<string, object>
            {
                ["path"] = BlueprintTestHelper.ProjectPath,
                ["mode"] = "editor",
            },
            result: ["processPid"]),
        DemoStep.Wait("等待引擎启动", "Waiting for engine startup", 60),
        DemoStep.Do("检查编辑器在线状态", "Check editor online status", "editor",
            new Dictionary<string, object>
            {
                ["action"] = "status",
            },
            result: ["online", "sessions"]),
        DemoStep.Wait("上线后停留 10 秒", "Hold 10 seconds after online", 10),
        DemoStep.Do("关闭编辑器", "Close editor", "editor",
            new Dictionary<string, object>
            {
                ["action"] = "quit_editor",
                ["path"] = BlueprintTestHelper.ProjectPath,
            },
            result: ["exitRequested", "note"]),
    ];
}
