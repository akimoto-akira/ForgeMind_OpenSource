// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using System.Text.Json;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Test.Demo;

/// <summary>
/// The <c>--Demo</c> flow: play one built-in script on the console.
/// <para>The terminal is the recording surface. It is also the honest one: ForgeMind's interface is
/// MCP tool calls, and a terminal printing <c>指令 → 工具 / 状态 / 结果字段</c> is what an agent
/// session actually looks like, while a hand-built chat window only imitates that look and adds a
/// message pump, a layout pass and a UI thread that all have to be kept alive to show the same
/// three lines.</para>
/// <para>As before, a demo answers for nothing: it writes no log and reports no error — the only
/// errors printed are command-line usage errors, which are not demo content. What it does share with
/// everything else is the real machinery: the host services and <see cref="ITool"/>, so a demo
/// exercises the same path as the test cases.</para>
/// <para>Pacing is a plain <see cref="System.Threading.Tasks.Task.Delay"/> in a plain loop, and the
/// waiting numbers come from a stopwatch. Nothing here waits on a message pump, so a run can be
/// neither stretched by an idle console nor compressed by a burst of due timers — the two ways the
/// recorded length used to drift.</para>
/// </summary>
public static class DemoRunner
{
    /// <summary>How long a turn's line holds before the next one is written.</summary>
    private static readonly TimeSpan TurnPause = TimeSpan.FromMilliseconds(400);

    /// <summary>How often a wait reports what it is still waiting for.</summary>
    private static readonly TimeSpan CountdownPause = TimeSpan.FromSeconds(1);

    /// <summary>Entry point: <c>--Demo &lt;脚本序号&gt; [zh|en]</c>.</summary>
    public static int Run(string[] args)
    {
        if (!TryParse(args, out int order, out string language, out string? usageError))
        {
            Console.Error.WriteLine(usageError);
            return 1;
        }

        IDemoScript? script = DiscoverScripts().FirstOrDefault(candidate => candidate.Order == order);
        if (script is null)
        {
            Console.Error.WriteLine($"[Demo] 脚本序号 {order} 不存在");
            return 1;
        }

        ForgeMindServerHost.Start();
        ForgeMindServerHost.EnsurePluginsLoaded();
        try
        {
            PlayAsync(script, language, ForgeMindServerHost.Tools).GetAwaiter().GetResult();
        }
        finally
        {
            ForgeMindServerHost.Stop();
        }

        return 0;
    }

    private static bool TryParse(string[] args, out int order, out string language, out string? error)
    {
        order = 0;
        language = "zh";
        error = null;

        // The script number has no default: without it nothing is played.
        if (args.Length < 2)
        {
            error = "[Demo] 缺少脚本序号。用法: ForgeMindServerTest --Demo <脚本序号> [zh|en]";
            return false;
        }

        if (!int.TryParse(args[1], out order))
        {
            error = $"[Demo] 脚本序号必须是整数: '{args[1]}'";
            return false;
        }

        if (args.Length >= 3)
        {
            string raw = args[2];
            if (string.Equals(raw, "zh", StringComparison.OrdinalIgnoreCase))
            {
                language = "zh";
            }
            else if (string.Equals(raw, "en", StringComparison.OrdinalIgnoreCase))
            {
                language = "en";
            }
            else
            {
                error = $"[Demo] 语言只能是 zh 或 en: '{raw}'";
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<IDemoScript> DiscoverScripts()
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDemoScript).IsAssignableFrom(type))
            .Select(type => (IDemoScript)Activator.CreateInstance(type)!);
    }

    /// <summary>
    /// Plays a script with the editor it needs: the engine session opens before the first turn and
    /// closes after the last one, and a session that never came up ends the run instead of playing
    /// turns that could only fail. The close sits in a <c>finally</c> because a demo that leaves the
    /// editor standing blocks the next run's build and its launch.
    /// </summary>
    private static async System.Threading.Tasks.Task PlayAsync(IDemoScript script, string language,
        IReadOnlyList<ITool> tools)
    {
        Write(Header(script, language), Dim);

        var history = new List<ToolResult>();
        try
        {
            if (script.ManagesEngine && !await StartEngineAsync(script, language, tools))
            {
                return;
            }

            foreach (DemoTurn turn in script.Lines)
            {
                string instruction = turn.ToolName is null
                    ? turn.Text(language)
                    : $"{turn.Text(language)} → {turn.ToolName}";
                Write($"› {instruction}", Plain);

                if (turn.WaitSeconds is int seconds)
                {
                    await CountdownAsync(seconds, language);
                    continue;
                }

                // The call itself is synchronous down inside the bridge, so it goes to a worker and the
                // console keeps its own thread for printing.
                (ToolResult result, string answer) = await System.Threading.Tasks.Task.Run(
                    () =>
                    {
                        ToolResult answered = DemoStep.Execute(turn, tools, history);
                        return (answered, Answer(answered, turn.ResultKeys));
                    });

                history.Add(result);
                // An optional turn finding nothing to do is the expected outcome, not a failure.
                bool skipped = turn.Optional && !result.Success;
                Write($"  {Status(skipped ? "skipped" : result.Success ? "succeeded" : "failed", language)}"
                    + $"  {answer}", skipped ? Dim : result.Success ? Good : Bad);
                await System.Threading.Tasks.Task.Delay(TurnPause);
            }
        }
        finally
        {
            if (script.ManagesEngine)
            {
                await StopEngineAsync(script, language, tools);
            }
        }
    }

    /// <summary>The run's identity, so a recording says which script it is showing.</summary>
    private static string Header(IDemoScript script, string language)
        => English(language)
            ? $"ForgeMind Visual Demo · script {script.Order} · {script.Name}"
            : $"ForgeMind可视化演示程序 · 脚本 {script.Order} · {script.Name}";

    /// <summary>
    /// Starts the editor: the line counts the wait down while the start polls, then reports the
    /// process it got online.
    /// </summary>
    /// <returns>False when no usable editor exists afterwards, which ends the run.</returns>
    private static async System.Threading.Tasks.Task<bool> StartEngineAsync(IDemoScript script,
        string language, IReadOnlyList<ITool> tools)
    {
        Write($"› {script.StartText(language)}", Plain);

        EngineSession.StartOutcome outcome = await script.StartEngineAsync(tools,
            (stage, remaining) => WriteInPlace($"  {script.StageText(stage, remaining, language)}"));

        Write($"  {Status(outcome.Success ? "succeeded" : "failed", language)}  {outcome.Message}",
            outcome.Success ? Good : Bad);
        return outcome.Success;
    }

    /// <summary>
    /// Closes the editor. A close that fails is reported, not thrown: the run is ending anyway and the
    /// recording should say what actually happened.
    /// </summary>
    private static async System.Threading.Tasks.Task StopEngineAsync(IDemoScript script,
        string language, IReadOnlyList<ITool> tools)
    {
        Write($"› {script.StopText(language)}", Plain);

        try
        {
            ToolResult result = await System.Threading.Tasks.Task.Run(() => script.StopEngineAsync(tools));
            // What the session's close returns is the quit answer, so the fields that prove it are the
            // same ones script 0 names on its own closing turn.
            Write($"  {Status(result.Success ? "succeeded" : "failed", language)}  "
                + $"{Answer(result, ["exitRequested", "note"])}", result.Success ? Good : Bad);
        }
        catch (Exception exception)
        {
            Write($"  {Status("failed", language)}  {exception.Message}", Bad);
        }
    }

    /// <summary>
    /// Counts a built-in wait down in place. The number shown comes from elapsed time, not from how
    /// many delays have been taken, so a slow console cannot stretch the wait and a fast one cannot
    /// shorten it.
    /// </summary>
    private static async System.Threading.Tasks.Task CountdownAsync(int seconds, string language)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int remaining = seconds;
        while (remaining > 0)
        {
            WriteInPlace($"  {remaining}s");
            await System.Threading.Tasks.Task.Delay(CountdownPause);
            remaining = seconds - (int)clock.Elapsed.TotalSeconds;
        }

        WriteInPlace(string.Empty);
        Write($"  {Status("succeeded", language)}", Good);
    }

    /// <summary>
    /// What a turn puts on screen: the tool's own summary line, then the answer fields the script
    /// named, one per line. A turn that names none shows the summary alone — the block is there to
    /// confirm the work, not to reprint the reply, and the fields every turn repeats
    /// (<c>projectFile</c>, <c>action</c>, <c>dirty</c>) are the ones nobody came to look at.
    /// </summary>
    private static string Answer(ToolResult result, string[]? keys)
    {
        string summary = result.Message ?? string.Empty;
        if (keys is null || keys.Length == 0 || result.Data is null)
        {
            return summary;
        }

        JsonElement root = result.Data is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(result.Data);

        // The tool wraps the companion's answer in an envelope, so the named fields live one level in.
        // Both levels are read and the inner one wins, which lets a script name either.
        JsonElement response = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("response", out JsonElement inner)
            && inner.ValueKind == JsonValueKind.Object
            ? inner
            : root;

        List<string> parts = [];
        foreach (string key in keys)
        {
            if (response.TryGetProperty(key, out JsonElement found)
                || (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out found)))
            {
                parts.Add($"{key}: {Brief(found, 0)}");
                continue;
            }

            // A field the answer does not carry is shown as absent rather than dropped: a silently
            // missing line is exactly what a confirmation is supposed to rule out.
            parts.Add($"{key}: —");
        }

        return summary + "\n    " + string.Join("\n    ", parts);
    }

    /// <summary>
    /// One value on one line, with no language in it: a count and a bracketed head for containers, cut
    /// short rather than wrapped — a long asset path broken in half reads as nothing at all, and a
    /// nested tree the reader has to unwrap is not an answer.
    /// </summary>
    private static string Brief(JsonElement value, int depth)
    {
        string text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "—",
            JsonValueKind.Array when depth == 0 => $"[{value.GetArrayLength()}] "
                + string.Join(", ", value.EnumerateArray().Take(3).Select(item => Brief(item, 1)))
                + (value.GetArrayLength() > 3 ? " …" : string.Empty),
            JsonValueKind.Object when depth == 0 => "{" + string.Join(", ",
                value.EnumerateObject().Take(4).Select(pair => $"{pair.Name}={Brief(pair.Value, 1)}")) + "}",
            _ => value.GetRawText(),
        };

        return text.Length <= 140 ? text : text[..140] + " …";
    }

    /// <summary>Status wording, in the language the run was started with.</summary>
    private static string Status(string kind, string language)
    {
        bool english = English(language);
        return kind switch
        {
            "running" => english ? "Running" : "执行中",
            "failed" => english ? "Failed" : "执行失败",
            "skipped" => english ? "Skipped" : "已跳过",
            _ => english ? "Succeeded" : "执行成功",
        };
    }

    private static bool English(string language)
        => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

    private const ConsoleColor Plain = ConsoleColor.Gray;
    private const ConsoleColor Dim = ConsoleColor.DarkGray;
    private const ConsoleColor Good = ConsoleColor.Green;
    private const ConsoleColor Bad = ConsoleColor.Red;

    /// <summary>Writes a line, coloured when there is a real console to colour.</summary>
    private static void Write(string text, ConsoleColor color)
    {
        foreach (string line in text.Split('\n'))
        {
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine(line);
                continue;
            }

            ConsoleColor before = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = before;
        }
    }

    /// <summary>
    /// Replaces the current line in place — the countdown and the waiting notes. A redirected run
    /// gets each update on its own line instead, because there is no cursor to move.
    /// </summary>
    private static void WriteInPlace(string text)
    {
        if (Console.IsOutputRedirected)
        {
            if (text.Length > 0)
            {
                Console.WriteLine(text.TrimStart());
            }

            return;
        }

        // The padding erases what the previous update left behind; the trailing carriage return keeps
        // the cursor at the start of the line so the next update overwrites rather than appends.
        Console.Write("\r" + text + new string(' ', 16) + "\r");
    }
}
