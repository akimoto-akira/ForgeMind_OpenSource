// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Unified scan-data contract — write side.
/// <para>The contract is bidirectional: a read-only surface cannot host all data
/// sources, because the bridge reflection reporter <b>pushes</b> data in rather than
/// being queried.</para>
/// <para>Member kinds cross the boundary as plain strings ("event" / "function" /
/// "variable" / "component") so the contract stays engine- and implementation-agnostic.</para>
/// </summary>
public interface IScanDataReporter
{
    /// <summary>
    /// Reports the member shapes of a parent blueprint asset, as read through the bridge.
    /// Keys are member names, values are member kinds.
    /// </summary>
    /// <param name="parentRef">Bare reference as written in DSL <c>[Extends]</c>.</param>
    /// <param name="memberKinds">Member name → kind ("event" / "function" / "variable" / "component").</param>
    /// <param name="generatedClass">Generated class of the asset (<c>&lt;AssetName&gt;_C</c>) when known;
    /// needed by cross-blueprint call targets.</param>
    void ReportParentMembers(
        string parentRef,
        IReadOnlyDictionary<string, string> memberKinds,
        string? generatedClass = null);

    /// <summary>
    /// Drops every reported parent asset. Callers report a fresh batch at the start of a
    /// translation run, so stale reports from a previous run must not leak into this one.
    /// </summary>
    void ClearReportedParents();

    /// <summary>
    /// Whether anything at all is currently reported for a parent asset. Lets a reporter skip
    /// assets it has already resolved instead of querying the bridge twice.
    /// </summary>
    bool HasReportedParent(string parentRef);
}