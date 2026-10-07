// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Authorization;

namespace ForgeMindServer.Plugins;

/// <summary>
/// ForgeMindServer plugin contract. External DLLs that implement this interface and are placed
/// in the plugin directory are discovered, instantiated, and have <see cref="OnLoad"/> called
/// during host startup so they can perform their injection actions (e.g. registering tools).
/// Implementations must be public concrete types with a parameterless constructor.
/// </summary>
public interface IPlugin
{
    /// <summary>Unique plugin name (used for logging and duplicate-name detection).</summary>
    string Name { get; }

    /// <summary>Plugin version (display only).</summary>
    string Version { get; }

    /// <summary>Plugin capability description (display only).</summary>
    string Description { get; }

    /// <summary>
    /// The module identity declared by the plugin: ModuleId / PublisherId / Version / Core compatibility
    /// range / Capabilities. Core passes this identity to the authorization service during the handshake
    /// to determine the plugin's grant.
    /// </summary>
    ModuleIdentity Identity { get; }

    /// <summary>
    /// Plugin injection action: invoked once by the host after loading the plugin DLL and
    /// instantiating the implementation class. Performs its injection through
    /// <paramref name="context"/> (e.g. <c>context.RegisterTool(...)</c> adds tools to the MCP tool
    /// table; <c>context.ReplaceService(...)</c> swaps a host default service). Called before the MCP
    /// service starts, so injected tools are visible to the first batch of client connections.
    /// An exception is treated as this plugin having failed to load: the host logs it and skips it,
    /// neither other plugins nor the host itself are affected.
    /// </summary>
    void OnLoad(IPluginContext context);

    /// <summary>
    /// Called once during host shutdown (reverse load order, best effort: exceptions are only logged).
    /// Host services (bridge, scan index, etc.) remain available at this point.
    /// </summary>
    void OnUnload();
}
