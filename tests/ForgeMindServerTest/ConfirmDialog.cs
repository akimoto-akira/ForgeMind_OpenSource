// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Runtime.InteropServices;
using System.Text;

namespace ForgeMindServer.Test;

/// <summary>
/// Answers the editor's startup confirmation dialog ("Warning: Known issues with graphics
/// driver" and its siblings) by sending BM_CLICK to the dialog's affirmative button.
/// <para>Every path that launches the editor meets the same dialog: the UE5.0 group lifecycle
/// case and the <c>--Demo</c> scripts start the same project through the same <c>launch</c> tool.
/// The detection therefore lives here, once — a second copy would drift the moment one of them
/// learned a new window shape.</para>
/// </summary>
internal static class ConfirmDialog
{
    /// <summary>Reports what one round found, for a caller that wants it in its log.</summary>
    internal delegate void Note(string message);

    /// <summary>
    /// One round of detection: the foreground window first (the usual case — the dialog is in
    /// front), then every top-level window of <paramref name="processId"/>, because the dialog
    /// can sit behind other windows while the editor is not the active process.
    /// </summary>
    internal static bool TryClick(int? processId, Note? note = null)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero && ClickInWindow(foreground, note))
        {
            return true;
        }

        if (processId is int pid)
        {
            IntPtr clicked = NativeMethods.FindWindowByPid(pid, window => ClickInWindow(window, note));
            if (clicked != IntPtr.Zero)
            {
                return true;
            }
        }

        note?.Invoke("No window with an affirmative button yet");
        return false;
    }

    /// <summary>
    /// Walks the child controls of one window and clicks the first Button whose text is an
    /// affirmative answer. The button texts are the engine's own UI strings, read from
    /// <see cref="ChineseUiStrings"/> rather than duplicated here.
    /// </summary>
    private static bool ClickInWindow(IntPtr window, Note? note)
    {
        string title = NativeMethods.GetWindowText(window);
        IntPtr found = IntPtr.Zero;

        NativeMethods.EnumChildWindows(window, (child, _) =>
        {
            if (!string.Equals(NativeMethods.GetClassName(child), "Button", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string text = NativeMethods.GetWindowText(child);
            if (text.Contains(ChineseUiStrings.DialogOk, StringComparison.Ordinal)
                || text.Contains(ChineseUiStrings.DialogConfirm, StringComparison.Ordinal))
            {
                found = child;
                return false; // One affirmative button is enough — stop walking this window.
            }

            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.SendMessage(found, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        note?.Invoke($"Window '{title}' answered with button hwnd=0x{found.ToInt64():X} (BM_CLICK sent)");
        return true;
    }
}

/// <summary>Win32 user32.dll interop: window enumeration, text reading and button clicking.</summary>
internal static class NativeMethods
{
    /// <summary>BM_CLICK: simulate a button click.</summary>
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

    /// <summary>Reads window text (a title or a button caption).</summary>
    public static string GetWindowText(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Reads a window class name.</summary>
    public static string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        _ = GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Enumerates all top-level windows; for each one belonging to <paramref name="pid"/> the
    /// <paramref name="predicate"/> is asked, and the first window answering true is returned.
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
                        return false; // Stop enumeration after the match.
                    }
                }
                catch (Exception)
                {
                    // One unreadable window must not stop the walk over the others.
                }
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }
}
