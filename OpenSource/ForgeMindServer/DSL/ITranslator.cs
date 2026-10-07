// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using Microsoft.CodeAnalysis;

namespace ForgeMindServer.DSL;

/// <summary>A single using alias: using Name = "Target";</summary>
public sealed record UsingAlias(string Name, string Target);

/// <summary>
/// The complete context for a single top-level declaration block (AI Collaboration Spec.md §2.3).
/// </summary>
public sealed class BlockContext
{
    /// <summary>The top-level declaration node for this block (including leading attribute list).</summary>
    public SyntaxNode Root { get; init; } = null!;

    /// <summary>The original DSL text for this block (used for diagnostic line positioning).</summary>
    public string BlockText { get; init; } = "";

    /// <summary>The regex-detected assetType (snake_case).</summary>
    public string AssetType { get; init; } = "";

    /// <summary>The using aliases in effect for this block (file-level + block-level).</summary>
    public IReadOnlyList<UsingAlias> Usings { get; init; } = Array.Empty<UsingAlias>();

    /// <summary>The block-level diagnostic reporter.</summary>
    public DiagnosticReporter Diagnostics { get; init; } = new();
}

/// <summary>Domain translator interface (AI Collaboration Spec.md §2.1 / Core Syntax.md §10.2).</summary>
public interface ITranslator
{
    /// <summary>Injects using aliases for this block (file-level + block-level; later block-level overrides same-named file-level).</summary>
    void SetUsings(IReadOnlyList<UsingAlias> usings);

    /// <summary>Walks the pre-parsed syntax tree nodes and assembles them into a single-asset IR JSON.</summary>
    /// <param name="blockRoot">The top-level declaration node parsed and split by ParserFactory.</param>
    /// <param name="blockText">The original DSL text for this block (used for diagnostic line positioning).</param>
    /// <param name="engineVersion">Engine version.</param>
    /// <param name="compile">Whether to compile.</param>
    /// <param name="save">Whether to save.</param>
    /// <param name="projectPath">The .uproject path of the owning project — index queries use it to determine scope (can be null).</param>
    string Translate(SyntaxNode blockRoot, string blockText, string engineVersion, bool? compile, bool? save, string? projectPath = null);

    /// <summary>The assetType value (snake_case) handled by this translator.</summary>
    string DomainAssetType { get; }

    /// <summary>The attribute name array recognized by this translator (used by ParserFactory for regex detection).</summary>
    string[] DomainAttributes { get; }

    /// <summary>Diagnostic reporter.</summary>
    DiagnosticReporter Diagnostics { get; }
}
