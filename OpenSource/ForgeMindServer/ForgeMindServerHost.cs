// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMindServer.Abstractions;


using ForgeMindServer.Authorization;
using ForgeMindServer.Authorization.Open;
using ForgeMindServer.Bridge;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;
using ForgeMindServer.Plugins;
using ForgeMindServer.Settings;

namespace ForgeMindServer;

/// <summary>
/// Standalone host services for the ForgeMindServer MCP server. Takes over the
/// roles the SiliconLife host plugin used to provide: owns the TCP bridge
/// server for ForgeMindServer companions, loads <see cref="IPlugin"/> plugins
/// from the plugins directory, and owns the registry of exposed tools.
/// </summary>
public static class ForgeMindServerHost
{
    private static readonly ILogger _logger = LogManager.Instance.GetLogger<ForgeMindServerHostMarker>();

    /// <summary>Logger category marker (a static class cannot be a type argument).</summary>
    private sealed class ForgeMindServerHostMarker
    {
    }

    /// <summary>
    /// Authorization service — the single entry point through which the Core tool layer checks permissions.
    /// Defaults to the built-in open-source authorization (<see cref="OpenAuthorizationService"/>):
    /// capabilities are fixed-open (Community), and it does **not parse any license file**.
    /// A commercial authorization plugin overrides this property via service replacement (Manifest ServiceOverride) once loaded.
    /// </summary>
    public static IAuthorizationService AuthorizationService { get; internal set; } = new OpenAuthorizationService();

    /// <summary>The host's own module identity (the default identity when Core requests capabilities).</summary>
    public static ModuleIdentity CoreModuleIdentity { get; } = new()
    {
        ModuleId = "ForgeMind.Core",
        PublisherId = "ForgeMind",
        Version = typeof(ForgeMindServerHost).Assembly.GetName().Version?.ToString() ?? "0.0.0"
    };

    /// <summary>The host's own authorization context (machine code + Core version).</summary>
    public static AuthorizationContext CoreAuthorizationContext { get; } = new()
    {
        MachineCode = SafeMachineCode(),
        MachineName = Environment.MachineName,
        CoreVersion = typeof(ForgeMindServerHost).Assembly.GetName().Version?.ToString() ?? "0.0.0"
    };

    /// <summary>Safe machine-code acquisition (falls back to empty string on failure; does not block host startup).</summary>
    private static string SafeMachineCode()
    {
        try
        {
            return MachineIdentity.Generate();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Convenience entry for the host to check capability authorization: determines whether a capability
    /// is available at tool-call time. Returns null when authorized; non-null is an error description
    /// (usable directly as a ToolResult.Failed message).
    /// </summary>
    internal static string? RequirePermission(string capability) =>
        AuthorizationService.RequireCapability(capability, CoreModuleIdentity, CoreAuthorizationContext);

    /// <summary>
    /// Service-replacement entry: a plugin (or a Manifest-driven ServiceOverride) replaces a
    /// default service already registered on the host with a new implementation. Core is unaware of any
    /// "commercial" concept — replacement happens solely by abstract type.
    /// </summary>
    /// <returns>The previous service implementation (null if no previous implementation exists for this type).</returns>
    internal static object? ApplyServiceOverride(Type serviceType, object implementation)
    {
        if (serviceType == typeof(IAuthorizationService) && implementation is IAuthorizationService authService)
        {
            object? previous = AuthorizationService;
            AuthorizationService = authService;
            _logger.Info(null, "[ForgeMindServer] Authorization service replaced: {0}", authService.GetType().FullName);
            return previous;
        }

        // Future replaceable services (IPluginHost / IDslEngine, etc.) register here
        _logger.Warn(null, "[ForgeMindServer] Unknown service type {0}; replacement ignored", serviceType.FullName);
        return null;
    }

    /// <summary>
    /// Gets the current implementation of a specific service type. Used by PluginLoader to capture
    /// the previous service state before applying overrides, enabling rollback on OnLoad failure.
    /// </summary>
    internal static object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IAuthorizationService))
            return AuthorizationService;
        return null;
    }

    /// <summary>
    /// Restores a service to a previous implementation. Used by PluginLoader to roll back overrides
    /// when OnLoad fails or when a plugin is unloaded.
    /// </summary>
    internal static void RestoreService(Type serviceType, object? implementation)
    {
        if (serviceType == typeof(IAuthorizationService) && implementation is IAuthorizationService authService)
        {
            AuthorizationService = authService;
            _logger.Info(null, "[ForgeMindServer] Authorization service restored: {0}", authService.GetType().FullName);
            return;
        }
        _logger.Warn(null, "[ForgeMindServer] Unknown service type {0}; restore ignored", serviceType.FullName);
    }

    /// <summary>
    /// MCP settings manager. Loaded from settings.json on construction; changes persist immediately.
    /// </summary>
    internal static McpSettingsManager SettingsManager { get; } = McpSettingsManager.Shared;

    /// <summary>
    /// TCP bridge server for ForgeMindServer companions. Created eagerly so
    /// tools can reference it; actually listens only after <see cref="Start"/>.
    /// </summary>
    public static ForgeMindServerBridgeServer BridgeServer { get; } = new();

    /// <summary>
    /// Built-in tool list. Tools injected by plugins via <see cref="PluginContext.RegisterTool"/>
    /// are appended to this list (injection completes before the MCP service starts; visible on the client's first connection).
    /// </summary>
    private static readonly List<ITool> _toolList =
    [
        new SettingsTool(SettingsManager),
        new GetTaskResultTool(),
        new ListExecutingTasksTool(),
        new CleanupTasksTool(),
    ];

    /// <summary>Tool-table snapshot. First access triggers on-demand plugin loading: during host startup
    /// we only resolve the plugin directory and construct the loader; the real scan and OnLoad are
    /// deferred to here — startup cost is independent of plugin count, and callers always receive
    /// the complete tool table (a half-read table is harder to debug than a slow startup).
    /// </summary>
    internal static IReadOnlyList<ITool> Tools => _toolList;

    /// <summary>Plugin loader. Populated on <see cref="Start"/>, torn down on <see cref="Stop"/>.</summary>
    private static PluginLoader? _pluginLoader;

    /// <summary>Plugin directory: resolved at startup; used during deferred loading.</summary>
    private static string? _pluginDir;

    /// <summary>One-shot deferred-loading gate (0 = not started, 1 = started): guarantees a single load under concurrency.</summary>
    private static int _pluginsLoadStarted;

    /// <summary>
    /// Host-side engine registry: populated by local engine / project probing on <see cref="Start"/>;
    /// its read-only snapshot is injected into plugins via <see cref="PluginContext"/>
    /// (<see cref="IPluginContext.Engines"/>). Engine data is computed by the host and read-only to plugins.
    /// </summary>
    internal static EngineRegistry EngineRegistry { get; } = new();

    /// <summary>Starts host services (the UE bridge).</summary>
    public static void Start()
    {

        // Open-source authorization: no license-file parsing, no trial-license generation.
        // AuthorizationService keeps its field-initial value OpenAuthorizationService (Community fixed capabilities).
        // Commercial authorization is introduced only by a ServiceOverride during plugin loading (see PluginLoader / ApplyServiceOverride).
        _logger.Info(null, "[ForgeMindServer] Authorization: {0} ({1})",
            AuthorizationService.GetType().FullName,
            AuthorizationService is OpenAuthorizationService ? "open-source community" : "provider");

        try
        {
            BridgeServer.Start();
        }
        catch (Exception ex)
        {
            _logger.Error(null, "[ForgeMindServer] Bridge server failed to start: {0}", ex.Message);
        }

        // Host-side engine discovery: enumerate locally installed engines and register them into EngineRegistry;
        // its read-only snapshot is then injected into each plugin via PluginContext (IPluginContext.Engines).
        // Engine data is computed by the host and read-only to plugins — plugins cannot modify it in reverse.
        try
        {
            DiscoverEngines();
        }
        catch (Exception ex)
        {
            _logger.Warn(null, "[ForgeMindServer] Engine discovery failed (non-fatal): {0}", ex.Message);
        }

        // Plugin directory resolution (default: <host-dir>\plugins, overridable via FORGEMIND_PLUGINS_DIR).
        // Discovers IPlugin implementations and runs their OnLoad injection actions (tool registration, etc.).
        // Loading does <b>not</b> happen here: the caller decides when via EnsurePluginsLoaded().
        try
        {
            string pluginDir =
                Environment.GetEnvironmentVariable("FORGEMIND_PLUGINS_DIR") is { Length: > 0 } envDir
                    ? envDir
                    : Path.Combine(AppContext.BaseDirectory, "plugins");
            _pluginLoader = new PluginLoader(
                dir => new PluginContext(_toolList, dir, CoreModuleIdentity, EngineRegistry, new EditorBridge()),
                (type, impl) => ApplyServiceOverride(type, impl),
                GetService,
                RestoreService,
                () => AuthorizationService,
                CoreAuthorizationContext);
            _pluginDir = pluginDir;
        }
        catch (Exception ex)
        {
            _logger.Warn(null, "[ForgeMindServer] Plugin load failed (non-fatal): {0}", ex.Message);
        }
    }

    /// <summary>
    /// Host-side engine discovery: enumerate locally installed Unreal engines and register the primary
    /// engine into <see cref="EngineRegistry"/>. The registry is keyed by <see cref="EngineKind"/>
    /// (at most one primary engine per kind); launcher binary installs are preferred
    /// (AppName shaped like "UE_5.x"); fall back to the first source build (registry GUID association).
    /// Current-project information is left to be filled later by project discovery / settings — tools like
    /// KnowledgeTool parse the <c>engineVersion</c> parameter themselves and do not rely on this field.
    /// </summary>
    private static void DiscoverEngines()
    {
        UnrealEngineInstallInfo[] installs = Engine.UnrealEngineLocator.GetAllUnrealApplications();
        if (installs.Length == 0)
            return;

        UnrealEngineInstallInfo? primary = Array.Find(installs, i =>
            string.Equals(i.NamespaceId, "ue", StringComparison.OrdinalIgnoreCase) &&
            (i.AppName ?? "").StartsWith("UE_", StringComparison.OrdinalIgnoreCase));
        primary ??= Array.Find(installs, i =>
            string.Equals(i.NamespaceId, "regUe", StringComparison.OrdinalIgnoreCase));
        primary ??= installs[0];

        EngineRegistry.Register(new EngineDescriptor(
            EngineKind.Unreal,
            primary.Value.InstallLocation,
            TryParseEngineVersion(primary.Value.AppVersion),
            string.Equals(primary.Value.NamespaceId, "regUe", StringComparison.OrdinalIgnoreCase),
            true));
    }

    /// <summary>Parses the major version from a launcher / build version string (tolerates build suffixes such as "5.4.0-12345").</summary>
    private static Version TryParseEngineVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Version(0, 0);
        int cut = raw.IndexOfAny(['-', '+', ' ']);
        string token = cut >= 0 ? raw[..cut] : raw;
        return Version.TryParse(token, out Version? v) ? v : new Version(0, 0);
    }

    /// <summary>
    /// Explicitly loads all plugins from the plugin directory. The host does <b>not</b> auto-load on startup
    /// — the caller decides when (the host itself on demand, or the test process triggers it once ready).
    /// A pure host-internal control surface, not exposed as an external feature. Idempotent and thread-safe;
    /// failures are logged and do not block the host.
    /// <para>Directory layout (the single authority is the staging standard): each subdirectory of the
    /// plugins root is one plugin package (main DLL + sibling manifest + dependencies). Loose DLLs directly
    /// in the root are also honored (manual drop-in deployment). Both are scanned top-level per directory —
    /// the loader keeps its single-directory semantics; only this entry decides the layout.</para>
    /// </summary>
    public static void EnsurePluginsLoaded()
    {
        if (Interlocked.Exchange(ref _pluginsLoadStarted, 1) == 0 && _pluginLoader is not null && _pluginDir is not null)
        {
            // Loose DLLs directly in the plugins root (manual drop-in deployment)
            try
            {
                _pluginLoader.LoadFromDirectory(_pluginDir);
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[ForgeMindServer] Plugin root scan failed (non-fatal): {0}", ex.Message);
            }

            // Staging standard: one subdirectory per plugin package. A failure in a single
            // package only skips that package — other plugins are still loaded.
            foreach (string packageDir in Directory
                .GetDirectories(_pluginDir)
                .OrderBy(d => d, StringComparer.Ordinal))
            {
                try
                {
                    _pluginLoader.LoadFromDirectory(packageDir);
                }
                catch (Exception ex)
                {
                    _logger.Warn(null, "[ForgeMindServer] Plugin package scan failed (non-fatal): {0} — {1}",
                        packageDir, ex.Message);
                }
            }
        }

        // Bridge-reflection read provider (source 3): registered AFTER the engine is hooked up
        // (plugins have run their OnLoad). Join order follows data readiness, and the chain
        // asks last-joined first — so the query order becomes bridge reflection, then the live
        // scan index, then the frozen engine index (freshness descending). The plugin-registered
        // providers join earlier with their OnLoad; without this registration a chain over them
        // ends in all-null and the parent-member shapes reported through the bridge (filled by
        // ParentAssetPrefetcher) become unreachable. Registration is unconditional and idempotent:
        // an empty bridge index answers null and the chain falls through harmlessly.
        ScanDataProviderRegistry.Register(BridgeScanDataProvider.Instance);
    }
    /// <summary>Stops host services on shutdown.</summary>
    public static void Stop()
    {
        // Unload plugins before stopping the bridge: host services (bridge etc.) remain available during OnUnload
        if (_pluginLoader != null)
        {
            try
            {
                _pluginLoader.UnloadAll();
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[ForgeMindServer] Plugin unload failed: {0}", ex.Message);
            }
            _pluginLoader = null;
        }

        try
        {
            BridgeServer.Stop();
        }
        catch (Exception ex)
        {
            _logger.Warn(null, "[ForgeMindServer] Bridge server stop failed: {0}", ex.Message);
        }
    }
}
