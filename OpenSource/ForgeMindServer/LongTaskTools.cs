// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer.Compat;

namespace ForgeMindServer;

/// <summary>
/// Long-task status polling tool (requirements.md §2.6). Given a <c>task_id</c>, queries the result of a long task
/// and returns one of four states: executing / completed / failed / expired.
/// </summary>
public sealed class GetTaskResultTool : ITool
{
    public string Name => "get_task_result";

    public string Description =>
        "Poll the status of a long-running task created by the timeout fallback. " +
        "Pass task_id; returns one of: executing (keep polling), completed (result in data.result), " +
        "failed (error in message), expired (timed out, TTL elapsed).";

    public string GetDisplayName() => "Get Task Result";

    public Dictionary<string, object> GetParameterSchema()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["task_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The task GUID returned by an 'executing' response"
                }
            },
            ["required"] = new[] { "task_id" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("task_id", out object? raw) || raw is null)
            return ToolResult.Failed("Missing 'task_id' parameter");

        string? taskIdStr = raw switch
        {
            JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
            string s => s,
            _ => raw.ToString()
        };

        if (string.IsNullOrWhiteSpace(taskIdStr) || !Guid.TryParse(taskIdStr, out Guid taskId))
            return ToolResult.Failed($"Invalid task_id: {taskIdStr}");

        if (!LongTaskManager.Instance.TryGet(taskId, out LongTaskSlot? slot))
        {
            // Slot does not exist: either never created, or already reclaimed by the Watchdog after TTL
            return ToolResult.Successful(
                "Task not found or expired (TTL elapsed).",
                new Dictionary<string, object?> { ["status"] = "expired", ["task_id"] = taskId.ToString() });
        }

        switch (slot.Status)
        {
            case LongTaskStatus.Executing:
                return ToolResult.Successful(
                    "Task still running; keep polling.",
                    new Dictionary<string, object?> { ["status"] = "executing", ["task_id"] = taskId.ToString() });

            case LongTaskStatus.Completed:
            case LongTaskStatus.Failed:
                var data = new Dictionary<string, object?>
                {
                    ["status"] = slot.Status.ToString().ToLowerInvariant(),
                    ["task_id"] = taskId.ToString(),
                    ["success"] = slot.Success,
                    ["message"] = slot.Message,
                    ["result"] = slot.Data
                };
                // Result has been read → destroy the slot (requirements §2.5 step 6)
                LongTaskManager.Instance.PruneOne(taskId);
                return ToolResult.Successful(
                    slot.Status == LongTaskStatus.Completed ? "Task completed." : "Task failed.",
                    data);

            default: // Expired
                LongTaskManager.Instance.PruneOne(taskId);
                return ToolResult.Successful(
                    "Task expired (TTL elapsed).",
                    new Dictionary<string, object?> { ["status"] = "expired", ["task_id"] = taskId.ToString() });
        }
    }
}

/// <summary>
/// Lists all currently executing long tasks (requirements.md §2.6, for diagnostics).
/// </summary>
public sealed class ListExecutingTasksTool : ITool
{
    public string Name => "list_executing_tasks";

    public string Description =>
        "List all currently executing long tasks (debugging). Returns the task_id, tool name, " +
        "creation time and engine pid of each.";

    public string GetDisplayName() => "List Executing Tasks";

    public Dictionary<string, object> GetParameterSchema()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>(),
            ["required"] = new string[0]
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        IReadOnlyList<LongTaskSlot> list = LongTaskManager.Instance.ListExecuting();
        var tasks = new List<Dictionary<string, object?>>(list.Count);
        foreach (LongTaskSlot s in list)
        {
            tasks.Add(new Dictionary<string, object?>
            {
                ["task_id"] = s.TaskId.ToString(),
                ["tool"] = s.ToolName,
                ["created_at"] = s.CreatedAtUtc.ToString("o"),
                ["engine_pid"] = s.EnginePid
            });
        }

        return ToolResult.Successful(
            $"{tasks.Count} executing task(s).",
            new Dictionary<string, object?> { ["tasks"] = tasks, ["count"] = tasks.Count });
    }
}

/// <summary>
/// Manually cleans up long-task slots (requirements.md §2.6). Pass <c>task_id</c> to remove a single slot;
/// omit it to prune all completed / failed / expired slots and return the pruning count.
/// </summary>
public sealed class CleanupTasksTool : ITool
{
    public string Name => "cleanup_tasks";

    public string Description =>
        "Manually clean up long-task slots. Pass task_id to remove a single slot; " +
        "omit it to prune all completed / failed / expired slots. Returns the number cleaned.";

    public string GetDisplayName() => "Cleanup Tasks";

    public Dictionary<string, object> GetParameterSchema()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["task_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional task GUID to remove a single slot; omit to prune all expired/completed"
                }
            },
            ["required"] = new string[0]
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("task_id", out object? raw) && raw is not null)
        {
            string? taskIdStr = raw switch
            {
                JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
                string s => s,
                _ => raw.ToString()
            };

            if (string.IsNullOrWhiteSpace(taskIdStr) || !Guid.TryParse(taskIdStr, out Guid taskId))
                return ToolResult.Failed($"Invalid task_id: {taskIdStr}");

            int n = LongTaskManager.Instance.PruneOne(taskId);
            return ToolResult.Successful($"Cleaned {n} task(s).", new Dictionary<string, object?> { ["cleaned"] = n });
        }

        int removed = LongTaskManager.Instance.PruneAll();
        return ToolResult.Successful(
            $"Cleaned {removed} expired/completed task(s).",
            new Dictionary<string, object?> { ["cleaned"] = removed });
    }
}
