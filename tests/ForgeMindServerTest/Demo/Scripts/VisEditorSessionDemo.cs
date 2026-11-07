// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Order 7 — what the session layer answers before any asset work: which editors are connected,
/// and the state of the one being driven.
/// <para>Both turns are the built-in editor actions (<c>list</c> / <c>status</c>), the same ones
/// <c>EngineLifecycleDemo</c> uses; nothing here reaches for a companion command.</para>
/// <para>Result confirmation: both turns. The reply of a session query is the evidence, and it is
/// three fields deep - <c>online</c>, <c>bridgePort</c>, <c>sessions</c> - so the turns name those;
/// everything else the answer carries is bookkeeping the reader did not ask about.</para>
/// </summary>
public sealed class VisEditorSessionDemo : IDemoScript
{
    public override string Name => "会话与状态：谁在线，当前状态";

    public override int Order => 7;

    public override IReadOnlyList<DemoTurn> Lines =>
    [
        DemoStep.Do("列出在线编辑器会话", "List the connected editor sessions", "editor",
            new Dictionary<string, object>
            {
                ["action"] = "list",
            },
            result: ["bridgePort", "sessions"]),
        DemoStep.Do("查询编辑器状态", "Query the editor status", "editor",
            new Dictionary<string, object>
            {
                ["action"] = "status",
            },
            result: ["online", "bridgePort", "sessions"]),
    ];
}
