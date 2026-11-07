// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Test;

/// <summary>
/// Starting and stopping the editor for a run — one implementation behind both directions: the
/// UE5.0 group lifecycle case, and the <c>--Demo</c> scripts, which have to own the engine session
/// themselves because a demo sits under no group that could launch or close anything for it.
/// <para>Both wait for the same two things after <c>launch</c>: the editor's startup confirmation
/// dialog has to be answered (<see cref="ConfirmDialog"/>), and the bridge has to report the
/// editor online. A run must never end with the editor standing — the next <c>launch</c> is
/// refused while the project is already open, and a live editor holds the project's binaries
/// locked for the next build.</para>
/// </summary>
internal static class EngineSession
{
    /// <summary>
    /// Calls one tool by name. The test side routes this through <see cref="TestContext"/> so the
    /// bridge round-trip lands in the run record; the demo side routes it through its own tool
    /// table, because a demo writes no log.
    /// </summary>
    internal delegate ToolResult ToolCall(string toolName, Dictionary<string, object> parameters);

    /// <summary>
    /// What a start is currently waiting on. Deliberately a state and not a sentence: the wording
    /// belongs to whoever is watching (the demo window speaks two languages, the test log does not).
    /// </summary>
    internal enum StartStage
    {
        /// <summary>Launch was sent; waiting for a startup dialog worth answering.</summary>
        Dialog,

        /// <summary>The editor process exists; waiting for the bridge to see it online.</summary>
        Online,
    }

    /// <summary>How a start ended.</summary>
    /// <param name="Success">An editor is online and can be driven.</param>
    /// <param name="Launched">True when this run started the editor, false when it reused a live one.</param>
    /// <param name="Message">One line for the record and for the demo row.</param>
    internal readonly record struct StartOutcome(bool Success, bool Launched, string Message);

    /// <summary>How long the startup dialog is given to appear, and how often it is looked for.</summary>
    private const int DialogWaitSeconds = 60;
    private const int DialogPollSeconds = 10;

    /// <summary>
    /// Loading the project has no fixed duration, so it is polled instead of waited out: the run
    /// moves on the moment the bridge answers, and the limit only exists so a hung editor ends the
    /// run instead of stalling it forever.
    /// </summary>
    private const int OnlineWaitSeconds = 120;
    private const int OnlinePollSeconds = 5;

    /// <summary>Grace period after <c>quit_editor</c>: the process needs time to actually go away.</summary>
    private const int QuitWaitSeconds = 5;

    /// <summary>
    /// Starts the editor and waits until it is online.
    /// <para>An editor that is already online is reused rather than launched again: the launch tool
    /// refuses to open the same project twice, and a re-record normally leaves the previous session
    /// standing.</para>
    /// </summary>
    /// <param name="call">Tool invoker for the caller's side (test context or demo tool table).</param>
    /// <param name="logger">Where to record progress; <c>null</c> keeps the start silent.</param>
    /// <param name="progress">Told what the start is waiting on and for how much longer.</param>
    internal static async Task<StartOutcome> StartAsync(
        ToolCall call,
        ILogger? logger = null,
        Action<StartStage, int>? progress = null)
    {
        if (IsOnline(call("editor", Status())))
        {
            logger?.Info(null, "[Engine] An editor is already online — reusing it");
            return new StartOutcome(true, false, "Reused the editor that was already online");
        }

        logger?.Info(null, "[Engine] Launching {0}", BlueprintTestHelper.ProjectPath);
        ToolResult launch = call("launch", new Dictionary<string, object>
        {
            ["path"] = BlueprintTestHelper.ProjectPath,
            ["mode"] = "editor",
        });

        if (!launch.Success)
        {
            // Losing the race against another launcher still leaves an editor online, and that is
            // the state this method promises. Only a failure with nothing online is a failure.
            if (!IsOnline(call("editor", Status())))
            {
                logger?.Error(null, "[Engine] Launch failed: {0}", launch.Message);
                return new StartOutcome(false, false, launch.Message);
            }

            logger?.Warn(null, "[Engine] Launch reported a failure but an editor is online: {0}", launch.Message);
            return new StartOutcome(true, false, launch.Message);
        }

        int? processId = ReadProcessId(launch);
        logger?.Info(null, "[Engine] Launched (PID {0})", processId?.ToString() ?? "unknown");

        // The startup dialog blocks loading until someone answers it, so it is polled while the
        // editor boots: either it shows up within the window, or it never will and loading has
        // already begun.
        bool answered = false;
        for (int elapsed = 0; elapsed < DialogWaitSeconds; elapsed += DialogPollSeconds)
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(DialogPollSeconds));
            answered = ConfirmDialog.TryClick(processId,
                message => logger?.Info(null, "[Engine]   {0}", message));
            progress?.Invoke(StartStage.Dialog, DialogWaitSeconds - (elapsed + DialogPollSeconds));

            if (answered)
            {
                break;
            }
        }

        logger?.Info(null, answered
            ? "[Engine] Startup dialog answered"
            : "[Engine] No startup dialog appeared");

        for (int elapsed = 0; elapsed < OnlineWaitSeconds; elapsed += OnlinePollSeconds)
        {
            if (IsOnline(call("editor", Status())))
            {
                logger?.Info(null, "[Engine] Editor is online");
                return new StartOutcome(true, true,
                    $"Editor online (PID {processId?.ToString() ?? "unknown"})");
            }

            progress?.Invoke(StartStage.Online, OnlineWaitSeconds - elapsed);
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(OnlinePollSeconds));
        }

        logger?.Error(null, "[Engine] Editor did not come online within {0}s", OnlineWaitSeconds);
        return new StartOutcome(false, true,
            $"Editor did not come online within {OnlineWaitSeconds}s");
    }

    /// <summary>
    /// Closes the editor, saving first: dirty assets make the engine raise its save prompt instead
    /// of quitting, and the window then never closes.
    /// </summary>
    /// <returns>The <c>quit_editor</c> result — the save result is folded into the log, not the return,
    /// because a failed save must not stop the close.</returns>
    internal static async Task<ToolResult> StopAsync(ToolCall call, ILogger? logger = null)
    {
        ToolResult save = call("editor", new Dictionary<string, object>
        {
            ["action"] = "save_asset",
            ["path"] = BlueprintTestHelper.ProjectPath,
            ["payload"] = new Dictionary<string, object> { ["all"] = true },
        });

        if (save.Success)
        {
            logger?.Info(null, "[Engine] Dirty assets saved");
        }
        else
        {
            logger?.Warn(null, "[Engine] Failed to save dirty assets: {0}", save.Message);
        }

        ToolResult quit = call("editor", new Dictionary<string, object>
        {
            ["action"] = "quit_editor",
            ["path"] = BlueprintTestHelper.ProjectPath,
        });

        if (quit.Success)
        {
            logger?.Info(null, "[Engine] quit_editor sent");
        }
        else
        {
            logger?.Warn(null, "[Engine] quit_editor failed: {0}", quit.Message);
        }

        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(QuitWaitSeconds));
        return quit;
    }

    /// <summary>Editor process id carried by a successful <c>launch</c> answer (Data.processPid).</summary>
    internal static int? ReadProcessId(ToolResult launchResult)
    {
        int? processId = ReadInt32(launchResult, "processPid");
        return processId is > 0 ? processId : null;
    }

    private static Dictionary<string, object> Status() => new() { ["action"] = "status" };

    /// <summary>
    /// <c>status</c> always succeeds — the answer is its <c>online</c> flag, so a failed call means
    /// "not online", never "unknown".
    /// </summary>
    private static bool IsOnline(ToolResult status)
        => status.Success && ReadBoolean(status, "online") == true;

    /// <summary>
    /// Reads one field of a tool result's data: an anonymous object on the direct path, a JSON
    /// element when the result came back through the bridge. Both shapes occur, so both are read.
    /// </summary>
    private static object? ReadField(ToolResult result, string name)
    {
        object? data = result.Data;
        if (data is null)
        {
            return null;
        }

        if (data is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Object
                   && element.TryGetProperty(name, out JsonElement value)
                ? value
                : null;
        }

        try
        {
            return data.GetType().GetProperty(name)?.GetValue(data);
        }
        catch (Exception)
        {
            // An unreadable shape is reported as "absent" by the caller, which is what it is.
            return null;
        }
    }

    private static bool? ReadBoolean(ToolResult result, string name)
        => ReadField(result, name) switch
        {
            bool flag => flag,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => null,
        };

    private static int? ReadInt32(ToolResult result, string name)
        => ReadField(result, name) switch
        {
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out int number)
                => number,
            _ => null,
        };
}
