// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.DSL;

/// <summary>
/// Host-side registry of the IR conversion implementation (refactor plan 4.5).
/// <para>The host keeps only the contract; function C (the DSL plugin) supplies the
/// implementation and registers it while loading. The host then assembles batch IR
/// through <see cref="Current"/> instead of calling a concrete assembler.</para>
/// <para>Last-writer-wins; a later plugin replaces an earlier registration.</para>
/// </summary>
public static class IrConverterRegistry
{
    private static IIrConverter? _converter;

    /// <summary>Registers the IR conversion implementation.</summary>
    public static void Register(IIrConverter converter) => _converter = converter;

    /// <summary>
    /// The registered converter, or null when no plugin has registered one - the host
    /// then reports that DSL translation is unavailable rather than silently degrading.
    /// </summary>
    public static IIrConverter? Current => _converter;

    /// <summary>Drops the registration (host shutdown / test isolation).</summary>
    public static void Clear() => _converter = null;
}
