// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// A class as seen by contract consumers (function C and any other plugin).
/// <para>A view type rather than the scanner's own model class: the scan model belongs to
/// the scanning implementation, and a public contract must not expose it.</para>
/// </summary>
/// <param name="Name">Type name including its prefix, e.g. <c>AActor</c>.</param>
/// <param name="SuperClass">Declared base type name; null or empty when none is declared.</param>
/// <param name="BaseClasses">Every declared base type, in declaration order. Needed because a
/// type may list several (interfaces plus a superclass): derivation checks walk the whole list,
/// so a single base name would silently truncate the chain.</param>
public sealed record ScanClassView(
    string Name,
    string? SuperClass,
    IReadOnlyList<string>? BaseClasses = null,
    int ScopeRank = 0)
{
    /// <summary>
    /// Ordinal rank of this type's proximity to the engine itself — higher wins when several
    /// candidates match a call. Exposed as a plain number so the contract stays engine-agnostic:
    /// the provider decides what the ordering means, consumers only compare.
    /// </summary>
    public int Scope => ScopeRank;

    /// <summary>
    /// Every declared base type, never null. Falls back to <see cref="SuperClass"/> when the
    /// provider reported only the first one.
    /// </summary>
    public IReadOnlyList<string> AllBaseClasses =>
        BaseClasses is { Count: > 0 } bases ? bases
        : string.IsNullOrWhiteSpace(SuperClass) ? [] : [SuperClass];
}

/// <summary>A single parameter (pin) of a function.</summary>
/// <param name="Name">Parameter name, which is also the pin name.</param>
/// <param name="Type">Declared type; null when the index does not carry it.</param>
public sealed record ScanParameterView(string Name, string? Type);

/// <summary>A function as seen by contract consumers.</summary>
/// <param name="Name">Function name.</param>
/// <param name="IsStatic">Whether it is a static function.</param>
/// <param name="IsPure">Whether it is a pure function (no exec pin).</param>
/// <param name="Parameters">Parameters in pin order.</param>
/// <param name="FilePath">Declaring source file; consumers need it to point at the declaration.</param>
/// <param name="LineNumber">Declaring line number.</param>
/// <param name="ReturnType">Declared return type; null when the index does not carry it, which
/// consumers must treat as a wildcard during type matching rather than as "void".</param>
public sealed record ScanFunctionView(
    string Name,
    bool IsStatic,
    bool IsPure,
    IReadOnlyList<ScanParameterView> Parameters,
    string FilePath,
    int LineNumber,
    string? ReturnType = null);

/// <summary>One declaration site of a function name.</summary>
/// <param name="Class">Class that declares it.</param>
/// <param name="Function">The declared function.</param>
public sealed record ScanFunctionDeclarationView(ScanClassView Class, ScanFunctionView Function);

/// <summary>A scanned root, reduced to what consumers can observe.</summary>
/// <param name="RootPath">Root path — one root is one scan scope.</param>
/// <param name="Version">Engine version string; a data attribute only, never an identity.</param>
/// <param name="ModuleNames">Module names directly under this root.</param>
/// <param name="PluginNames">Plugin names under this root.</param>
public sealed record ScanRootView(
    string RootPath,
    string Version,
    IReadOnlyList<string> ModuleNames,
    IReadOnlyList<string> PluginNames);