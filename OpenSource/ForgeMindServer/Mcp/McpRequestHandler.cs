// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.IO;
using System.Text.Json;
using ForgeMindServer.Bridge;
using ForgeMindServer.Compat;
using ForgeMindServer.Settings;

namespace ForgeMindServer.Mcp;

/// <summary>
/// Transport-agnostic JSON-RPC dispatch for the Model Context Protocol.
/// Implements the subset an MCP host needs to use tools: <c>initialize</c>,
/// <c>notifications/*</c>, <c>ping</c>, <c>tools/list</c> and <c>tools/call</c>.
/// Both the stdio and the HTTP transports feed frames through this class.
/// </summary>
internal sealed class McpRequestHandler
{
    /// <summary>MCP protocol revision implemented by this server.</summary>
    internal const string ProtocolVersion = "2025-06-18";

    /// <summary>Options for tool payload serialization (null fields omitted inside tool output).</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IReadOnlyList<ITool> _tools;
    private readonly ILogger _logger = LogManager.Instance.GetLogger<McpRequestHandler>();

    public McpRequestHandler(IReadOnlyList<ITool> tools) => _tools = tools;

    /// <summary>
    /// Parses one raw JSON-RPC frame and dispatches it.
    /// Returns the response frame, or null for notifications (no reply owed).
    /// </summary>
    public object? HandleText(string text)
    {
        JsonElement request;
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            request = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.Warn(null, "[MCP] Unparseable frame ignored: {0}", ex.Message);
            return BuildError(null, -32700, "Parse error");
        }

        return Handle(request);
    }

    /// <summary>Dispatches one parsed JSON-RPC frame; null means "notification, no reply".</summary>
    public object? Handle(JsonElement request)
    {
        string method = request.TryGetProperty("method", out JsonElement methodEl)
            ? methodEl.GetString() ?? ""
            : "";
        bool isNotification = !request.TryGetProperty("id", out JsonElement idEl) ||
                              idEl.ValueKind == JsonValueKind.Null;

        try
        {
            switch (method)
            {
                case "initialize":
                    return BuildResult(idEl, HandleInitialize(request));

                case "ping":
                    return BuildResult(idEl, new { });

                case "tools/list":
                    return BuildResult(idEl, HandleToolsList());

                case "tools/call":
                    return BuildResult(idEl, HandleToolsCall(request));

                default:
                    // Notifications (notifications/initialized, notifications/cancelled, ...)
                    // need no reply; unknown requests get a JSON-RPC error
                    if (!isNotification)
                        return BuildError(idEl, -32601, $"Method not found: {method}");
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(null, "[MCP] Handling '{0}' failed: {1}", method, ex.Message);
            if (!isNotification)
                return BuildError(idEl, -32603, $"Internal error: {ex.Message}");
            return null;
        }
    }

    /// <summary>True when the frame is an <c>initialize</c> request (session start).</summary>
    public static bool IsInitialize(JsonElement request) =>
        request.TryGetProperty("method", out JsonElement methodEl) &&
        methodEl.GetString() == "initialize";

    /// <summary>True when the frame carries no id (a notification — no response owed).</summary>
    public static bool IsNotification(JsonElement request) =>
        !request.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind == JsonValueKind.Null;

    // ===== initialize =====

    private static object HandleInitialize(JsonElement request)
    {
        // Echo the client's protocol version when supported-compatible; the
        // client validates and continues with notifications/initialized.
        string clientVersion = request.TryGetProperty("params", out JsonElement p) &&
                               p.ValueKind == JsonValueKind.Object &&
                               p.TryGetProperty("protocolVersion", out JsonElement pv)
            ? pv.GetString() ?? ProtocolVersion
            : ProtocolVersion;

        return new
        {
            protocolVersion = string.IsNullOrEmpty(clientVersion) ? ProtocolVersion : clientVersion,
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "ForgeMindServer",
                version = typeof(McpRequestHandler).Assembly.GetName().Version?.ToString() ?? "1.0.0"
            }
        };
    }

    // ===== tools/list =====

    private object HandleToolsList()
    {
        var list = new List<object>(_tools.Count);
        foreach (ITool tool in _tools)
        {
            list.Add(new
            {
                name = tool.Name,
                description = tool.Description,
                inputSchema = JsonSerializer.SerializeToElement(tool.GetParameterSchema())
            });
        }

        return new { tools = list };
    }

    // ===== tools/call =====

    private object HandleToolsCall(JsonElement request)
    {
        JsonElement p = request.TryGetProperty("params", out JsonElement paramsEl) ? paramsEl : default;

        string toolName = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out JsonElement nameEl)
            ? nameEl.GetString() ?? ""
            : "";

        ITool? tool = _tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
            return ErrorContent($"Unknown tool: {toolName}");

        // Arguments arrive as a JSON object; keep each value as a JsonElement
        // so tools can round-trip strings, booleans and nested payloads
        var parameters = new Dictionary<string, object>();
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("arguments", out JsonElement argsEl) &&
            argsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in argsEl.EnumerateObject())
                parameters[property.Name] = property.Value;
        }

        return InvokeTool(tool, toolName, parameters);
    }

    /// <summary>
    /// Invokes a tool, wrapped by the timeout-fallback mechanism from requirements.md §2.5:
    /// returns within <c>command_timeout_ms</c> → a normal synchronous response;
    /// does not return in time → treated as a long task; the AI is released immediately (returns
    /// executing + task_id) while the background continues to await the result.
    /// </summary>
    private object InvokeTool(ITool tool, string toolName, Dictionary<string, object> parameters)
    {
        int timeoutMs = GetTimeoutMs(tool, parameters);

        Task<ToolResult> execTask;
        try
        {
            execTask = Task.Run(() => tool.Execute(Guid.Empty, parameters));
            if (execTask.Wait(timeoutMs))
                return BuildToolResponse(execTask.Result);
        }
        catch (AggregateException ex)
        {
            _logger.Error(null, "[MCP] Tool '{0}' threw: {1}", toolName,
                ex.InnerException?.Message ?? ex.Message);
            return ErrorContent($"Tool execution crashed: {ex.InnerException?.Message ?? ex.Message}");
        }

        // Timeout: treat as a long task, release the AI immediately, and keep waiting for the engine in the background.
        // The AI passes only the project path (path argument), never a PID; the server looks up the engine PID via Bridge.
        string projectPath = ResolveProjectPath(parameters);
        (bool ok, string? error) = LongTaskManager.Instance.TryCreate(toolName, projectPath, out LongTaskSlot? slot);
        if (!ok || slot == null)
        {
            _logger.Warn(null, "[MCP] Long task rejected for '{0}': {1}", toolName, error ?? "(unknown)");
            return ErrorContent(error ?? "max concurrent long tasks reached");
        }

        if (!string.IsNullOrEmpty(projectPath))
        {
            BridgeSession? session = ForgeMindServerHost.BridgeServer.GetSessionByProject(projectPath);
            if (session is { IsReady: true })
                slot.EnginePid = session.EditorPid;
        }

        _ = execTask.ContinueWith(t =>
        {
            try
            {
                if (t.IsFaulted)
                    LongTaskManager.Instance.MarkFailed(slot,
                        t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Tool crashed");
                else
                    LongTaskManager.Instance.Complete(slot, t.Result);
            }
            catch (Exception ex)
            {
                LongTaskManager.Instance.MarkFailed(slot, ex.Message);
            }
        }, TaskScheduler.Default);

        return BuildLongTaskResponse(slot.TaskId);
    }

    /// <summary>Computes the synchronous wait timeout (ms) for this call. Batched DSL apply uses a longer timeout (requirements §4.3).</summary>
    private static int GetTimeoutMs(ITool tool, Dictionary<string, object> parameters)
    {
        int defaultMs = 30_000;
        try
        {
            defaultMs = (int)McpSettingsManager.Shared.Get(McpSettingsManager.CommandTimeoutMsKey);
        }
        catch
        {
            // Use the default value
        }

        // The dsl tool always leaves the process as one apply_asset_ir batch, and
        // DslTool's own BatchCommandTimeout is 120s. The old condition also looked
        // for an "action" parameter, which the dsl schema never declares — so it
        // never matched and large batches were cut off at the 30s default.
        if (tool.Name == "dsl")
        {
            return Math.Max(defaultMs, 120_000);
        }

        return defaultMs;
    }

    /// <summary>
    /// Extracts the project path (.uproject absolute path) from the tool's parameters. Engine-side
    /// tools uniformly receive the project path via the <c>path</c> argument; the AI has no reason
    /// (and no way) to supply an engine PID. The server later resolves the engine PID via
    /// <see cref="ForgeMindServerBridgeServer"/>. Returns an empty string when no project path
    /// argument is present (the long task degrades to pure TTL reclamation).
    /// </summary>
    private static string ResolveProjectPath(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("path", out object? raw) || raw is null)
            return "";

        string? text = raw switch
        {
            JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
            string s => s,
            _ => raw.ToString()
        };

        if (string.IsNullOrWhiteSpace(text))
            return "";

        FileInfo? file = Engine.UeProjectFileManager.ResolveProjectFile(text.Trim());
        return file?.FullName ?? "";
    }

    /// <summary>Wraps a tool result into an MCP tools/call response.</summary>
    private static object BuildToolResponse(ToolResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["success"] = result.Success,
            ["message"] = result.Message
        };
        if (result.Data != null)
            payload["data"] = result.Data;

        return new
        {
            content = new object[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(payload, JsonOptions)
                }
            },
            isError = !result.Success
        };
    }

    /// <summary>Long-task fallback response: immediately returns executing + task_id for the AI to poll get_task_result.</summary>
    private static object BuildLongTaskResponse(Guid taskId)
    {
        var payload = new Dictionary<string, object?>
        {
            ["success"] = true,
            ["message"] = $"Operation is still running; poll get_task_result('{taskId}') for the result.",
            ["data"] = new Dictionary<string, object?>
            {
                ["status"] = "executing",
                ["task_id"] = taskId.ToString()
            }
        };

        return new
        {
            content = new object[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(payload, JsonOptions)
                }
            },
            isError = false
        };
    }

    /// <summary>MCP error shape for a failed tool call (protocol-level success).</summary>
    private static object ErrorContent(string message)
    {
        return new
        {
            content = new object[]
            {
                new { type = "text", text = message }
            },
            isError = true
        };
    }

    // ===== JSON-RPC envelope helpers =====

    internal static object BuildResult(JsonElement id, object result) => new Dictionary<string, object?>
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    };

    internal static object BuildError(JsonElement? id, int code, string message) => new Dictionary<string, object?>
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new { code, message }
    };
}
