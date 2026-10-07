// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using ForgeMindServer.Authorization;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Plugins;

/// <summary>
/// Plugin loader: scans the target directory for *.dll files (plus a sibling *.manifest.json),
/// loads them in an isolated <see cref="AssemblyLoadContext"/>, discovers every <see cref="IPlugin"/>
/// implementation (including those derived through an abstract base class), and loads them
/// through the mutual-recognition handshake protocol:
///
/// Discover Plugin → Read Manifest → Verify Integrity (SHA-256 + signature)
/// → Check Core version compatibility → Apply ServiceOverride (may replace a host default)
/// → Authorization Handshake (Core ↔ Plugin mutual recognition) → Entitlement Verification → Plugin Ready
///
/// Failure at any single plugin (load / instantiation / integrity / authorization / OnLoad throws)
/// only logs and skips that plugin — neither other plugins nor the host itself are affected.
///
/// The host's own assemblies, the runtime framework, and any assemblies already loaded in the
/// default context are not reloaded from the plugin directory — cross-boundary types such as
/// IPlugin / ITool / IAuthorizationService always resolve to the host-side instance to avoid type
/// identity splitting.
/// </summary>
public sealed class PluginLoader
{
    /// <summary>Runtime / shared-framework assembly prefixes skipped by name during scanning.</summary>
    private static readonly string[] RuntimeNamePrefixes = ["System.", "Microsoft."];

    /// <summary>Individual runtime assemblies skipped by name.</summary>
    private static readonly string[] RuntimeAssemblyNames = ["netstandard", "mscorlib", "WindowsBase"];

    /// <summary>The host assembly name — references to host types from a plugin must fall back to the default context for resolution.</summary>
    private const string HostAssemblyName = "ForgeMindServer";

    private readonly Func<string, IPluginContext> _contextFactory;
    private readonly Action<Type, object> _serviceReplacer;
    private readonly Func<Type, object?> _serviceGetter;
    private readonly Action<Type, object?> _serviceRestorer;
    private readonly Func<IAuthorizationService> _authServiceFactory;
    private readonly AuthorizationContext _coreContext;
    private readonly ILogger _logger = LogManager.Instance.GetLogger<PluginLoader>();
    private readonly List<IPlugin> _loaded = [];

    /// <summary>Per-plugin service state capture: records what services each plugin replaced and their previous values,
    /// so they can be rolled back on OnLoad failure or restored on Unload.</summary>
    private readonly Dictionary<IPlugin, List<(Type ServiceType, object? PreviousState)>> _pluginOverrides = new();

    /// <summary>Isolated load context for plugin assemblies (created on the first directory scan, then shared and extended with additional directories).</summary>
    private PluginLoadContext? _loadContext;

    /// <summary>Scanned directories (used by the load context for dependency resolution; accumulates across LoadFromDirectory calls).</summary>
    private readonly List<string> _searchDirectories = [];

    public PluginLoader(
        Func<string, IPluginContext> contextFactory,
        Action<Type, object> serviceReplacer,
        Func<Type, object?> serviceGetter,
        Action<Type, object?> serviceRestorer,
        Func<IAuthorizationService> authServiceFactory,
        AuthorizationContext coreContext)
    {
        _contextFactory = contextFactory;
        _serviceReplacer = serviceReplacer;
        _serviceGetter = serviceGetter;
        _serviceRestorer = serviceRestorer;
        _authServiceFactory = authServiceFactory;
        _coreContext = coreContext;
    }

    /// <summary>Plugins that have successfully completed OnLoad (in load order).</summary>
    public IReadOnlyList<IPlugin> LoadedPlugins => _loaded;

    /// <summary>
    /// Scans the directory (top-level only) and loads the plugins within. Logs and exits when the
    /// directory does not exist or contains no DLLs. May be called repeatedly on multiple directories;
    /// loaded plugins accumulate in <see cref="LoadedPlugins"/>.
    /// </summary>
    public IReadOnlyList<IPlugin> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            _logger.Info(null, "[Plugins] Plugin directory not found, skipping scan: {0}", directory);
            return _loaded;
        }

        List<string> dllFiles = Directory
            .GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            // Windows 8.3-matching quirk: "*.dll" also matches names like "x.dllx"; filter by extension explicitly
            .Where(f => string.Equals(Path.GetExtension(f), ".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (dllFiles.Count == 0)
        {
            _logger.Info(null, "[Plugins] Plugin directory contains no DLLs, skipping scan: {0}", directory);
            return _loaded;
        }

        if (_loadContext == null)
        {
            _searchDirectories.Add(directory);
            _loadContext = new PluginLoadContext(_searchDirectories);
        }
        else if (!_searchDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            _searchDirectories.Add(directory); // the context holds the same list reference; appending takes effect immediately
        }

        int loadedCount = 0;
        var seenPluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dllPath in dllFiles)
        {
            // Load the sibling Manifest and the raw DLL bytes (for integrity verification)
            // Manifest 文件存在但损坏 → InvalidDataException → 拒绝加载
            string? manifestPath = ManifestLoader.ResolveManifestPath(dllPath);
            ModuleManifest? manifest;
            try
            {
                manifest = ManifestLoader.LoadManifest(manifestPath);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.IO.IOException or JsonException)
            {
                _logger.Error(null, "[Plugins] Refusing to load {0}: manifest corrupt — {1}",
                    Path.GetFileName(dllPath), ex.Message);
                continue;
            }

            byte[] pluginBytes;
            try
            {
                pluginBytes = File.ReadAllBytes(dllPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[Plugins] Failed to read {0}, skipping: {1}", Path.GetFileName(dllPath), ex.Message);
                continue;
            }

            // Layer 2 — Integrity: content hash + signature
            PluginIntegrityStatus integrity = PluginIntegrityVerifier.Verify(manifest, pluginBytes);
            switch (integrity)
            {
                case PluginIntegrityStatus.IntegrityMismatch:
                    _logger.Error(null, "[Plugins] Refusing to load {0}: PayloadHash does not match the DLL content (official distribution altered?)",
                        Path.GetFileName(dllPath));
                    continue;
                case PluginIntegrityStatus.SignatureInvalid:
                    _logger.Error(null, "[Plugins] Refusing to load {0}: signature could not be verified by any trusted public key",
                        Path.GetFileName(dllPath));
                    continue;
                case PluginIntegrityStatus.Development:
                    _logger.Debug(null, "[Plugins] {0}: development / community mode (unsigned Manifest)", Path.GetFileName(dllPath));
                    break;
                case PluginIntegrityStatus.Trusted:
                    _logger.Info(null, "[Plugins] {0}: integrity verified (Trusted)", Path.GetFileName(dllPath));
                    break;
            }

            Assembly? assembly = TryLoadAssembly(dllPath);
            if (assembly == null)
                continue;

            foreach (Type type in DiscoverPluginTypes(assembly, dllPath))
            {
                if (LoadPlugin(type, directory, manifest, integrity, seenPluginNames))
                    loadedCount++;
            }
        }

        _logger.Info(null, "[Plugins] Scan complete: {0} — {1} DLL(s), {2} plugin(s) loaded",
            directory, dllFiles.Count, loadedCount);
        return _loaded;
    }

    /// <summary>
    /// Unloads every plugin by calling <see cref="IPlugin.OnUnload"/> in reverse-load order
    /// (best effort; exceptions are only logged). Restores any services the plugin had replaced
    /// during load back to their previous state. Note that tool instances injected by a plugin are
    /// held by the host for its whole lifetime and the assembly itself cannot be unloaded; this
    /// method only guarantees that the plugin receives its lifecycle callback.
    /// </summary>
    public void UnloadAll()
    {
        for (int i = _loaded.Count - 1; i >= 0; i--)
        {
            IPlugin plugin = _loaded[i];
            try
            {
                plugin.OnUnload();
                _logger.Info(null, "[Plugins] Unloaded: {0}", plugin.Name);
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[Plugins] {0}.OnUnload failed: {1}", plugin.Name, ex.Message);
            }

            // Restore services this plugin had replaced (refactor plan §1 Plugin lifecycle / rollback)
            if (_pluginOverrides.TryGetValue(plugin, out List<(Type ServiceType, object? PreviousState)>? overrides))
            {
                // Restore in reverse order of application (LIFO)
                for (int j = overrides.Count - 1; j >= 0; j--)
                {
                    var (serviceType, previousState) = overrides[j];
                    try
                    {
                        _serviceRestorer(serviceType, previousState);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(null, "[Plugins] {0} service restore failed for {1}: {2}",
                            plugin.Name, serviceType.FullName, ex.Message);
                    }
                }
                _pluginOverrides.Remove(plugin);
            }
        }

        _loaded.Clear();
    }

    // ===== Single DLL loading & discovery =====

    /// <summary>Loads a candidate DLL into the plugin context; returns null for host / runtime assemblies or load failures.</summary>
    private Assembly? TryLoadAssembly(string dllPath)
    {
        try
        {
            string? name = AssemblyName.GetAssemblyName(dllPath).Name;
            if (name == null || ShouldSkipAssembly(name))
            {
                _logger.Debug(null, "[Plugins] Skipping non-plugin assembly: {0}", Path.GetFileName(dllPath));
                return null;
            }

            return _loadContext!.LoadFromAssemblyPath(dllPath);
        }
        catch (Exception ex)
        {
            _logger.Error(null, "[Plugins] Failed to load {0}: {1}", Path.GetFileName(dllPath), ex.Message);
            return null;
        }
    }

    /// <summary>Enumerates instantiable concrete <see cref="IPlugin"/> types within an assembly.</summary>
    private IEnumerable<Type> DiscoverPluginTypes(Assembly assembly, string dllPath)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.Warn(null, "[Plugins] {0} partial type load failure: {1}",
                Path.GetFileName(dllPath),
                ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message);
            types = ex.Types;
        }
        catch (Exception ex)
        {
            _logger.Error(null, "[Plugins] {0} type enumeration failed: {1}", Path.GetFileName(dllPath), ex.Message);
            yield break;
        }

        foreach (Type? type in types)
        {
            if (type == null || type.IsAbstract || type.IsInterface || !typeof(IPlugin).IsAssignableFrom(type))
                continue;

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                _logger.Warn(null, "[Plugins] Skipping {0}: missing parameterless constructor", type.FullName ?? type.Name);
                continue;
            }

            yield return type;
        }
    }

    /// <summary>
    /// Runs the full mutual-recognition handshake to instantiate and load a plugin:
    /// identity merge (Manifest first) → version compatibility → ServiceOverride application
    /// → authorization handshake → OnLoad.
    /// </summary>
    private bool LoadPlugin(Type type, string pluginDirectory, ModuleManifest? manifest, PluginIntegrityStatus integrity, HashSet<string> seenPluginNames)
    {
        IPlugin plugin;
        try
        {
            plugin = (IPlugin)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            _logger.Error(null, "[Plugins] Failed to instantiate {0}: {1}", type.FullName ?? type.Name, ex.Message);
            return false;
        }

        if (!seenPluginNames.Add(plugin.Name))
            _logger.Warn(null, "[Plugins] Duplicate plugin name: {0} ({1})", plugin.Name, type.FullName ?? type.Name);

        // Identity merge: the Manifest's declared identity takes precedence (it is the authoritative
        // reference by which Core knows the plugin); plugins without a Manifest fall back to their
        // runtime-declared Identity (development / community mode).
        if (manifest != null &&
            !string.Equals(manifest.ModuleId, plugin.Identity.ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error(null, "[Plugins] Refusing to load {0}: identity mismatch — Manifest={1}, Runtime={2}",
                type.Assembly.GetName().Name, manifest.ModuleId, plugin.Identity.ModuleId);
            return false;
        }
        ModuleIdentity identity = manifest?.ToModuleIdentity() ?? plugin.Identity;

        // Version compatibility check: is this Core within the range the plugin supports for ForgeMind Core?
        string coreVersion = _coreContext.CoreVersion;
        if (identity.IsCompatibleWithCore(coreVersion))
        {
            _logger.Info(null, "[Plugins] {0} v{1} is compatible with Core {2}", identity.ModuleId, identity.Version, coreVersion);
        }
        else
        {
            _logger.Error(null, "[Plugins] Refusing to load {0}: requires Core in [{1}, {2}], current is {3}",
                identity.ModuleId,
                identity.MinCoreVersion.Length > 0 ? identity.MinCoreVersion : "*",
                identity.MaxCoreVersion.Length > 0 ? identity.MaxCoreVersion : "*",
                coreVersion);
            return false;
        }

        // Authorization handshake: Core authenticates the plugin via the current authorization
        // service and obtains its entitlement. Failure (e.g. the commercial auth service deciding
        // the plugin is not licensed) → refuse to load. Note: handshake *before* ServiceOverride —
        // the auth-provider plugin itself must first be authorized by the current service before it
        // is allowed to replace that service (avoids the "self-asking" paradox).
        IAuthorizationService authService = _authServiceFactory();
        AuthorizationResult authResult = authService.Authenticate(identity, _coreContext);
        if (!authResult.IsAuthorized)
        {
            _logger.Error(null, "[Plugins] Refusing to load {0}: authorization failed — {1}",
                identity.ModuleId,
                authResult.ErrorMessage ?? authResult.ErrorCode ?? "unknown");
            return false;
        }

        var entitlement = authResult.Entitlement;
        _logger.Info(null, "[Plugins] {0} authorized: {1} ({2} capability/capabilities)",
            identity.ModuleId, entitlement?.Edition ?? "?", entitlement?.Features.Count ?? 0);

        // Apply the service replacements declared by the Manifest (e.g. a commercial auth plugin replacing the built-in open-source authorizer).
        // Capture previous state so it can be rolled back if OnLoad fails (refactor plan §1 Plugin lifecycle / rollback).
        var capturedOverrides = new List<(Type ServiceType, object? PreviousState)>();
        if (manifest != null && !TryApplyServiceOverrides(manifest, type.Assembly, integrity, capturedOverrides))
        {
            // On ApplyServiceOverrides failure, roll back any already-applied overrides for this plugin
            RollbackOverrides(capturedOverrides);
            return false;
        }

        try
        {
            plugin.OnLoad(_contextFactory(pluginDirectory));
            _loaded.Add(plugin);

            // Retain the overrides so Unload can restore them later
            if (capturedOverrides.Count > 0)
                _pluginOverrides[plugin] = capturedOverrides;

            _logger.Info(null, "[Plugins] Loaded: {0} v{1} — {2} ({3})",
                plugin.Name, plugin.Version, plugin.Description, type.FullName);
            return true;
        }
        catch (Exception ex)
        {
            // OnLoad failed: roll back all service overrides so the host is not left in a partial state
            RollbackOverrides(capturedOverrides);
            _logger.Error(null, "[Plugins] {0} injection failed, skipping: {1}", plugin.Name, ex);
            return false;
        }
    }

    /// <summary>
    /// Rolls back a batch of service overrides by restoring each service to its previously captured state.
    /// Called when OnLoad fails or when ApplyServiceOverrides encounters an error.</summary>
    private void RollbackOverrides(List<(Type ServiceType, object? PreviousState)> overrides)
    {
        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            var (serviceType, previousState) = overrides[i];
            try
            {
                _serviceRestorer(serviceType, previousState);
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[Plugins] Service rollback failed for {0}: {1}", serviceType.FullName, ex.Message);
            }
        }
        overrides.Clear();
    }

    /// <summary>
    /// Modules authorized to replace security-sensitive core services. Ordinary plugins cannot
    /// arbitrarily swap <see cref="IAuthorizationService"/> or similar security-critical services;
    /// only modules explicitly granted this capability (distributed by the trusted publisher) may
    /// do so.
    /// </summary>
    private static readonly HashSet<string> SecurityOverrideAllowlist = new(StringComparer.Ordinal)
    {
        "ForgeMind.Authorization.Commercial"
    };

    /// <summary>
    /// Applies the ServiceOverride entries declared by the Manifest: instantiates a type from the
    /// plugin assembly to replace a default service already registered by the host (e.g. a commercial
    /// auth plugin replacing the built-in IAuthorizationService). Only Manifests that pass
    /// <see cref="PluginIntegrityVerifier"/> drive such a replacement. Security-sensitive services
    /// require <see cref="SecurityOverrideAllowlist"/> membership AND a <b>Trusted</b> integrity
    /// status (signature verified by a trusted public key) — an unsigned plugin that merely forges
    /// the allow-listed ModuleId in its manifest is not allowed to replace IAuthorizationService
    /// (refactor plan §15 fix).
    /// </summary>
    /// <param name="capturedOverrides">Captures the (serviceType, previousState) of each applied override,
    /// so the batch can be rolled back if OnLoad subsequently fails.</param>
    /// <returns>True if all overrides were applied successfully; false if a fatal failure occurred
    /// (in which case <paramref name="capturedOverrides"/> is in a partially-applied state and
    /// <see cref="RollbackOverrides"/> must be called).</returns>
    private bool TryApplyServiceOverrides(ModuleManifest manifest, Assembly pluginAssembly, PluginIntegrityStatus integrity,
        List<(Type ServiceType, object? PreviousState)> capturedOverrides)
    {
        foreach (ServiceOverride serviceOverride in manifest.ServiceOverrides)
        {
            try
            {
                Type? serviceType = ResolveTypeInDefaultContext(serviceOverride.ServiceType);
                if (serviceType == null)
                {
                    _logger.Warn(null, "[Plugins] ServiceOverride ignored: service type {0} not found in host",
                        serviceOverride.ServiceType);
                    continue;
                }

                // Security boundary (refactor plan §15): security-sensitive services such as
                // IAuthorizationService require BOTH allow-listed ModuleId AND Trusted integrity.
                // ModuleId alone is not sufficient — an unsigned plugin could forge it.
                if (serviceType == typeof(IAuthorizationService))
                {
                    if (!SecurityOverrideAllowlist.Contains(manifest.ModuleId ?? ""))
                    {
                        _logger.Warn(null, "[Plugins] ServiceOverride denied: {0} is not authorized to replace IAuthorizationService",
                            manifest.ModuleId);
                        continue;
                    }
                    if (integrity != PluginIntegrityStatus.Trusted)
                    {
                        _logger.Warn(null, "[Plugins] ServiceOverride denied: {0} is allow-listed but integrity is {1} (Trusted required) — refusing to replace IAuthorizationService",
                            manifest.ModuleId, integrity);
                        continue;
                    }
                }

                Type? implType = pluginAssembly.GetType(serviceOverride.ImplementationType);
                if (implType == null)
                {
                    _logger.Warn(null, "[Plugins] ServiceOverride ignored: implementation type {0} not found in plugin",
                        serviceOverride.ImplementationType);
                    continue;
                }

                if (implType.GetConstructor(Type.EmptyTypes) == null)
                {
                    _logger.Warn(null, "[Plugins] ServiceOverride ignored: {0} is missing a parameterless constructor",
                        implType.FullName ?? implType.Name);
                    continue;
                }

                object implementation = Activator.CreateInstance(implType)!;
                if (!serviceType.IsInstanceOfType(implementation))
                {
                    _logger.Warn(null, "[Plugins] ServiceOverride ignored: {0} does not implement {1}",
                        implType.FullName, serviceType.FullName);
                    continue;
                }

                // Capture the current (pre-override) service state before replacing, so we can roll back
                object? previousState = _serviceGetter(serviceType);
                _serviceReplacer(serviceType, implementation);
                capturedOverrides.Add((serviceType, previousState));

                _logger.Info(null, "[Plugins] Service replaced: {0} ← {1} ({2})",
                    serviceType.FullName, implType.FullName,
                    serviceOverride.Behavior ?? "ReplaceDefault");
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[Plugins] ServiceOverride application failed: {0}", ex.Message);
                return false;
            }
        }
        return true;
    }

    /// <summary>Resolves a type by its full name in the default load context (host assembly first).</summary>
    private static Type? ResolveTypeInDefaultContext(string typeFullName)
    {
        foreach (Assembly assembly in AssemblyLoadContext.Default.Assemblies)
        {
            Type? type = assembly.GetType(typeFullName);
            if (type != null)
                return type;
        }
        return null;
    }

    /// <summary>The host itself, the runtime framework, and assemblies already loaded in the default context are never loaded from the plugin directory.</summary>
    private static bool ShouldSkipAssembly(string assemblyName)
    {
        if (string.Equals(assemblyName, HostAssemblyName, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (string prefix in RuntimeNamePrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (string name in RuntimeAssemblyNames)
        {
            if (string.Equals(assemblyName, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Isolated load context for plugin assemblies. Not unloadable (tool instances injected by plugins
    /// are held by the host tool table for its lifetime and the assembly cannot, in fact, be unloaded);
    /// its value lies in dependency-resolution control: any assembly already present in the default
    /// context (host, shared framework, host dependencies) falls back to default resolution so that
    /// cross-boundary types such as IPlugin / ITool / ToolResult / IAuthorizationService share a single
    /// source with the host (type identity); all other dependencies are resolved from the plugin
    /// directory first.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly List<string> _searchDirectories;

        public PluginLoadContext(List<string> searchDirectories)
            : base(name: "ForgeMindPlugins") => _searchDirectories = searchDirectories;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (name == null)
                return null;

            foreach (Assembly loaded in Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    return null; // Fall back to the default context — type resolves to the host's source
            }

            foreach (string directory in _searchDirectories)
            {
                string candidate = Path.Combine(directory, name + ".dll");
                if (File.Exists(candidate))
                    return LoadFromAssemblyPath(candidate);
            }

            return null;
        }
    }
}