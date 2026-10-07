// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Authorization;
using ForgeMindServer.Plugins;

namespace ForgeMind.OfflineEngineKit;

/// <summary>
/// Offline Engine Kit plugin: registers the Unreal Engine offline tools
/// (project / engine / build / editor / launch / switch_version) with the
/// engine / permission / editor-bridge context computed by the host.
/// Tool behavior (MCP surface, version handling, command forwarding) lives here;
/// shared read utilities (project-file parsing, engine enumeration) are reached
/// through the host's public surface — the plugin only reads engine state, never
/// mutates host engine registry.
/// </summary>
public sealed class OfflineEngineKitPlugin : IPlugin
{
    public string Name => "ForgeMind.OfflineEngineKit";

    public string Version => "1.0.0";

    public string Description => "UE offline engine tool collection (project/project_files/engine/build/editor/launch/switch_version), migrated from the host as a standalone plugin";

    public ModuleIdentity Identity => new()
    {
        ModuleId = "ForgeMind.OfflineEngineKit",
        PublisherId = "ForgeMind",
        Version = "1.0.0",
        MinCoreVersion = "",
        MaxCoreVersion = "",
        Capabilities = ["offline-engine-kit", "project", "project_files", "engine", "build", "editor", "launch", "switch_version"]
    };

    public void OnLoad(IPluginContext context)
    {
        // The host discovered engines and the current project before calling
        // OnLoad; the plugin only reads them (never writes host engine state).
        var permChecker = new ServerPermissionChecker();

        context.RegisterTool(new ProjectTool(permChecker));
        context.RegisterTool(new ProjectFilesTool(permChecker));
        context.RegisterTool(new EngineTool(permChecker));
        context.RegisterTool(new BuildTool(permChecker));
        context.RegisterTool(new EditorTool(permChecker, context.EditorBridge!));
        context.RegisterTool(new LaunchTool(permChecker, context.EditorBridge!));
        context.RegisterTool(new SwitchVersionTool(permChecker));
    }

    public void OnUnload()
    {
        // Injected tool instances are held by the host tool table for the process
        // lifetime; nothing to reclaim here.
    }
}
