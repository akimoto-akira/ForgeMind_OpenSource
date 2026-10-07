// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMindServer.Abstractions;
using ForgeMindServer.Authorization;
using ForgeMindServer.Plugins;

namespace ForgeMind.EngineScanner;

/// <summary>
/// Engine scanner plugin (function B).
/// <para>Its main surface is the standalone command line entry (<see cref="Program"/>):
/// it enumerates the engines installed on this machine, scans the ones without a cache,
/// and writes the results to disk — replacing the Debug-only "scan every installed engine
/// version at host startup" behavior. The host does not need to be running for that.</para>
/// <para>This plugin registers no tool. It registers the unified scan-data provider
/// instead (<see cref="ScanIndexDataProvider"/>), which is how the host and every other
/// plugin read scan results: the scanning module stays inside this assembly and is
/// reached only through the contract.</para>
/// </summary>
public sealed class EngineScannerPlugin : IPlugin
{
    public string Name => "ForgeMind.EngineScanner";

    public string Version => "1.0.0";

    public string Description =>
        "Engine code and asset scanner; standalone command line entry that enumerates " +
        "installed engines and writes scan caches";

    public ModuleIdentity Identity => new()
    {
        ModuleId = "ForgeMind.EngineScanner",
        PublisherId = "ForgeMind",
        Version = "1.0.0",
        MinCoreVersion = "",
        MaxCoreVersion = "",
        Capabilities = ["engine-scanner", "scan"]
    };

    public void OnLoad(IPluginContext context)
    {
        // Become the host's scan-data source: the host and every other plugin read scan
        // results through the unified contract, never through this module's types.
        ScanIndexDataProvider provider = new();
        ScanIndexDataProvider.Instance = provider;
        ScanDataProviderRegistry.Register(provider);
    }

    public void OnUnload()
    {
        // No state to reclaim.
    }
}
