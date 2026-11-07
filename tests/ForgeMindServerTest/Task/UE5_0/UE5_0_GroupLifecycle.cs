// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Compat;

namespace ForgeMindServer.Test.Task.UE5_0;

// Namespace Task conflicts with System.Threading.Tasks.Task type; resolve with explicit alias
using Task = System.Threading.Tasks.Task;

/// <summary>
/// UE5.0 Group Setup: Delete Tests folder -> Delay 10s -> Launch editor -> Delay 60s -> Check online status.
/// Order = 0, runs before all test cases in this group.
/// </summary>
[MustRun]
public sealed class UE5_0_LaunchEditor : ITestCase
{
    private const string ProjectPath =
        @"d:\ForgeMind\UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject";

    public string Name => "Launch ForgeMind_5_0 Editor";
    public string? Group => "UE5.0";
    public int Order => 0;

    private const string TestsDir =
        @"d:\ForgeMind\UEProject\ForgeMind_5_0\ForgeMind_5_0\Content\Tests";

    /// <summary>
    /// Editor autosaves of a previous run: when this folder has content the editor may stop at
    /// the "recover from autosave" path on startup instead of opening the project, so it is
    /// wiped along with Content\Tests. Derived from <see cref="ProjectPath"/> so the two can
    /// never drift apart.
    /// </summary>
    private static readonly string AutosavesDir =
        Path.Combine(Path.GetDirectoryName(ProjectPath)!, "Saved", "Autosaves");

    public async Task Run(TestContext ctx)
    {
        // ── 1. Delete Content/Tests folder (clean up leftover uasset from previous test) ──
        if (Directory.Exists(TestsDir))
        {
            ctx.Logger.Info(null, "[Test]   Delete Tests folder: {0}", TestsDir);
            Directory.Delete(TestsDir, recursive: true);
        }
        else
        {
            ctx.Logger.Info(null, "[Test]   Tests folder does not exist, skip deletion");
        }

        // ── 1.5. Delete Saved/Autosaves folder (leftover autosaves block editor startup) ──
        if (Directory.Exists(AutosavesDir))
        {
            ctx.Logger.Info(null, "[Test]   Delete Autosaves folder: {0}", AutosavesDir);
            try
            {
                Directory.Delete(AutosavesDir, recursive: true);
            }
            catch (Exception ex)
            {
                // A lingering editor process may still hold a file. Warn and continue instead of
                // aborting the whole group -- a real startup failure surfaces at the launch step.
                ctx.Logger.Warn(null, "[Test]   Failed to delete Autosaves folder: {0}", ex.Message);
            }
        }
        else
        {
            ctx.Logger.Info(null, "[Test]   Autosaves folder does not exist, skip deletion");
        }

        // ── 2. Build project (ensure C++ plugin changes take effect) ──
        ctx.Logger.Info(null, "[Test]   Building ForgeMind_5_0 project...");
        ToolResult buildResult = ctx.CallTool("build", new()
        {
            ["path"] = ProjectPath,
            ["target"] = "editor",
            ["configuration"] = "development",
        });
        ctx.AssertSuccess(buildResult, "build project");
        ctx.Logger.Info(null, "[Test]   Build completed");

        // ── 3. Delay 10s to ensure build artifacts are written ──
        ctx.Logger.Info(null, "[Test]   Waiting 10 seconds...");
        await Task.Delay(TimeSpan.FromSeconds(10));

        // ── 3.5. Reset convertAssignmentOperatorsToBlueprintMacros to default PureNodes ──
        ctx.Logger.Info(null, "[Test]   Reset convertAssignmentOperatorsToBlueprintMacros to default PureNodes");
        ToolResult resetResult = ctx.CallTool("settings", new()
        {
            ["action"] = "set",
            ["key"] = "convertAssignmentOperatorsToBlueprintMacros",
            ["value"] = "PureNodes",
        });
        ctx.AssertSuccess(resetResult, "reset convertAssignmentOperatorsToBlueprintMacros");

        // ── 4. Launch the editor and wait until it is usable ──
        // Shared with the --Demo scripts: launch, answer the startup dialog (it may sit behind
        // other windows, so every window of the launch PID is checked), then poll the bridge
        // until it reports the editor online. An editor that is already online is reused.
        EngineSession.StartOutcome outcome = await EngineSession.StartAsync(ctx.TryCallTool, ctx.Logger);
        ctx.AssertTrue(outcome.Success, outcome.Message);
        ctx.Logger.Info(null, "[Test]   Editor is ready");
    }
}

/// <summary>
/// UE5.0 Group Teardown: quit_editor closes editor.
/// Order = int.MaxValue, runs after all test cases in this group.
/// </summary>
[MustRun]
public sealed class UE5_0_QuitEditor : ITestCase
{
    public string Name => "Close ForgeMind_5_0 Editor";
    public string? Group => "UE5.0";
    public int Order => int.MaxValue;

    public async Task Run(TestContext ctx)
    {
        // ── Close the editor ──
        // The shared close saves first: assets created through the DSL are marked dirty but not
        // written (ir["save"] defaults to false), and a dirty package makes the engine raise its
        // save prompt instead of quitting. Then quit_editor is sent, and the process is given time
        // to go away before anything else touches the project.
        ToolResult result = await EngineSession.StopAsync(ctx.TryCallTool, ctx.Logger);
        if (!result.Success)
        {
            ctx.Logger.Warn(null, "[Test]   Editor may still be running: {0}", result.Message);
        }

        // ── Reset convertAssignmentOperatorsToBlueprintMacros to default PureNodes ──
        ctx.Logger.Info(null, "[Test]   Reset convertAssignmentOperatorsToBlueprintMacros to default PureNodes");
        ToolResult resetResult = ctx.TryCallTool("settings", new()
        {
            ["action"] = "set",
            ["key"] = "convertAssignmentOperatorsToBlueprintMacros",
            ["value"] = "PureNodes",
        });
        if (resetResult.Success)
        {
            ctx.Logger.Info(null, "[Test]   Settings have been reset to default");
        }
        else
        {
            ctx.Logger.Warn(null, "[Test]   Failed to reset settings: {0}", resetResult.Message);
        }

        ctx.Logger.Info(null, "[Test]   Editor closed successfully");
    }
}
