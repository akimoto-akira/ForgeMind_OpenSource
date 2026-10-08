// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// UE document engine abstraction: holds scan inputs (all paths <b>injected</b>, no hard-coding),
/// and fixes UE's three-stage scan pipeline — modules → headers — assets — via the Template Method pattern.
/// <para>
/// Four independent scans: <b>engine code</b> / <b>engine assets</b> / <b>project code</b> / <b>project assets</b>;
/// each independently produces a <see cref="ScanRoot"/> that is admitted to the index on arrival — the translator can query signatures before asset scanning finishes.
/// </para>
/// <para>
/// Version subclass (Ue5_x) rule (test-driven, not version-number-guessed):
/// scanner N passes on N's source and also passes on N+1 → shared;
/// N+1 fails → open a dedicated scanner subclass for N+1.
/// <see cref="EngineVersion"/> is a data attribute only, never used as identity.
/// </para>
/// </summary>
public abstract class UeDocumentEngine : DocumentEngine
{
    /// <summary>UE source code path (GitHub source or local source) — injected by engine tool locate result.</summary>
    public DirectoryInfo SourceCodeDir { get; }

    /// <summary>UE installed path (Engine install directory) — injected by engine tool locate result.</summary>
    public DirectoryInfo InstallDir { get; }

    /// <summary>Engine version description (e.g. "5.0.3") — data attribute only, never used as identity.</summary>
    public string EngineVersion { get; }

    /// <summary>Project path (optional) — determined by editor startup event; when empty, only engine side is scanned.</summary>
    public DirectoryInfo? ProjectDir { get; init; }

    /// <summary>Scan diagnostics (rules compile / asset parse failures, etc.) — never halts scan; feeds test-driven stub gap-filling.</summary>
    public List<string> ScanDiagnostics { get; } = [];

    protected UeDocumentEngine(DirectoryInfo sourceCodeDir, DirectoryInfo installDir, string engineVersion)
    {
        SourceCodeDir = sourceCodeDir;
        InstallDir = installDir;
        EngineVersion = engineVersion;
    }

    /// <summary>
    /// Template method: engine side and project side are isomorphic (same model and scan logic, only differ in root).
    /// When project path is not injected, only the engine-side root is produced.
    /// </summary>
    public sealed override IReadOnlyList<ScanRoot> Scan()
    {
        var roots = new List<ScanRoot>();
        roots.AddRange(ScanEngineCode());
        roots.AddRange(ScanEngineAssets());
        roots.AddRange(ScanProjectCode());
        roots.AddRange(ScanProjectAssets());
        return roots;
    }

    // ── Four independent scans (each produces a ScanRoot, admitted on arrival) ──

    /// <summary>
    /// Engine code scan: plugin discovery + module rules compilation + header scanning.
    /// No assets — code scan completion is immediately dispatchable; translator can query parent-chain signatures at once.
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanEngineCode();

    /// <summary>
    /// Engine asset scan: walks .uasset under the engine install directory.
    /// Independent of code scan, dispatched later; long-running but does not block signature queries.
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanEngineAssets();

    /// <summary>
    /// Project code scan: project-direct modules + project-plugin modules rules compilation + header scanning.
    /// Independent of engine side, dispatched in parallel.
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanProjectCode();

    /// <summary>
    /// Project asset scan: walks .uasset under the project directory.
    /// Independent of code scan, dispatched last.
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanProjectAssets();
}
