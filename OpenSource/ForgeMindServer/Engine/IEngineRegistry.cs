// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Engine;

/// <summary>
/// Read-only engine registry interface: plugins consume host-computed engine data through it.
/// <para><b>Key point: getters only — no <c>Register()</c> / <c>SetCurrentProject()</c> /
/// <c>Refresh()</c></b>. Engine registration is the host's responsibility; plugins can only
/// read and must never modify host state in reverse.</para>
/// </summary>
public interface IEngineRegistry
{
    /// <summary>Engine descriptors indexed by engine kind (at most one primary engine per kind).</summary>
    IReadOnlyDictionary<EngineKind, EngineDescriptor> Engines { get; }

    /// <summary>Current project information (null when no project is open).</summary>
    ProjectInfo? CurrentProject { get; }

    /// <summary>Gets the primary engine descriptor of the specified kind; returns null when not found.</summary>
    EngineDescriptor? GetEngine(EngineKind kind);
}
