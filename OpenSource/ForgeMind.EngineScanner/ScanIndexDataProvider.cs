// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMind.Scan.Unreal;
using ForgeMindServer.Abstractions;

namespace ForgeMind.EngineScanner;

/// <summary>
/// Function B's implementation of the unified scan-data contract: serves the scan index
/// it builds and exposes it to the host and to other plugins through the contract.
/// <para>Registered by <see cref="EngineScannerPlugin"/> while loading, so the host reads
/// scan data without ever touching the scanning module directly.</para>
/// </summary>
internal sealed class ScanIndexDataProvider : ScanSignatureCatalog, IScanDataProvider
{
    private const string PureSpecifier = "BlueprintPure";

    /// <summary>The registered instance, so the inherited signature catalog can reach the contract.</summary>
    internal static ScanIndexDataProvider? Instance { get; set; }

    private static ScanIndexStore Store => ScanIndexStore.Current;

    // ===== read side =====

    public IReadOnlyList<ScanRootView> Roots =>
        Store.Roots
            .Select(r => new ScanRootView(
                r.RootPath,
                r.Version,
                r.Modules.Select(m => m.Name).ToArray(),
                r.Plugins.Select(p => p.Name).ToArray()))
            .ToArray();

    public void EnqueueScan(string projectPath) => Store.EnqueueScan(projectPath);

    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name) =>
        ToViews(Store.FindClassesFlexible(name));

    public IReadOnlyList<ScanClassView> FindClassesFlexible(string name, string? projectPath) =>
        ToViews(Store.FindClassesFlexible(name, projectPath));

    public IReadOnlyList<ScanFunctionView> CollectFunctions(string className) =>
        CollectFunctions(className, null);

    public IReadOnlyList<ScanFunctionView> CollectFunctions(string className, string? projectPath)
    {
        IReadOnlyList<ScanClassView> classes = string.IsNullOrWhiteSpace(projectPath)
            ? FindClassesFlexible(className)
            : FindClassesFlexible(className, projectPath);

        // Functions come back aggregated per class (inherited included); merge across
        // matches, keeping first-seen order and dropping duplicate signatures.
        var merged = new List<ScanFunctionView>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (ScanClassView cls in classes)
        {
            foreach (ScanFunctionView fn in CollectFromStore(cls.Name, projectPath))
            {
                if (seen.Add(fn.Name + "/" + fn.Parameters.Count))
                    merged.Add(fn);
            }
        }

        return merged;
    }

    public IReadOnlyList<ScanFunctionDeclarationView> FindFunctionDeclarations(string functionName) =>
        Store.FindFunctionDeclarations(functionName)
            .Select(d => new ScanFunctionDeclarationView(ToView(d.Class), ToView(d.Function)))
            .ToArray();

    public string? ResolveEventFunctionName(string? name, string? projectPath) =>
        Store.ResolveEventFunctionName(name, projectPath);

    public string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName) =>
        Store.ResolveEventFunctionName(name, projectPath, parentClassName);

    // Parent-member shapes are source 2 (bridge reflection) and live in the host; this
    // provider does not own them, so it reports "unknown" rather than inventing data.
    public ScanParentMemberKind? GetParentMemberKind(string parentRef, string member) => null;

    public string? GetParentGeneratedClass(string parentRef) => null;

    // ===== mapping helpers =====

    private IReadOnlyList<ScanFunctionView> CollectFromStore(string className, string? projectPath)
    {
        UeClassInfo? cls = (string.IsNullOrWhiteSpace(projectPath)
                ? Store.FindClassesFlexible(className).FirstOrDefault()
                : Store.FindClassesFlexible(className, projectPath).FirstOrDefault())
            as UeClassInfo;

        return cls == null ? [] : Store.CollectFunctions(cls).Select(ToView).ToArray();
    }

    private static IReadOnlyList<ScanClassView> ToViews(IEnumerable<UeClassInfo> classes) =>
        classes.Select(ToView).ToArray();

    private static ScanClassView ToView(UeClassInfo cls) =>
        new(
            cls.Name,
            cls.BaseClasses.Count > 0 ? cls.BaseClasses[0] : null,
            cls.BaseClasses.ToArray(),
            (int)cls.Scope);

    private static ScanFunctionView ToView(UeFunctionInfo fn) =>
        new(
            fn.Name,
            fn.IsStatic,
            fn.Specifiers.Contains(PureSpecifier, StringComparer.OrdinalIgnoreCase),
            fn.Parameters.Select(p => new ScanParameterView(p.Name, p.Type)).ToArray(),
            fn.FilePath,
            fn.LineNumber,
            fn.ReturnType);
}
