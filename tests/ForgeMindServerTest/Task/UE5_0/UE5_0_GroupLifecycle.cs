// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Runtime.InteropServices;
using System.Text;
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

        // ── 4. Launch editor ──
        ctx.Logger.Info(null, "[Test]   Launching ForgeMind_5_0 editor");
        ToolResult launchResult = ctx.CallTool("launch", new()
        {
            ["path"] = ProjectPath,
            ["mode"] = "editor",
        });

ctx.AssertSuccess(launchResult, "launch editor");

// ── 5. Delay 60s, poll and click launch confirmation dialog ──
        // Dialog (e.g., "Warning: Known issues with graphics driver") may appear in foreground
        // or be obscured by other windows; enumerate top-level windows by PID from launch result,
        // find and click button with text containing "OK/Confirm".
        int? editorPid = ReadProcessPid(launchResult);
        ctx.Logger.Info(null, "[Test]   Waiting 60s and polling confirmation dialog (launch PID={0})...",
            editorPid?.ToString() ?? "unknown");

        const int totalSeconds = 60;
        const int intervalSeconds = 10;
        bool confirmClicked = false;
        for (int elapsed = 0; elapsed < totalSeconds && !confirmClicked; elapsed += intervalSeconds)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
            confirmClicked = TryClickConfirmDialog(ctx, editorPid);
        }

        ctx.Logger.Info(null,
            confirmClicked
                ? "[Test]   Clicked 'OK/Confirm' button in dialog"
                : "[Test]   No dialog with 'OK/Confirm' button found in 60s");

        // ── 6. Delay another 60s to wait for editor to finish loading ──
        ctx.Logger.Info(null, "[Test]   Continuing to wait 60s for editor to finish loading...");
        await Task.Delay(TimeSpan.FromSeconds(60));

        // ── 7. Check editor online status ──
        ctx.Logger.Info(null, "[Test]   Checking editor online status");
        ToolResult statusResult = ctx.CallTool("editor", new()
        {
            ["action"] = "status",
        });

ctx.AssertField(statusResult, "data.online", expected: true);
        ctx.Logger.Info(null, "[Test]   Editor is ready");
    }

    /// <summary>
    /// Read editor process PID from launch tool result (Data.processPid).
    /// </summary>
    private static int? ReadProcessPid(ToolResult launchResult)
    {
        try
        {
            object? data = launchResult.Data;
            if (data is null)
            {
                return null;
            }

            var prop = data.GetType().GetProperty("processPid");
            if (prop is not null && prop.GetValue(data) is int pid && pid > 0)
            {
                return pid;
            }
        }
        catch (Exception)
        {
            // Fallback to "check foreground window only" when PID is unreadable; does not affect test continuation
        }

        return null;
    }

    /// <summary>
    /// Find and click launch confirmation dialog: check foreground window first, then enumerate
    /// all top-level windows for the editor process by PID; send BM_CLICK if any window has
    /// a button with text containing "OK/Confirm".
    /// </summary>
    private static bool TryClickConfirmDialog(TestContext ctx, int? editorPid)
    {
        // 1. Foreground window (most common scenario: dialog is in front)
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero && ClickConfirmButtonInWindow(ctx, foreground))
        {
            return true;
        }

        // 2. Enumerate all top-level windows by PID (dialog may not be in foreground)
        if (editorPid is int pid)
        {
            IntPtr clicked = NativeMethods.FindWindowByPid(pid, hwnd =>
                ClickConfirmButtonInWindow(ctx, hwnd));
            if (clicked != IntPtr.Zero)
            {
                return true;
            }
        }

        ctx.Logger.Info(null, "[Test]   No dialog window with 'OK/Confirm' button found in this round");
        return false;
    }

    /// <summary>
    /// Enumerate child controls within a single window; click the first Button with text containing "OK/Confirm".
    /// </summary>
    private static bool ClickConfirmButtonInWindow(TestContext ctx, IntPtr hwnd)
    {
        string title = NativeMethods.GetWindowText(hwnd);
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumChildWindows(hwnd, (child, _) =>
        {
            if (string.Equals(NativeMethods.GetClassName(child), "Button",
                    StringComparison.OrdinalIgnoreCase))
            {
                string text = NativeMethods.GetWindowText(child);
                if (text.Contains(ChineseUiStrings.DialogOk, StringComparison.Ordinal)
                    || text.Contains(ChineseUiStrings.DialogConfirm, StringComparison.Ordinal))
                {
                    found = child;
                    return false; // Stop enumeration after match
                }
            }

            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.SendMessage(found, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        ctx.Logger.Info(null, "[Test]   Window '{0}' has 'OK/Confirm' button hwnd=0x{1:X}, BM_CLICK sent",
            title, found.ToInt64());
        return true;
    }
}

/// <summary>
/// Win32 user32.dll interop declarations (for dialog detection and button clicking).
/// </summary>
internal static class NativeMethods
{
    /// <summary>BM_CLICK: Simulate button click.</summary>
    public const uint BM_CLICK = 0x00F5;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Read window text (title or button text).</summary>
    public static string GetWindowText(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Read window class name.</summary>
    public static string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        _ = GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Enumerate all top-level windows; for windows belonging to <paramref name="pid"/>, invoke
    /// <paramref name="predicate"/>; return the first window handle that returns true.
    /// </summary>
    public static IntPtr FindWindowByPid(int pid, Func<IntPtr, bool> predicate)
    {
        IntPtr result = IntPtr.Zero;
        _ = EnumWindows((hwnd, lparam) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid == (uint)pid)
            {
                try
                {
                    if (predicate(hwnd))
                    {
                        result = hwnd;
                        return false; // Stop enumeration after match
                    }
                }
                catch (Exception)
                {
                    // Exception in processing a single window does not affect other windows
                }
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }
}

/// <summary>
/// UE5.0 Group Teardown: quit_editor closes editor.
/// Order = int.MaxValue, runs after all test cases in this group.
/// </summary>
[MustRun]
public sealed class UE5_0_QuitEditor : ITestCase
{
    private const string ProjectPath =
        @"d:\ForgeMind\UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject";

    public string Name => "Close ForgeMind_5_0 Editor";
    public string? Group => "UE5.0";
    public int Order => int.MaxValue;

    public async Task Run(TestContext ctx)
    {
        // ── Save all dirty assets ──
        // Assets created by DSL have ir["save"] default false; not saved to disk but package is marked dirty.
        // When quit_editor is called, engine detects dirty unsaved assets and shows save dialog blocking close.
        // Use save_asset all:true to persist all dirty /Game packages first, then quit_editor.
        ctx.Logger.Info(null, "[Test]   Saving all dirty assets");
        ToolResult saveResult = ctx.TryCallTool("editor", new()
        {
            ["action"] = "save_asset",
            ["path"] = ProjectPath,
            ["payload"] = new { all = true },
        });
        if (saveResult.Success)
        {
            ctx.Logger.Info(null, "[Test]   Dirty assets saved");
        }
        else
        {
            ctx.Logger.Warn(null, "[Test]   Failed to save dirty assets (editor may be closed): {0}", saveResult.Message);
        }

        ctx.Logger.Info(null, "[Test]   Closing ForgeMind_5_0 editor");

        ToolResult result = ctx.TryCallTool("editor", new()
        {
            ["action"] = "quit_editor",
            ["path"] = ProjectPath,
        });

        if (result.Success)
        {
            ctx.Logger.Info(null, "[Test]   quit_editor sent");
        }
        else
        {
            ctx.Logger.Warn(null, "[Test]   quit_editor failed (editor may be closed): {0}", result.Message);
        }

        await Task.Delay(TimeSpan.FromSeconds(5));

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
