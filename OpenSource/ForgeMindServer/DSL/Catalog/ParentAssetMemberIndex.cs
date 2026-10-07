// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System;
using System.Collections.Generic;

using ForgeMind.Scan.Abstractions;
using ForgeMindServer.Abstractions;

namespace ForgeMindServer.DSL.Catalog;

/// <summary>Parent-class blueprint asset member shapes (for the "subclass member shape must match parent" validation).</summary>
// Member shapes now live on the unified contract as ScanScanParentMemberKind (Abstractions).

/// <summary>
/// Parent-class (blueprint asset) member-shape index: an in-process singleton, filled by
/// <see cref="ParentAssetPrefetcher"/> after reading online through the bridge; translators
/// are pure functions (Core Syntax.md §11.1 forbids direct bridge connections) and only query
/// this index read-only.
/// <para>
/// Key = bare reference of <c>[Extends]</c> in DSL: <c>typeof(X)</c> → "X"; path strings kept as-is.
/// Matches the key produced by <see cref="BlueprintTranslator"/>'s internal <c>ExtractTypeRef</c>.
/// </para>
/// </summary>
public sealed class ParentAssetMemberIndex
{
    public static ParentAssetMemberIndex Current { get; } = new();

    // parentRef → (member name → shape). Filled within one batch, cleared across batches (see ParentAssetPrefetcher).
    private readonly Dictionary<string, IReadOnlyDictionary<string, ScanParentMemberKind>> _map =
        new(StringComparer.Ordinal);

    // parentRef → blueprint generated class (<AssetName>_C). Only needed by cross-blueprint call
    // targets (DSL-generated / in-project blueprint assets that are not [Extends] parent classes);
    // the bridge-side add_blueprint_node resolves its class against the generated class
    // (identical shape to the A-side MCP ownerClass="Lib_Static_A_C").
    private readonly Dictionary<string, string> _genClasses = new(StringComparer.Ordinal);

    /// <summary>Cleared before each new batch to avoid stale cross-test / cross-batch data pollution (key contains only the asset name, prone to collisions).</summary>
    public void Clear()
    {
        _map.Clear();
        _genClasses.Clear();
    }

    public void Replace(string parentRef, IReadOnlyDictionary<string, ScanParentMemberKind> kinds)
        => Replace(parentRef, kinds, null);

    public void Replace(string parentRef, IReadOnlyDictionary<string, ScanParentMemberKind> kinds, string? generatedClass)
    {
        if (string.IsNullOrEmpty(parentRef))
            return;
        _map[parentRef] = kinds;
        if (generatedClass is not null)
            _genClasses[parentRef] = generatedClass;
        else
            _genClasses.Remove(parentRef);
    }

    /// <summary>Blueprint generated class name (e.g. "Lib_Static_B_C"); not a blueprint asset / not registered → null.</summary>
    public string? GetGeneratedClass(string parentRef) =>
        _genClasses.TryGetValue(parentRef, out var c) ? c : null;

    /// <summary>Queries the shape of a given parent member; unknown parent / member not on parent → null (no guessing, no blocking).</summary>
    public ScanParentMemberKind? GetKind(string parentRef, string member)
    {
        if (string.IsNullOrEmpty(parentRef) || string.IsNullOrEmpty(member))
            return null;
        return _map.TryGetValue(parentRef, out var dict) && dict.TryGetValue(member, out var k)
            ? k
            : null;
    }

    public bool HasParent(string parentRef) =>
        !string.IsNullOrEmpty(parentRef) && _map.ContainsKey(parentRef);
}
