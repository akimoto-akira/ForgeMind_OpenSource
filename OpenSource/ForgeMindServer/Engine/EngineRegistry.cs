// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Engine;

namespace ForgeMindServer.Engine;

/// <summary>
/// Host-side engine registry: written by the host's internal engine/project discovery
/// logic via <see cref="Register"/> / <see cref="SetCurrentProject"/>, then exposes
/// a read-only snapshot for plugins via <see cref="GetSnapshot"/>.
/// <para>Plugins never hold this type directly — they consume data only through
/// <see cref="IEngineRegistry"/>, so the boundary that engine data is computed by the host
/// and read-only for plugins is strictly enforced.</para>
/// </summary>
public sealed class EngineRegistry
{
    private readonly Dictionary<EngineKind, EngineDescriptor> _engines = new();
    private ProjectInfo? _currentProject;

    /// <summary>Registers (or overwrites) the primary engine descriptor of a given kind. Called by the host.</summary>
    public void Register(EngineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _engines[descriptor.Kind] = descriptor;
    }

    /// <summary>Sets the current project. Called by the host.</summary>
    public void SetCurrentProject(ProjectInfo project)
    {
        _currentProject = project;
    }

    /// <summary>Generates a read-only snapshot for plugins (deep-copies current state; subsequent host writes do not affect snapshots already issued).</summary>
    public IEngineRegistry GetSnapshot() => new Snapshot(this);

    private sealed class Snapshot : IEngineRegistry
    {
        private readonly IReadOnlyDictionary<EngineKind, EngineDescriptor> _engines;
        private readonly ProjectInfo? _currentProject;

        public Snapshot(EngineRegistry source)
        {
            _engines = new Dictionary<EngineKind, EngineDescriptor>(source._engines);
            _currentProject = source._currentProject;
        }

        public IReadOnlyDictionary<EngineKind, EngineDescriptor> Engines => _engines;

        public ProjectInfo? CurrentProject => _currentProject;

        public EngineDescriptor? GetEngine(EngineKind kind) =>
            _engines.TryGetValue(kind, out EngineDescriptor? descriptor) ? descriptor : null;
    }
}
