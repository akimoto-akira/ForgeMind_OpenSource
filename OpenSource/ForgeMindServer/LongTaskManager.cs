// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using ForgeMindServer.Bridge;
using ForgeMindServer.Compat;
using ForgeMindServer.Settings;

namespace ForgeMindServer;

/// <summary>
/// Long-task slot status (maps to the four get_task_result states in requirements.md §2.6).
/// </summary>
public enum LongTaskStatus
{
    /// <summary>Task is running, result not ready yet (AI keeps polling).</summary>
    Executing,

    /// <summary>Engine returned success.</summary>
    Completed,

    /// <summary>Engine returned an error.</summary>
    Failed,

    /// <summary>Engine did not respond within TTL (treated as failed).</summary>
    Expired
}

/// <summary>
/// Long-task slot: in-memory state of the MCP process, dies with a restart. Records the task GUID, status, target engine PID, and result.
/// </summary>
public sealed class LongTaskSlot
{
    /// <summary>Unique task identifier (GUID).</summary>
    public Guid TaskId { get; init; }

    /// <summary>Current status.</summary>
    public LongTaskStatus Status { get; set; } = LongTaskStatus.Executing;

    /// <summary>Name of the tool that triggered this task (for diagnostics).</summary>
    public string ToolName { get; init; } = "";

    /// <summary>Task creation time (UTC).</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Last data update time (UTC); used as the TTL start.</summary>
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Target engine process PID; 0 means unknown (no process-alive check possible; falls back to TTL reclamation).</summary>
    public int EnginePid { get; set; }

    /// <summary>Target project path (.uproject absolute path). The AI passes only the project path, never a PID;
    /// the server uses this to look up the engine PID via <see cref="ForgeMindServerBridgeServer"/>.</summary>
    public string ProjectPath { get; set; } = "";

    /// <summary>Whether the result is successful (valid only for Completed / Failed).</summary>
    public bool Success { get; set; }

    /// <summary>Result message (success summary or error description).</summary>
    public string Message { get; set; } = "";

    /// <summary>Structured result data (valid only for Completed / Failed).</summary>
    public object? Data { get; set; }
}

/// <summary>
/// Long-task manager (requirements.md §2.5 / §2.6). In-process singleton: owns all long-task slots and drives
/// a Watchdog background thread that performs TTL reclamation and engine-process liveness probing.
/// <para>Design guarantees: MCP never waits for the engine (releases the AI on timeout);
/// MCP never pushes (only responds reactively to <c>get_task_result</c> polls).</para>
/// </summary>
public sealed class LongTaskManager
{
    /// <summary>In-process shared instance.</summary>
    public static readonly LongTaskManager Instance = new();

    private readonly ConcurrentDictionary<Guid, LongTaskSlot> _slots = new();
    private Timer? _watchdog;
    private readonly object _watchdogLock = new();
    private readonly object _logger = LogManager.Instance.GetLogger<LongTaskManager>();

    private LongTaskManager() { }

    /// <summary>Current slot count.</summary>
    public int Count => _slots.Count;

    /// <summary>
    /// Creates a new long-task slot. Returns failure when <c>maxConcurrentLongTasks</c> is exceeded.
    /// </summary>
    public (bool ok, string? error) TryCreate(string toolName, string projectPath, out LongTaskSlot slot)
    {
        int max = GetInt(McpSettingsManager.MaxConcurrentLongTasksKey, 100);
        if (_slots.Count >= max)
        {
            slot = null!;
            return (false, $"max concurrent long tasks reached ({max})");
        }

        var s = new LongTaskSlot
        {
            TaskId = Guid.NewGuid(),
            ToolName = toolName,
            ProjectPath = projectPath,
            EnginePid = 0,
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow
        };
        _slots[s.TaskId] = s;
        EnsureWatchdog();
        slot = s;
        return (true, null);
    }

    /// <summary>Attempts to fetch a slot by GUID.</summary>
    public bool TryGet(Guid taskId, out LongTaskSlot slot) => _slots.TryGetValue(taskId, out slot!);

    /// <summary>Writes the result and marks the status after a task completes.</summary>
    public void Complete(LongTaskSlot slot, ToolResult result)
    {
        slot.Success = result.Success;
        slot.Message = result.Message;
        slot.Data = result.Data;
        slot.Status = result.Success ? LongTaskStatus.Completed : LongTaskStatus.Failed;
        slot.LastActivityUtc = DateTime.UtcNow;
    }

    /// <summary>Task failed (e.g. engine-process crash).</summary>
    public void MarkFailed(LongTaskSlot slot, string error)
    {
        slot.Success = false;
        slot.Message = error;
        slot.Status = LongTaskStatus.Failed;
        slot.LastActivityUtc = DateTime.UtcNow;
    }

    /// <summary>Removes a single slot (called when a poll retrieves a result, or for manual cleanup).</summary>
    public int PruneOne(Guid taskId) => _slots.TryRemove(taskId, out _) ? 1 : 0;

    /// <summary>Returns all slots still in the executing state (for diagnostics).</summary>
    public IReadOnlyList<LongTaskSlot> ListExecuting()    {
        var list = new List<LongTaskSlot>(_slots.Count);
        foreach (LongTaskSlot s in _slots.Values)
            if (s.Status == LongTaskStatus.Executing)
                list.Add(s);
        return list;
    }

    /// <summary>
    /// Marks all <see cref="LongTaskStatus.Executing"/> tasks related to a given project as failed
    /// (called by the Bridge when the editor disconnects / crashes). Returns the number of tasks marked.
    /// </summary>
    public int FailByProject(string projectFile, string reason)
    {
        if (string.IsNullOrEmpty(projectFile))
            return 0;

        string norm = Normalize(projectFile);
        int failed = 0;
        foreach (LongTaskSlot s in _slots.Values)
        {
            if (s.Status == LongTaskStatus.Executing &&
                !string.IsNullOrEmpty(s.ProjectPath) &&
                string.Equals(Normalize(s.ProjectPath), norm, StringComparison.OrdinalIgnoreCase))
            {
                s.Status = LongTaskStatus.Failed;
                s.Success = false;
                s.Message = reason;
                s.LastActivityUtc = DateTime.UtcNow;
                failed++;
            }
        }
        return failed;
    }

    /// <summary>
    /// Removes all completed / failed / expired slots and returns the removal count; only executing, non-expired slots remain.
    /// </summary>
    public int PruneAll()
    {
        int ttl = GetInt(McpSettingsManager.TaskResultTtlMsKey, 3_600_000);
        DateTime now = DateTime.UtcNow;
        int removed = 0;
        foreach (LongTaskSlot s in _slots.Values)
        {
            bool stale = (now - s.LastActivityUtc).TotalMilliseconds > ttl;
            if (s.Status != LongTaskStatus.Executing || stale)
            {
                if (_slots.TryRemove(s.TaskId, out _))
                    removed++;
            }
        }
        return removed;
    }

    // ===== Watchdog =====

    private void EnsureWatchdog()
    {
        lock (_watchdogLock)
        {
            if (_watchdog != null)
                return;

            int interval = GetInt(McpSettingsManager.EnginePidCheckIntervalMsKey, 120_000);
            _watchdog = new Timer(_ => WatchdogTick(), null, interval, interval);
        }
    }

    private void WatchdogTick()
    {
        try
        {
            int ttl = GetInt(McpSettingsManager.TaskResultTtlMsKey, 3_600_000);
            int interval = GetInt(McpSettingsManager.EnginePidCheckIntervalMsKey, 120_000);
            DateTime now = DateTime.UtcNow;

            foreach (LongTaskSlot s in _slots.Values)
            {
                if (s.Status == LongTaskStatus.Executing)
                {
                    if (!string.IsNullOrEmpty(s.ProjectPath))
                    {
                        // Server looks up the online editor session via Bridge by project path (AI does not pass a PID)
                        BridgeSession? session = ForgeMindServerHost.BridgeServer.GetSessionByProject(s.ProjectPath);
                        if (session is { IsReady: true })
                        {
                            s.EnginePid = session.EditorPid;
                            if (s.EnginePid > 0 && !IsProcessAlive(s.EnginePid))
                            {
                                s.Status = LongTaskStatus.Failed;
                                s.Success = false;
                                s.Message = $"Engine process [{s.EnginePid}] has exited (editor crash or network drop)";
                            }
                            else
                            {
                                s.LastActivityUtc = now; // Engine alive (Bridge session live) → refresh TTL to avoid reclamation
                            }
                        }
                        else
                        {
                            s.Status = LongTaskStatus.Failed;
                            s.Success = false;
                            s.Message = $"Engine session disconnected (no online editor for project {s.ProjectPath}) — editor may have crashed or lost network";
                            s.LastActivityUtc = now;
                        }
                    }
                    // ProjectPath empty: cannot associate an engine; fall back to TTL reclamation
                }

                // TTL reclamation: data not updated for too long → destroy the slot (executing and overdue is treated as expired)
                if ((now - s.LastActivityUtc).TotalMilliseconds > ttl)
                    _slots.TryRemove(s.TaskId, out _);
            }

            // Settings may have changed; adjust the next probe interval
            _watchdog?.Change(interval, interval);
        }
        catch (Exception ex)
        {
            if (_logger is ILogger logger)
                logger.Warn(null, "[LongTask] Watchdog tick failed: {0}", ex.Message);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p is { HasExited: false };
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int GetInt(string key, int fallback)
    {
        try
        {
            return (int)McpSettingsManager.Shared.Get(key);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Path normalization (matches the Bridge side): uses the full path when resolvable, otherwise normalizes directory separators.</summary>
    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}
