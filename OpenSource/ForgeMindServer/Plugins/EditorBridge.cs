// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMindServer.Abstractions;

using System.Text.Json;
using ForgeMindServer.Bridge;
using ForgeMindServer.Plugins;

namespace ForgeMindServer;

/// <summary>
/// Host-side implementation of <see cref="IEditorBridge"/>. Wraps the host's
/// <see cref="ForgeMindServerHost.BridgeServer"/> (editor session broker) and the
/// offline scan index's event-name resolver, exposing them to plugins behind a
/// read-only + command-forwarding surface. The plugin never touches
/// BridgeServer / ScanIndexStore directly.
/// </summary>
internal sealed class EditorBridge : IEditorBridge
{
    public bool IsRunning => ForgeMindServerHost.BridgeServer?.IsRunning ?? false;

    public int? Port => ForgeMindServerHost.BridgeServer?.Port;

    public BridgeSession[] GetSessions() =>
        ForgeMindServerHost.BridgeServer?.GetSessions() ?? [];

    public BridgeSession? GetSessionByProject(string projectFile) =>
        ForgeMindServerHost.BridgeServer?.GetSessionByProject(projectFile);

    public Task<BridgeMessage> SendCommandAsync(BridgeSession session, string action, JsonElement? payload, TimeSpan timeout) =>
        session.CallAsync(action, payload, timeout);

    public string? ResolveEventFunctionName(string? original, string? projectPath) =>
        ScanDataProvider.Current.ResolveEventFunctionName(original, projectPath);

    public void EnqueueScan(string projectFile) =>
        ScanDataProvider.Current.EnqueueScan(projectFile);
}
