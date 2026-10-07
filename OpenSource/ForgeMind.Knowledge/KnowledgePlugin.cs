// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Authorization;
using ForgeMindServer.Engine;
using ForgeMindServer.Plugins;

namespace ForgeMind.Knowledge;

/// <summary>
/// Knowledge plugin: registers the UE knowledge tool (<see cref="KnowledgeTool"/>)
/// with the engine/permission context computed by the host.
/// The engine data it consumes is read-only (see <see cref="IEngineRegistry"/>) —
/// the plugin never mutates host engine state.
/// </summary>
public sealed class KnowledgePlugin : IPlugin
{
    public string Name => "ForgeMind.Knowledge";

    public string Version => "1.0.0";

    public string Description => "UE 知识库工具（list / search / get），由宿主迁出为独立插件";

    public ModuleIdentity Identity => new()
    {
        ModuleId = "ForgeMind.Knowledge",
        PublisherId = "ForgeMind",
        Version = "1.0.0",
        MinCoreVersion = "",
        MaxCoreVersion = "",
        Capabilities = ["knowledge"]
    };

    public void OnLoad(IPluginContext context)
    {
        // The host discovered engines and the current project before calling
        // OnLoad; the plugin only reads them (never writes host engine state).
        EngineDescriptor? ue = context.Engines.GetEngine(EngineKind.Unreal);
        ProjectInfo? project = context.Engines.CurrentProject;

        var permChecker = new ServerPermissionChecker();
        context.RegisterTool(new KnowledgeTool(ue, project, permChecker));
    }

    public void OnUnload()
    {
        // Injected tool instances are held by the host tool table for the process
        // lifetime; nothing to reclaim here.
    }
}

/// <summary>
/// Open-source permission guard: passes every tool (no commercial entitlement
/// restrictions). A commercial build that replaces <see cref="IAuthorizationService"/>
/// can supply a rate-limited guard instead. Kept parameterless here because the
/// open-source authorization service always permits; the plugin gets no
/// authorization surface beyond what <see cref="IPluginContext"/> exposes.
/// </summary>
internal sealed class ServerPermissionChecker
{
    /// <summary>Returns null to allow the tool; a string is the rejection reason.</summary>
    public string? RequirePermission(string toolName) => null;
}
