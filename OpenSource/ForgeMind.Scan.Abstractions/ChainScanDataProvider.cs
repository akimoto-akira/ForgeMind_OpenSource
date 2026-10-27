// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Chain over the registered scan-data providers: they are <b>peers</b>, not layers to blend.
/// <para>
/// Providers are held in the order given, which is the order they are asked — registration
/// order reversed, so the one that joined last answers first. A query moves to the next
/// provider only when the current one has no answer (empty list / null / false), and stops at
/// the first answer. Nothing is merged: no deduping, no blending of two providers' views of
/// the same class — whoever answers first is the answer.</para>
/// <para>
/// <see cref="EnqueueScan"/> is the one exception: it is a write, not a query, so it is
/// broadcast to every provider rather than stopping at the first.</para>
/// </summary>
public sealed class ChainScanDataProvider : IScanDataProvider
{
    private readonly IReadOnlyList<IScanDataProvider> _providers;

    /// <summary>Creates a chain asked in the order given (first element is asked first).</summary>
    public ChainScanDataProvider(IReadOnlyList<IScanDataProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <summary>The providers in the order this chain asks them.</summary>
    public IReadOnlyList<IScanDataProvider> Providers => _providers;

    /// <inheritdoc />
    /// <remarks>First non-empty snapshot wins; a provider with no roots yet is skipped.</remarks>
    public IReadOnlyList<ScanRootView> Roots
    {
        get
        {
            foreach (IScanDataProvider provider in _providers)
            {
                IReadOnlyList<ScanRootView> roots = provider.Roots;
                if (roots.Count > 0)
                    return roots;
            }

            return [];
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Availability is a whole-chain property: any provider holding data means the host has
    /// data, so consumers stop taking the lenient path.
    /// </remarks>
    public bool HasIndexData
    {
        get
        {
            foreach (IScanDataProvider provider in _providers)
            {
                if (provider.HasIndexData)
                    return true;
            }

            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>Broadcast: every provider gets the chance to queue the path.</remarks>
    public void EnqueueScan(string projectPath)
    {
        foreach (IScanDataProvider provider in _providers)
            provider.EnqueueScan(projectPath);
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            IReadOnlyList<ScanClassView> result = provider.FindClassesFlexible(name);
            if (result.Count > 0)
                return result;
        }

        return [];
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name, string? projectPath)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            IReadOnlyList<ScanClassView> result = provider.FindClassesFlexible(name, projectPath);
            if (result.Count > 0)
                return result;
        }

        return [];
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanFunctionView> CollectFunctions(string className)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            IReadOnlyList<ScanFunctionView> result = provider.CollectFunctions(className);
            if (result.Count > 0)
                return result;
        }

        return [];
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanFunctionDeclarationView> FindFunctionDeclarations(string functionName)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            IReadOnlyList<ScanFunctionDeclarationView> result = provider.FindFunctionDeclarations(functionName);
            if (result.Count > 0)
                return result;
        }

        return [];
    }

    /// <inheritdoc />
    public string? ResolveEventFunctionName(string? name, string? projectPath)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            string? resolved = provider.ResolveEventFunctionName(name, projectPath);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    /// <inheritdoc />
    public string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            string? resolved = provider.ResolveEventFunctionName(name, projectPath, parentClassName);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    /// <inheritdoc />
    public bool TryFindFunction(
        string owner, string method, int positionalCount,
        IReadOnlyList<string>? namedArguments, out ScanFunctionSignature? signature)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            if (provider.TryFindFunction(owner, method, positionalCount, namedArguments, out signature))
                return true;
        }

        signature = null;
        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Falls through until a provider resolves the owner. When none does, the strongest signal
    /// seen is returned instead of the last one's — the contract treats "no such name" and
    /// "such a name that does not fit" as opposite outcomes, so a <see cref="ScanOwnerLookup.NoTypeMatch"/>
    /// from any provider outranks a <see cref="ScanOwnerLookup.NoDeclaration"/>.
    /// </remarks>
    public ScanOwnerLookup TryFindFunctionOwner(
        string method, IReadOnlyList<string?>? argumentTypes,
        string? expectedReturnType, out string ownerClass)
    {
        ScanOwnerLookup strongest = ScanOwnerLookup.NoDeclaration;
        string strongestOwner = "";

        foreach (IScanDataProvider provider in _providers)
        {
            ScanOwnerLookup lookup = provider.TryFindFunctionOwner(
                method, argumentTypes, expectedReturnType, out string candidate);

            if (lookup == ScanOwnerLookup.Resolved)
            {
                ownerClass = candidate;
                return lookup;
            }

            if (lookup == ScanOwnerLookup.NoTypeMatch && strongest != ScanOwnerLookup.NoTypeMatch)
            {
                strongest = lookup;
                strongestOwner = candidate;
            }
        }

        ownerClass = strongestOwner;
        return strongest;
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanPropertyView> CollectProperties(string className)
    {
        // First provider that actually has the member wins; an empty answer from one provider means
        // "not in this data source", not "the class has no properties" — keep asking (spec §7).
        foreach (IScanDataProvider provider in _providers)
        {
            IReadOnlyList<ScanPropertyView> props = provider.CollectProperties(className);
            if (props.Count > 0)
                return props;
        }

        return [];
    }

    /// <inheritdoc />
    public ScanParentMemberKind? GetParentMemberKind(string parentRef, string member)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            ScanParentMemberKind? kind = provider.GetParentMemberKind(parentRef, member);
            if (kind is not null)
                return kind;
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetParentGeneratedClass(string parentRef)
    {
        foreach (IScanDataProvider provider in _providers)
        {
            string? generated = provider.GetParentGeneratedClass(parentRef);
            if (generated is not null)
                return generated;
        }

        return null;
    }
}
