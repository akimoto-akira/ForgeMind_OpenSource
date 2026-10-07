// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Bridge;
using ForgeMindServer.Compat;

namespace ForgeMindServer.DSL;

/// <summary>
/// IR conversion contract (refactor plan §4.5 — the host keeps the skeleton).
/// <para>The host declares how a batch of DSL blocks is turned into IR; function C
/// (the DSL plugin) provides the implementation. The signature deliberately uses only
/// public types so the plugin can implement it.</para>
/// <para>Wiring (implementation registration) happens in a later step; this step only
/// establishes the contract.</para>
/// </summary>
public interface IIrConverter
{
    /// <summary>
    /// Converts a batch of DSL source into Batch IR.
    /// <para>One parse, many translators: parse → split blocks → translate each → aggregate.
    /// Any block producing an error diagnostic aborts the whole batch and no IR is
    /// returned.</para>
    /// </summary>
    /// <param name="dslCode">Full DSL source text.</param>
    /// <param name="engineVersion">Target engine version, e.g. <c>"5.6"</c>.</param>
    /// <param name="compile">Whether to compile; null takes the domain default.</param>
    /// <param name="save">Whether to save; null takes the auto-save default.</param>
    /// <param name="session">Bridge session; null for offline translation.</param>
    /// <param name="path">Project <c>.uproject</c> path; null when not project-scoped.</param>
    /// <param name="logger">Logger; null when unavailable.</param>
    /// <param name="batchIrJson">
    /// Batch IR JSON <c>{ version, batch: true, assets: [...] }</c> on success, null otherwise.
    /// </param>
    /// <param name="errors">Aggregated diagnostics; empty on success.</param>
    /// <returns>True when every block passed and IR was produced.</returns>
    bool TryAssemble(
        string dslCode,
        string engineVersion,
        bool? compile,
        bool? save,
        BridgeSession? session,
        string? path,
        ILogger? logger,
        out string? batchIrJson,
        out IReadOnlyList<string> errors);
}
