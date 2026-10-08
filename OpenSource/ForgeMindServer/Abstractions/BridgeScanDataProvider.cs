// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMindServer.DSL.Catalog;

namespace ForgeMindServer.Abstractions;

/// <summary>
/// Bridge-reflection read provider (source 3): serves the parent blueprint-asset member
/// shapes reported through the bridge (ParentAssetPrefetcher → IScanDataReporter →
/// ParentAssetMemberIndex) through the unified scan-data contract.
/// <para>
/// The host registers it after the engine is hooked up (plugins have run their OnLoad) —
/// the LAST provider to join, so by the chain's "last joined answers first" rule it is
/// asked before the scan indexes. Join order follows data readiness, and the query order
/// is freshness descending: bridge reflection (live, session-created assets), then the
/// live scan index, then the frozen engine index.</para>
/// <para>
/// The two parent-member queries are the only real answers: the bridge holds no class
/// roots, no function signatures, and no event-name mappings — every other query reports
/// "nothing found" so the chain falls through. <see cref="HasIndexData"/> stays false: this
/// source owns no scan data, so it must never flip a consumer out of its lenient path by
/// itself.</para>
/// </summary>
public sealed class BridgeScanDataProvider : IScanDataProvider
{
    /// <summary>Process-wide single registration instance (the bridge index it reads is a singleton too).</summary>
    public static BridgeScanDataProvider Instance { get; } = new();

    private BridgeScanDataProvider()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanRootView> Roots => [];

    /// <inheritdoc />
    /// <remarks>The bridge reflects asset members on demand; nothing to queue.</remarks>
    public void EnqueueScan(string projectPath)
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name) => [];

    /// <inheritdoc />
    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name, string? projectPath) => [];

    /// <inheritdoc />
    public IReadOnlyList<ScanFunctionView> CollectFunctions(string className) => [];

    /// <inheritdoc />
    public IReadOnlyList<ScanFunctionDeclarationView> FindFunctionDeclarations(string functionName) => [];

    /// <inheritdoc />
    public string? ResolveEventFunctionName(string? name, string? projectPath) => null;

    /// <inheritdoc />
    public string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName) => null;

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately false: this source has no scan data of its own — the parent-member
    /// queries answer regardless (that data lives here whether or not a scan index does).
    /// </remarks>
    public bool HasIndexData => false;

    /// <inheritdoc />
    public bool TryFindFunction(
        string owner, string method, int positionalCount,
        IReadOnlyList<string>? namedArguments, out ScanFunctionSignature? signature)
    {
        signature = null;
        return false;
    }

    /// <inheritdoc />
    public ScanOwnerLookup TryFindFunctionOwner(
        string method, IReadOnlyList<string?>? argumentTypes,
        string? expectedReturnType, out string ownerClass)
    {
        ownerClass = null!;
        return ScanOwnerLookup.NoDeclaration;
    }

    /// <inheritdoc />
    public ScanParentMemberKind? GetParentMemberKind(string parentRef, string member) =>
        ParentAssetMemberIndex.Current.GetKind(parentRef, member);

    /// <inheritdoc />
    public string? GetParentGeneratedClass(string parentRef) =>
        ParentAssetMemberIndex.Current.GetGeneratedClass(parentRef);
}