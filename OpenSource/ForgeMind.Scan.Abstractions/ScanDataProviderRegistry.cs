// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Registration point for unified scan-data providers.
/// <para>Providers are peers, not replacements: each one registers a callback that can answer
/// queries from its own backing store — a frozen SPK bundle, a live in-memory index, an
/// on-demand bridge query. Every one of them stays registered.</para>
/// <para>
/// They are asked as a chain, last registered first: the provider that joined most recently
/// answers first, and only when it has no answer does the query fall through to the next one,
/// until one answers or all of them have been asked. See <see cref="ChainScanDataProvider"/>.
/// Registration order therefore encodes priority, and a later registration never removes an
/// earlier one.</para>
/// </summary>
public static class ScanDataProviderRegistry
{
    private static readonly List<IScanDataProvider> _providers = [];
    private static readonly object _gate = new();

    private static IScanDataReporter? _reporter;
    private static ChainScanDataProvider? _chain;

    /// <summary>
    /// Registers a read provider, and optionally a write reporter. Registering the same
    /// provider instance twice is a no-op.
    /// </summary>
    public static void Register(IScanDataProvider provider, IScanDataReporter? reporter = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_gate)
        {
            if (!_providers.Contains(provider))
            {
                _providers.Add(provider);
                _chain = null; // chain is cached; a new member invalidates it
            }
        }

        if (reporter is not null)
            _reporter = reporter;
    }

    /// <summary>Every registered provider, in registration order (last one is asked first).</summary>
    public static IReadOnlyList<IScanDataProvider> Providers
    {
        get
        {
            lock (_gate)
                return _providers.ToArray();
        }
    }

    /// <summary>
    /// The chain over <see cref="Providers"/>, or null when nothing has registered. A single
    /// registration is returned as-is — a chain of one adds nothing.
    /// </summary>
    public static IScanDataProvider? Provider
    {
        get
        {
            lock (_gate)
            {
                if (_providers.Count == 0)
                    return null;

                if (_providers.Count == 1)
                    return _providers[0];

                // Last joined answers first.
                IScanDataProvider[] newestFirst = _providers.AsEnumerable().Reverse().ToArray();
                return _chain ??= new ChainScanDataProvider(newestFirst);
            }
        }
    }

    /// <summary>The registered write reporter, or null to keep the host-side one.</summary>
    public static IScanDataReporter? Reporter => _reporter;

    /// <summary>Clears every registration (host shutdown / test isolation).</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _providers.Clear();
            _chain = null;
            _reporter = null;
        }
    }
}
