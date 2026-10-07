// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Bridge;
using ForgeMindServer.Compat;

namespace ForgeMindServer.DSL;

/// <summary>
/// Registry of translator instances (refactor plan §4.5 — the host keeps the skeleton).
/// <para>The host no longer holds a hard-coded switch over the translators. Function C
/// (the DSL plugin) registers a factory per asset type while loading; the host resolves
/// by asset type at translation time. Adding a domain means registering a factory rather
/// than editing host code.</para>
/// <para>Registration is last-writer-wins and idempotent per asset type.</para>
/// </summary>
public static class TranslatorRegistry
{
    /// <summary>
    /// Creates a translator for one translation run. Mirrors what a translator needs at
    /// construction time, so registration can capture the type rather than an instance.
    /// </summary>
    public delegate ITranslator TranslatorFactory(
        string engineVersion, BridgeSession? session, string? path, ILogger? logger);

    private static readonly Dictionary<string, TranslatorFactory> Factories = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the factory for an asset type (snake_case), e.g. <c>"blueprint"</c>.
    /// A later registration for the same asset type replaces the earlier one.
    /// </summary>
    public static void Register(string assetType, TranslatorFactory factory)
    {
        if (string.IsNullOrWhiteSpace(assetType) || factory == null)
            return;

        lock (Factories)
            Factories[assetType.Trim()] = factory;
    }

    /// <summary>
    /// Creates a translator for the asset type, or returns null when the asset type has
    /// no registered factory.
    /// </summary>
    public static ITranslator? TryCreate(
        string assetType, string engineVersion, BridgeSession? session, string? path, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(assetType))
            return null;

        TranslatorFactory? factory;
        lock (Factories)
        {
            if (!Factories.TryGetValue(assetType.Trim(), out factory))
                return null;
        }

        return factory(engineVersion, session, path, logger);
    }

    /// <summary>Whether an asset type has a registered factory.</summary>
    public static bool IsRegistered(string assetType)
    {
        if (string.IsNullOrWhiteSpace(assetType))
            return false;

        lock (Factories)
            return Factories.ContainsKey(assetType.Trim());
    }

    /// <summary>All currently registered asset types.</summary>
    public static IReadOnlyCollection<string> RegisteredAssetTypes
    {
        get
        {
            lock (Factories)
                return Factories.Keys.ToArray();
        }
    }

    /// <summary>Drops every registration (host shutdown / test isolation).</summary>
    public static void Clear()
    {
        lock (Factories)
            Factories.Clear();
    }
}
