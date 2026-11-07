// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Compat;

namespace ForgeMindServer.Test.Demo;

/// <summary>
/// One demo script — an abstract base rather than an interface, because a demo is no longer pure
/// scenario text: it plays against a live editor, and every script has to leave that editor
/// started before its first turn and closed after its last one.
/// <para>The division of labour: the script says what happens, this type says what the editor has
/// to be doing while it happens. Implementations live in <c>Demo/Scripts/</c> and are discovered
/// by reflection — dropping a file in is enough, the runner needs no edit.</para>
/// </summary>
public abstract class IDemoScript
{
    /// <summary>Script name (shown in usage errors and for future listings).</summary>
    public abstract string Name { get; }

    /// <summary>The number passed as the second command-line argument selects a script by this value.</summary>
    public abstract int Order { get; }

    /// <summary>The turns, played in order.</summary>
    public abstract IReadOnlyList<DemoTurn> Lines { get; }

    /// <summary>
    /// Whether the run owns the engine session around this script: the editor is started before
    /// the first turn and closed after the last one. On by default.
    /// <para>A demo sits under no group — unlike a test case, nothing above it launches or closes
    /// the editor, so if the script does not ask for it, it never happens. A script whose subject
    /// *is* the lifecycle (script 0) turns this off and plays those steps itself: two launchers
    /// would fight over the same project.</para>
    /// </summary>
    protected virtual bool StartsOwnEngine => true;

    /// <summary>Read by the runner, which is not allowed to care how a script decides.</summary>
    internal bool ManagesEngine => StartsOwnEngine;

    /// <summary>Caption of the turn that starts the engine, in the run's language.</summary>
    internal string StartText(string language)
        => English(language) ? "Start the engine" : "启动引擎";

    /// <summary>Caption of the turn that closes the engine.</summary>
    internal string StopText(string language)
        => English(language) ? "Close the engine" : "关闭引擎";

    /// <summary>
    /// What the run says while the start is still waiting. The stage is a state, the
    /// wording belongs to whoever prints it — hence both live here and not in the session.
    /// </summary>
    internal string StageText(EngineSession.StartStage stage, int remainingSeconds, string language)
        => stage switch
        {
            EngineSession.StartStage.Dialog => English(language)
                ? $"Waiting for the startup dialog · {remainingSeconds}s"
                : $"等待启动弹窗 · {remainingSeconds}s",
            _ => English(language)
                ? $"Waiting for the editor to come online · {remainingSeconds}s"
                : $"等待编辑器上线 · {remainingSeconds}s",
        };

    /// <summary>
    /// How a start that never produced a usable editor reads. Shown, not thrown: a demo reports
    /// what happened to the recording, it does not take the process down with it.
    /// </summary>
    internal string StartFailedText(string message, string language)
        => English(language) ? $"The engine did not start: {message}" : $"引擎未能启动：{message}";

    /// <summary>
    /// Starts the editor for this script: a live editor is reused, otherwise one is launched and
    /// waited for (startup dialog answered, then online).
    /// </summary>
    /// <param name="tools">This run's tool table.</param>
    /// <param name="progress">Told what the start is waiting on, so the row can count down.</param>
    internal System.Threading.Tasks.Task<EngineSession.StartOutcome> StartEngineAsync(
        IReadOnlyList<ITool> tools,
        Action<EngineSession.StartStage, int>? progress = null)
        => EngineSession.StartAsync(Caller(tools), null, progress);

    /// <summary>
    /// Closes the editor after the last turn: dirty assets are saved first, then <c>quit_editor</c>.
    /// The failure is returned rather than thrown — whether a stop failed is something the window
    /// shows, not something it dies of.
    /// </summary>
    internal async System.Threading.Tasks.Task<ToolResult> StopEngineAsync(IReadOnlyList<ITool> tools)
        => await EngineSession.StopAsync(Caller(tools));

    /// <summary>
    /// Bridges the session to this run's tools: the session knows tool names, the script side
    /// knows where the tool table is. Same execution path as a script turn, minus the caption.
    /// </summary>
    private static EngineSession.ToolCall Caller(IReadOnlyList<ITool> tools)
        => (toolName, parameters) => DemoStep.Call(tools, toolName, parameters);

    private static bool English(string language)
        => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
}
