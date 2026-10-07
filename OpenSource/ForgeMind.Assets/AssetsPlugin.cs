// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Authorization;
using ForgeMindServer.Plugins;

namespace ForgeMind.Assets;

/// <summary>
/// Assets plugin: registers the offline asset reading tool with the permission
/// context computed by the host.
/// <para>The tool reads asset binaries straight from disk — no engine runtime and no
/// editor connection. Which engine dialect is used to read an asset is decided by
/// <c>EngineVersion</c> inside the tool, so the tool name stays engine-agnostic
/// ("asset").</para>
/// </summary>
public sealed class AssetsPlugin : IPlugin
{
    public string Name => "ForgeMind.Assets";

    public string Version => "1.0.0";

    public string Description =>
        "Offline asset reading tool (asset), migrated out of the host into a standalone plugin";

    public ModuleIdentity Identity => new()
    {
        ModuleId = "ForgeMind.Assets",
        PublisherId = "ForgeMind",
        Version = "1.0.0",
        MinCoreVersion = "",
        MaxCoreVersion = "",
        Capabilities = ["asset"]
    };

    public void OnLoad(IPluginContext context)
    {
        var permChecker = new ServerPermissionChecker();
        context.RegisterTool(new AssetTool(permChecker));
    }

    public void OnUnload()
    {
        // Injected tool instances are held by the host tool table for the process
        // lifetime; nothing to reclaim here.
    }
}
