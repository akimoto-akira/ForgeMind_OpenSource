// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMindServer.DSL.Catalog;

namespace ForgeMindServer.Abstractions;

/// <summary>
/// Host-side access point for the unified scan-data contract (refactor plan 5).
/// <para>Consumers - including plugins - read through <see cref="Current"/> and write
/// through <see cref="Reporter"/>. The read implementation is supplied by function B (the
/// engine scanner) via <see cref="ScanDataProviderRegistry"/>; the host no longer carries a
/// scan index of its own.</para>
/// </summary>
public static class ScanDataProvider
{
    /// <summary>
    /// Read surface. Forwards to the registered provider, or an empty permissive one when
    /// no plugin registered - consumers degrade to their lenient paths instead of failing.
    /// </summary>
    public static IScanDataProvider Current =>
        ScanDataProviderRegistry.Provider ?? EmptyScanDataProvider.Instance;

    /// <summary>
    /// Write surface. Bridge-reported parent-member data lands in the host-side index.
    /// </summary>
    public static IScanDataReporter Reporter =>
        ScanDataProviderRegistry.Reporter ?? ParentMemberReporter.Instance;
}

/// <summary>
/// Permissive no-data provider used when no scan provider is registered. Every query
/// answers "nothing found", so translation and validation take their lenient paths instead of
/// throwing. The two parent-member queries still answer for real: that data does live in the
/// host (source 2, bridge reflection).
/// </summary>
internal sealed class EmptyScanDataProvider : IScanDataProvider
{
    public static EmptyScanDataProvider Instance { get; } = new();

    private EmptyScanDataProvider()
    {
    }

    public IReadOnlyList<ScanRootView> Roots => [];

    public void EnqueueScan(string projectPath)
    {
        // No scanner in this process: nothing to queue.
    }

    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name) => [];

    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name, string? projectPath) => [];

    public IReadOnlyList<ScanFunctionView> CollectFunctions(string className) => [];

    public IReadOnlyList<ScanFunctionDeclarationView> FindFunctionDeclarations(string functionName) => [];

    public string? ResolveEventFunctionName(string? name, string? projectPath) => null;

    public string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName) => null;

    // No data at all, so a failed lookup is "nothing to go on" rather than "does not fit":
    // consumers take their lenient path instead of reporting an error.
    public bool HasIndexData => false;

    public bool TryFindFunction(
        string owner, string method, int positionalCount,
        IReadOnlyList<string>? namedArguments, out ScanFunctionSignature? signature)
    {
        signature = null;
        return false;
    }

    public ScanOwnerLookup TryFindFunctionOwner(
        string method, IReadOnlyList<string?>? argumentTypes,
        string? expectedReturnType, out string ownerClass)
    {
        ownerClass = null!;
        return ScanOwnerLookup.NoDeclaration;
    }

    public ScanParentMemberKind? GetParentMemberKind(string parentRef, string member) =>
        ParentMemberReporter.Instance.GetParentMemberKind(parentRef, member);

    public string? GetParentGeneratedClass(string parentRef) =>
        ParentMemberReporter.Instance.GetParentGeneratedClass(parentRef);

}

/// <summary>
/// Host-side bridge reflection reporter (source 2, primary): stores the parent-member
/// shapes reported through the bridge into <see cref="ParentAssetMemberIndex"/>.
/// </summary>
internal sealed class ParentMemberReporter : IScanDataReporter
{
    public static ParentMemberReporter Instance { get; } = new();

    private ParentMemberReporter()
    {
    }

    public ScanParentMemberKind? GetParentMemberKind(string parentRef, string member) =>
        ParentAssetMemberIndex.Current.GetKind(parentRef, member);

    public string? GetParentGeneratedClass(string parentRef) =>
        ParentAssetMemberIndex.Current.GetGeneratedClass(parentRef);

    public void ReportParentMembers(
        string parentRef, IReadOnlyDictionary<string, string> memberKinds, string? generatedClass = null)
    {
        if (string.IsNullOrWhiteSpace(parentRef))
            return;

        var kinds = new Dictionary<string, ScanParentMemberKind>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in memberKinds)
        {
            if (Enum.TryParse(entry.Value, ignoreCase: true, out ScanParentMemberKind kind))
                kinds[entry.Key] = kind;
        }

        if (kinds.Count > 0)
            ParentAssetMemberIndex.Current.Replace(parentRef, kinds, generatedClass);
    }

    public void ClearReportedParents() => ParentAssetMemberIndex.Current.Clear();

    public bool HasReportedParent(string parentRef) =>
        ParentAssetMemberIndex.Current.HasParent(parentRef);
}
