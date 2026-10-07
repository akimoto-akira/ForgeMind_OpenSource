// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.Base;

using ForgeMindServer.Compat;
using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 1 - Engine List (Base) - list all locally installed Unreal engines
// =========================================================================

public sealed class Engine_List : ITestCase
{
    public string Name => "Engine List (installed engines)";
    public string? Group => "Base";
    public int Order => 1;

    /// <summary>
    /// Baseline: exactly these engine installations (appName, installLocation) must be
    /// detected — fewer (missing) and more (unexpected) entries both fail the case, with
    /// every difference reported in one message. Captured from the 2026-10-06 run log.
    /// Launcher records whose appName does not start with "UE_" are engine plugins
    /// (FabPlugin_* / QuixelBridge_* — NOT engines themselves) and are filtered out
    /// before the diff. AppVersion is deliberately not asserted (build numbers change
    /// on engine updates) and is only logged for visual inspection.
    /// </summary>
    private static readonly (string AppName, string InstallLocation)[] ExpectedEngines =
    [
        ("UE_5.0", @"D:\Program Files\Epic Games\UE_5.0"),
        ("UE_5.1", @"D:\Program Files\Epic Games\UE_5.1"),
        ("UE_5.2", @"D:\Program Files\Epic Games\UE_5.2"),
        ("UE_5.3", @"D:\Program Files\Epic Games\UE_5.3"),
        ("UE_5.4", @"D:\Program Files\Epic Games\UE_5.4"),
        ("UE_5.5", @"D:\Program Files\Epic Games\UE_5.5"),
        ("UE_5.6", @"D:\Program Files\Epic Games\UE_5.6"),
        ("UE_5.7", @"D:\Program Files\Epic Games\UE_5.7"),
        ("UE_5.8", @"D:\Program Files\Epic Games\UE_5.8"),
    ];

    public Task Run(TestContext ctx)
    {
        // The engine tool's 'list' action enumerates engines registered by the
        // Epic Launcher (LauncherInstalled.dat) and source builds in the Windows
        // registry (HKCU\SOFTWARE\Epic Games\Unreal Engine\Builds).
        ctx.Logger.Info(null, "[Test]   Listing all installed Unreal engines");
        ToolResult result = ctx.CallTool("engine", new()
        {
            ["action"] = "list",
        });
        ctx.AssertSuccess(result, "engine list");

        List<(string AppName, string AppVersion, string InstallLocation)> actual = ReadEngines(result);
        ctx.Logger.Info(null, "[Test]   Found {0} installed engine(s)", actual.Count);
        LogEngines(ctx, actual);

        // Filter to engines themselves: launcher records whose appName does not start
        // with "UE_" are engine plugins (FabPlugin_* / QuixelBridge_*), not engines —
        // they are logged above but excluded from the baseline diff. Same predicate as
        // the host's DiscoverEngines primary-engine selection.
        List<(string AppName, string AppVersion, string InstallLocation)> engines = actual
            .Where(e => e.AppName.StartsWith("UE_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        ctx.Logger.Info(null, "[Test]   {0} of {1} entries are engines themselves (rest are engine plugins)",
            engines.Count, actual.Count);

        // Baseline diff: missing and unexpected entries are both collected, so a
        // single run exposes the whole delta instead of only the first mismatch.
        HashSet<(string, string)> expectedSet =
            new(ExpectedEngines.Select(e => (e.AppName, e.InstallLocation)));
        HashSet<(string, string)> engineSet =
            new(engines.Select(e => (e.AppName, e.InstallLocation)));

        List<(string AppName, string InstallLocation)> missing = ExpectedEngines
            .Where(e => !engineSet.Contains((e.AppName, e.InstallLocation)))
            .ToList();
        List<(string AppName, string AppVersion, string InstallLocation)> extra = engines
            .Where(e => !expectedSet.Contains((e.AppName, e.InstallLocation)))
            .ToList();

        var problems = new List<string>();
        if (missing.Count > 0)
        {
            problems.Add($"missing {missing.Count} engine(s): "
                + string.Join(", ", missing.Select(e => $"{e.AppName} @ {e.InstallLocation}")));
        }
        if (extra.Count > 0)
        {
            problems.Add($"unexpected {extra.Count} engine(s): "
                + string.Join(", ", extra.Select(e => $"{e.AppName} @ {e.InstallLocation}")));
        }

        // Fewer (missing) or more (unexpected) than the baseline both fail.
        ctx.AssertTrue(missing.Count == 0 && extra.Count == 0,
            problems.Count > 0 ? string.Join("; ", problems) : "engine baseline mismatch");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Read engine entries (appName / appVersion / installLocation) from the tool
    /// result (Data.engines array) via reflection — the tool returns anonymous-typed
    /// data (same approach as the launch-PID reader in the group lifecycle case).
    /// </summary>
    private static List<(string AppName, string AppVersion, string InstallLocation)> ReadEngines(ToolResult result)
    {
        var entries = new List<(string AppName, string AppVersion, string InstallLocation)>();

        if (ReadDataProperty(result, "engines") is not System.Collections.IEnumerable list)
        {
            return entries;
        }

        foreach (object? engine in list)
        {
            if (engine is null)
            {
                continue;
            }

            entries.Add((
                ReadStringProperty(engine, "appName") ?? "",
                ReadStringProperty(engine, "appVersion") ?? "",
                ReadStringProperty(engine, "installLocation") ?? ""));
        }

        return entries;
    }

    /// <summary>
    /// Log each engine entry (appName / appVersion / installLocation) so the run
    /// log carries the full installation list for visual inspection.
    /// </summary>
    private static void LogEngines(TestContext ctx, List<(string AppName, string AppVersion, string InstallLocation)> entries)
    {
        foreach (var entry in entries)
        {
            ctx.Logger.Info(null, "[Test]     [{0}] {1} @ {2}",
                entry.AppName, entry.AppVersion, entry.InstallLocation);
        }
    }

    private static object? ReadDataProperty(ToolResult result, string name)
    {
        return result.Data is null ? null : ReadProperty(result.Data, name);
    }

    private static string? ReadStringProperty(object source, string name)
    {
        return ReadProperty(source, name)?.ToString();
    }

    private static object? ReadProperty(object source, string name)
    {
        try
        {
            return source.GetType().GetProperty(name)?.GetValue(source);
        }
        catch (Exception)
        {
            // Reflection failure is treated as a missing property, not a test failure
            return null;
        }
    }
}