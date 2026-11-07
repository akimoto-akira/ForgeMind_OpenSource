// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo;

/// <summary>
/// One turn of a demo script: what the "AI" says (both languages) and what it actually
/// does — a tool call, or a built-in wait. Instances come only from <see cref="DemoStep"/>,
/// so a script never touches tool lookup or execution mechanics.
/// </summary>
public sealed class DemoTurn
{
    internal DemoTurn(string zh, string en, string? toolName,
        IReadOnlyDictionary<string, object>? parameters, int? waitSeconds,
        bool optional = false, string[]? resultKeys = null)
    {
        Zh = zh;
        En = en;
        ToolName = toolName;
        Parameters = parameters;
        WaitSeconds = waitSeconds;
        Optional = optional;
        ResultKeys = resultKeys;
    }

    /// <summary>The instruction text shown when the run is in Chinese.</summary>
    public string Zh { get; }

    /// <summary>The instruction text shown when the run is in English.</summary>
    public string En { get; }

    /// <summary>
    /// A turn whose failure is not a failure: clearing the previous recording's asset fails on the
    /// first run (there is nothing to clear) and that must read as "nothing to do", not as an error.
    /// </summary>
    public bool Optional { get; }

    /// <summary>Tool to invoke; <c>null</c> marks the built-in wait instruction.</summary>
    public string? ToolName { get; }

    /// <summary>Tool parameters, passed through unchanged.</summary>
    public IReadOnlyDictionary<string, object>? Parameters { get; }

    /// <summary>Wait length in seconds; non-null only for the built-in wait instruction.</summary>
    public int? WaitSeconds { get; }

    /// <summary>
    /// The answer fields this turn exists to show, by name — <c>nodeId</c>, <c>compiled</c>,
    /// <c>sessions</c>. The script is the only thing that knows which part of an answer is the point
    /// of the turn, so it says so here and the runner prints exactly those; a turn that names none
    /// shows its one-line summary and nothing else. Dumping the whole reply is not a confirmation:
    /// it buries the two or three fields that prove the work in a wall of bookkeeping the every-turn
    /// fields (<c>projectFile</c>, <c>action</c>, <c>dirty</c>) repeat anyway.
    /// </summary>
    public string[]? ResultKeys { get; }

    /// <summary>Picks the text for the requested language ("zh" / "en").</summary>
    public string Text(string language)
        => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? En : Zh;
}
