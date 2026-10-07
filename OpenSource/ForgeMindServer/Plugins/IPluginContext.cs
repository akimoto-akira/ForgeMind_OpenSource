// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Authorization;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMindServer.Plugins;

/// <summary>
/// Plugin injection context: handed to the plugin when the host calls <see cref="IPlugin.OnLoad"/>;
/// the plugin uses it to perform its "injection actions" — registering tools into the MCP tool table,
/// replacing host default services, and so on.
/// </summary>
public interface IPluginContext
{
    /// <summary>Host logger (writes to stderr and any registered sinks; stdout is reserved for the MCP JSON-RPC channel).</summary>
    ILogger Logger { get; }

    /// <summary>
    /// Read-only engine entry: by the time <see cref="IPlugin.OnLoad"/> is called the host has already
    /// discovered and registered local engines and the current project; the plugin consumes this read-only
    /// (primary engine descriptor, current-project info) and cannot mutate host engine state back.
    /// </summary>
    IEngineRegistry Engines { get; }

    /// <summary>
    /// Editor bridge: read-only session view of the host BridgeServer, plus command forwarding and DSL
    /// event-name resolution. Consumed by plugins that issue commands while the editor is online (e.g.
    /// the editor tool of OfflineEngineKit); null when the host supplies no bridge (or the BridgeServer
    /// is disabled).
    /// </summary>
    IEditorBridge? EditorBridge { get; }

    /// <summary>The host executable's directory (AppContext.BaseDirectory).</summary>
    string HostDirectory { get; }

    /// <summary>The current plugin DLL's directory (a plugin's own dependencies are also resolved from here).</summary>
    string PluginDirectory { get; }

    /// <summary>
    /// Core's self-identity as reported to the plugin: CoreId / CoreVersion / CoreCapabilities /
    /// CoreProtocolVersion. The plugin uses this to verify "is this Core a ForgeMind Core I support?"
    /// (part of the mutual-recognition handshake).
    /// </summary>
    ModuleIdentity CoreIdentity { get; }

    /// <summary>
    /// Registers a tool into the MCP tool table. When the name collides with an existing tool
    /// (built-in or previously registered), the later registration is ignored and a warning is logged.
    /// </summary>
    void RegisterTool(ITool tool);

    /// <summary>Batch-registers tools (each goes through the duplicate-name check in <see cref="RegisterTool"/>).</summary>
    void RegisterTools(IEnumerable<ITool> tools);

    /// <summary>
    /// Replaces a host-registered default service (e.g. swapping the built-in open-source authorizer for
    /// a commercial one). Beyond the automated replacement driven by
    /// <see cref="ModuleManifest.ServiceOverrides"/>, plugins can also call this programmatically inside
    /// their OnLoad.
    /// </summary>
    void ReplaceService<TService>(TService implementation) where TService : class;
}
