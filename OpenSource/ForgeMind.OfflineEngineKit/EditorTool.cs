// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji


using System.Text.Json;
using ForgeMindServer.Bridge;
using ForgeMindServer.Plugins;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.OfflineEngineKit;

/// <summary>
/// Unreal Editor interaction tool ("Editor Interaction").
/// Routes commands to a running editor through the ForgeMindServer companion
/// over the TCP bridge. The action set is dynamic — it is whatever the
/// connected companion reports in its handshake ('list' shows it).
/// </summary>
public class EditorTool : ITool
{
    /// <summary>Per-command response limit (editor operations may be slow).</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Extended timeout for batch operations that create/modify many elements at once.</summary>
    private static readonly TimeSpan BatchCommandTimeout = TimeSpan.FromSeconds(120);

    private readonly ServerPermissionChecker _permChecker;
    private readonly IEditorBridge _bridge;

    internal EditorTool(ServerPermissionChecker permChecker, IEditorBridge bridge)
    {
        _permChecker = permChecker;
        _bridge = bridge;
    }

    /// <summary>Engine-agnostic tool name.</summary>
    public string Name => "editor";

    public string Description =>
        "Bridge to a running Unreal Editor via the ForgeMindServer companion plugin. " +
        "Built-in actions that do not require a connected editor: 'status' (is any editor " +
        "online?) and 'list' (connected editors and their advertised actions). " +
        "All other actions are forwarded verbatim to the companion with the optional 'payload' - " +
        "use 'list' to discover them. " +
        "Safety: 'quit_editor' shuts the editor down from inside (unsaved assets prompt the " +
        "user first, blocking the editor thread until decided); never kill the editor process " +
        "yourself. " +
        "Requires 'path' (the .uproject file or its folder) unless exactly one editor is connected.";

    public string GetDisplayName() => "Editor Interaction";

    public Dictionary<string, object> GetParameterSchema()
    {
        // Dynamic action list: the built-ins ('status'/'list') are always
        // available; forwarded actions come from whatever the connected
        // companions advertised in their handshake. Rebuilt on every call,
        // so the AI-visible schema tracks editors connecting/disconnecting.
        List<string> actions = ["status", "list"];
        try
        {
            if (_bridge.IsRunning)
            {
                foreach (BridgeSession session in _bridge.GetSessions())
                {
                    foreach (string command in session.Commands)
                    {
                        if (!actions.Contains(command))
                        {
                            actions.Add(command);
                        }
                    }
                }
            }
        }
        catch
        {
            // Server state is volatile (mid startup/shutdown); fall back to the built-ins
        }

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["action"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Command name. 'status' and 'list' are built-in and always " +
                                       "available; every other entry is a companion-advertised " +
                                       "action forwarded verbatim with 'payload'. The enum is rebuilt " +
                                       "per request - no editor online means only 'status' and 'list'.",
                    ["enum"] = actions.Cast<object>().ToArray()
                },
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Path to the .uproject file (or its folder) identifying which editor to talk to"
                },
                ["payload"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Action-specific arguments forwarded to the companion"
                }
            },
            ["required"] = new[] { "action" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        // Route by engine version: one branch per supported version. Currently all versions
        // fall through to RunEditor; if a version diverges later, modify its case directly
        // (requires no new engine-specific files or additional registration).
        EngineVersion? engineVersion = ResolveEngineVersion(parameters);
        return engineVersion switch
        {
            EngineVersion.UE_5_0 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_1 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_2 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_3 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_4 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_5 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_6 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_7 => RunEditor(callerId, parameters),
            EngineVersion.UE_5_8 => RunEditor(callerId, parameters),
            _ => RunEditor(callerId, parameters)
        };
    }

    /// <summary>
    /// Parses the engine version from 'path' for <see cref="Execute"/> routing.
    /// Returns null on a default path (single editor connected), invalid path, or unlisted version,
    /// falling through to the default branch.
    /// </summary>
    private static EngineVersion? ResolveEngineVersion(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("path", out object? pathObj) ||
            string.IsNullOrWhiteSpace(pathObj?.ToString()))
        {
            return null;
        }

        FileInfo? projectFile = ProjectTool.ResolveProjectFile(pathObj.ToString()!);
        return projectFile == null
            ? null
            : EngineVersions.FromAssociation(ProjectTool.GetEngineAssociation(projectFile.Directory!));
    }

    /// <summary>
    /// Editor bridge implementation shared across all versions.
    /// If a version diverges later, modify the corresponding case in <see cref="Execute"/> to branch differently.
    /// </summary>
    private ToolResult RunEditor(Guid callerId, Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("action", out object? actionObj) ||
            string.IsNullOrWhiteSpace(actionObj?.ToString()))
        {
            return ToolResult.Failed("Missing 'action' parameter");
        }

        string action = actionObj.ToString()!.Trim().ToLowerInvariant();

        // EditorTool checks permission by action
        string? permError = _permChecker.RequirePermission(action);
        if (permError != null)
            return ToolResult.Failed(permError);

        if (action == "status")
            return ExecuteStatus();

        if (action == "list")
            return ExecuteList();

        // Resolve the target session
        BridgeSession? session = ResolveSession(parameters, out string? resolveError);
        if (session == null)
            return ToolResult.Failed(resolveError!);

        // Forward the action with its payload
        JsonElement? payload = null;
        if (parameters.TryGetValue("payload", out object? payloadObj))
        {
            payload = payloadObj is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(payloadObj);
        }

        // ── Name normalization: after MCP receives the command, before sending to the bridge ──
        // Callers often write the display name of an engine event (e.g., "BeginPlay"), while the
        // bridge/UE needs the real UFunction name ("ReceiveBeginPlay"). Searches and corrects within
        // the scope determined by the project path; if not found or the action carries no node name,
        // it passes through untouched (no guessing, no interruption).
        // Event nodes only: the alias table (ScanIndexStore.AddEventAlias) is **engine-wide,
        // first-come-first-served**; any class's overridable events occupy slots (including
        // "Receive"-stripped forms), so non-event node family names get hijacked by unrelated
        // events — e.g., macro name "Add" changed to "ReceiveAdd" makes the bridge unable to
        // resolve the macro graph, failing macro instance creation immediately (TestRuns/20261004_1932).
        // Non-event families with a kind (macro / getter / setter / callDelegate / breakStruct…)
        // always pass through; calls without kind retain old behavior (resolve UFunction by name, mostly events).
        JsonElement? Normalize(JsonElement? p, string act, string? projectPath)
        {
            if (p is null || act != "add_blueprint_node")
                return p;

            JsonElement element = p.Value;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("name", out JsonElement nameElement)
                || nameElement.ValueKind != JsonValueKind.String)
                return p;

            if (element.TryGetProperty("kind", out JsonElement kindElement)
                && kindElement.ValueKind == JsonValueKind.String
                && !string.Equals(kindElement.GetString(), "event", StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }

            string? original = nameElement.GetString();
            string? canonical = _bridge.ResolveEventFunctionName(original, projectPath);

            if (string.IsNullOrWhiteSpace(canonical)
                || string.Equals(canonical, original, StringComparison.Ordinal))
                return p;

            // JsonElement is readonly: rebuild the object and overwrite the name
            var obj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element)
                      ?? new Dictionary<string, JsonElement>();
            obj["name"] = JsonSerializer.SerializeToElement(canonical);
            return JsonSerializer.SerializeToElement(obj);
        }

        payload = Normalize(payload, action, session?.ProjectFile);

        // Batch operations (e.g. apply_asset_ir, execute_blueprint_dsl) create entire blueprints
        // in one shot and need a longer timeout than single-node commands.
        TimeSpan timeout = action == "apply_asset_ir" ? BatchCommandTimeout : CommandTimeout;

        try
        {
            BridgeMessage response = session.CallAsync(action, payload, timeout).GetAwaiter().GetResult();

            if (!string.IsNullOrEmpty(response.Error))
                return ToolResult.Failed($"Companion reported an error for '{action}': {response.Error}");

            return ToolResult.Successful(
                $"'{action}' completed on '{Path.GetFileName(session.ProjectFile)}'",
                new
                {
                    projectFile = session.ProjectFile,
                    action,
                    response = response.Payload
                });
        }
        catch (TimeoutException)
        {
            return ToolResult.Failed($"Editor did not respond to '{action}' within {timeout.TotalSeconds:0}s");
        }
        catch (Exception ex)
        {
            return ToolResult.Failed($"Bridge call '{action}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports editor online status. Always succeeds — the 'online' flag carries the answer,
    /// so callers can probe connectivity without distinguishing failure from offline.
    /// </summary>
    private ToolResult ExecuteStatus()
    {
        if (!_bridge.IsRunning)
        {
            return ToolResult.Successful("No editor is online - bridge server is not running", new
            {
                online = false,
                reason = "bridge server not running",
                sessions = Array.Empty<object>()
            });
        }

        BridgeSession[] sessions = _bridge.GetSessions();
        bool online = sessions.Length > 0;

        return ToolResult.Successful(
            online ? $"{sessions.Length} editor(s) online" : "No editor is online",
            new
            {
                online,
                bridgePort = _bridge.Port,
                sessions = sessions.Select(s => new
                {
                    projectFile = s.ProjectFile,
                    engineVersion = s.EngineVersion,
                    editorPid = s.EditorPid
                }).ToArray()
            });
    }

    /// <summary>
    /// Lists connected editors with their dynamic action sets.
    /// Fails when no editor is connected — callers probing for connectivity should use 'status'.
    /// </summary>
    private ToolResult ExecuteList()
    {
        if (!_bridge.IsRunning)
            return ToolResult.Failed("Bridge server is not running");

        BridgeSession[] sessions = _bridge.GetSessions();
        if (sessions.Length == 0)
        {
            return ToolResult.Failed(
                "No editor is connected - use action 'status' to probe connectivity, " +
                "or launch the project first (launch) and check ForgeMindServer");
        }

        return ToolResult.Successful($"{sessions.Length} editor(s) connected", new
        {
            bridgePort = _bridge.Port,
            sessions = sessions.Select(s => new
            {
                projectFile = s.ProjectFile,
                engineVersion = s.EngineVersion,
                editorPid = s.EditorPid,
                actions = s.Commands
            }).ToArray()
        });
    }

    /// <summary>
    /// Picks the session to talk to: explicit 'path' wins; with exactly one
    /// connected session the path may be omitted.
    /// </summary>
    private BridgeSession? ResolveSession(Dictionary<string, object> parameters, out string? error)
    {
        error = null;

        if (parameters.TryGetValue("path", out object? pathObj) &&
            !string.IsNullOrWhiteSpace(pathObj?.ToString()))
        {
            FileInfo? projectFile = ProjectTool.ResolveProjectFile(pathObj.ToString()!);
            if (projectFile == null)
            {
                error = $"No valid .uproject found at '{pathObj}'";
                return null;
            }

            BridgeSession? session = _bridge.GetSessionByProject(projectFile.FullName);
            if (session == null)
            {
                error = $"No companion is connected for '{projectFile.Name}' - " +
                        "verify the editor is running and ForgeMindServer is enabled (project 'analyze' → companionPlugin)";
            }

            return session;
        }

        BridgeSession[] sessions = _bridge.GetSessions();
        if (sessions.Length == 1)
            return sessions[0];

        error = sessions.Length == 0
            ? "No editor is connected — launch the project first (launch) or check ForgeMindServer"
            : "Multiple editors are connected — provide 'path' to pick one";
        return null;
    }
}
