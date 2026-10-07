// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer.Bridge;

namespace ForgeMindServer.Plugins;

/// <summary>
/// Read-only view plus command-forwarding surface over the live Unreal Editor
/// sessions brokered by the host's BridgeServer, together with the host's DSL
/// event-name resolver. Injected into plugins (OfflineEngineKit's editor tool)
/// so they can list sessions and forward editor commands without reaching into
/// host internals. The command call path is delegated to the host's BridgeServer;
/// the plugin only ever sees the abstract session / command surface.
/// </summary>
public interface IEditorBridge
{
    /// <summary>Whether the editor bridge server is currently listening.</summary>
    bool IsRunning { get; }

    /// <summary>The TCP port the bridge server listens on (null when not running).</summary>
    int? Port { get; }

    /// <summary>All currently connected editor sessions (read-only snapshot).</summary>
    BridgeSession[] GetSessions();

    /// <summary>Resolves the editor session bound to a given .uproject path.</summary>
    BridgeSession? GetSessionByProject(string projectFile);

    /// <summary>Forwards a command to a connected editor session and awaits the response.</summary>
    Task<BridgeMessage> SendCommandAsync(BridgeSession session, string action, JsonElement? payload, TimeSpan timeout);

    /// <summary>
    /// Host-provided DSL event-name normalizer: resolves a display name to the
    /// engine's real event function name for a given project (uses the offline
    /// scan index when available, falls back to a loose match otherwise).
    /// </summary>
    string? ResolveEventFunctionName(string? original, string? projectPath);

    /// <summary>
    /// Enqueues an offline scan of the given project (idempotent; processed by a
    /// dedicated background thread on the host). The plugin triggers scans through
    /// this surface rather than reaching into ScanIndexStore directly.
    /// </summary>
    void EnqueueScan(string projectFile);
}
