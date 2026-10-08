// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// Documentation engine abstract base class (layered vertex): triggers a scan, produces index data organized by root.
/// <para>
/// Layered hierarchy: DocumentEngine → UeDocumentEngine → Ue5_x.
/// One root path = one scan scope; scan results are held by the caller until process exit (not persisted, not cached).
/// </para>
/// </summary>
public abstract class DocumentEngine
{
    /// <summary>
    /// Triggers a complete scan, producing data for all roots within the scan scope
    /// (engine-side roots + project-side roots; if no project, only engine-side roots).
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> Scan();
}
