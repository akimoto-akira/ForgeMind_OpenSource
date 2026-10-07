// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Authorization;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMindServer.Plugins;

/// <summary>
/// Host-side implementation of <see cref="IPluginContext"/>: writes tools registered by a plugin into
/// the host tool table (with duplicate-name checks) and reports the Core identity to the plugin,
/// as well as honoring service-replacement requests. Registration happens only during the host's
/// startup phase (before the MCP service starts serving), but it is still guarded by a lock to
/// prevent concurrent or late <see cref="IPlugin.OnLoad"/> registrations from tearing the list.
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly List<ITool> _tools;
    private readonly EngineRegistry _engineRegistry;
    private readonly IEditorBridge? _editorBridge;

    public PluginContext(List<ITool> tools, string pluginDirectory, ModuleIdentity coreIdentity, EngineRegistry engineRegistry, IEditorBridge? editorBridge)
    {
        _tools = tools;
        PluginDirectory = pluginDirectory;
        CoreIdentity = coreIdentity;
        _engineRegistry = engineRegistry;
        _editorBridge = editorBridge;
        HostDirectory = AppContext.BaseDirectory;
        Logger = LogManager.Instance.GetLogger("Plugin");
    }

    public ILogger Logger { get; }

    public string HostDirectory { get; }

    public string PluginDirectory { get; }

    /// <summary>Core's self-identity reported to the plugin (part of the mutual-recognition handshake).</summary>
    public ModuleIdentity CoreIdentity { get; }

    /// <summary>Read-only engine entry: read-only snapshot of the host-side registry (plugins cannot mutate host engine state back).</summary>
    public IEngineRegistry Engines => _engineRegistry.GetSnapshot();

    /// <summary>Editor bridge: read-only session view of the host BridgeServer, plus command forwarding and DSL event-name resolution.</summary>
    public IEditorBridge? EditorBridge => _editorBridge;

    public void RegisterTool(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        lock (_tools)
        {
            ITool? existing = _tools.FirstOrDefault(t =>
                string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                Logger.Warn(null, "[Plugins] Registration of tool '{0}' ignored: a tool with the same name already exists ({1})",
                    tool.Name, existing.GetType().FullName);
                return;
            }

            _tools.Add(tool);
            Logger.Info(null, "[Plugins] Tool registered: {0} ({1})", tool.Name, tool.GetType().FullName);
        }
    }

    public void RegisterTools(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        foreach (ITool tool in tools)
            RegisterTool(tool);
    }

    /// <summary>
    /// Replaces a host default service (e.g. swapping the built-in open-source authorizer for a commercial one).
    /// Delegates to the host's service-replacement entry point; abstract types the host does not recognize
    /// are logged as a warning and ignored.
    /// </summary>
    public void ReplaceService<TService>(TService implementation) where TService : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _ = ForgeMindServerHost.ApplyServiceOverride(typeof(TService), implementation);
    }
}