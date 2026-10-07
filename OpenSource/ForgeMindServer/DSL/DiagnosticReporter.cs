// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;

namespace ForgeMindServer.DSL;

/// <summary>Diagnostic severity level.</summary>
public enum DiagnosticSeverity
{
    Warning,
    Error
}

/// <summary>A single diagnostic entry.</summary>
/// <param name="Severity">Severity level.</param>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column number.</param>
/// <param name="Code">Diagnostic code (e.g., "FM1001").</param>
/// <param name="Message">Diagnostic text.</param>
public sealed record DiagnosticEntry(
    DiagnosticSeverity Severity,
    int Line,
    int Column,
    string Code,
    string Message);

/// <summary>
/// In-translation diagnostic reporter (MCP-side channel).
/// Diagnostics and IR are separate channels — diagnostics must not enter Bridge payloads (Core Syntax.md §8).
/// Any Error → terminates the whole batch; IR is not sent to the Bridge. Warnings do not interrupt translation.
/// </summary>
public sealed class DiagnosticReporter
{
    private readonly List<DiagnosticEntry> _entries = [];

    /// <summary>All diagnostic entries (in report order).</summary>
    public IReadOnlyList<DiagnosticEntry> Entries => _entries;

    /// <summary>Whether any error-level diagnostic exists.</summary>
    public bool HasErrors => _entries.Any(e => e.Severity == DiagnosticSeverity.Error);

    /// <summary>Whether any diagnostic exists.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Reports an Error (terminates the batch).</summary>
    public void Error(int line, int col, string code, string message) =>
        _entries.Add(new DiagnosticEntry(DiagnosticSeverity.Error, line, col, code, message));

    /// <summary>Reports a Warning (does not interrupt translation).</summary>
    public void Warn(int line, int col, string code, string message) =>
        _entries.Add(new DiagnosticEntry(DiagnosticSeverity.Warning, line, col, code, message));

    /// <summary>Merges all entries from another reporter.</summary>
    public void Merge(DiagnosticReporter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _entries.AddRange(other._entries);
    }

    /// <summary>Serializes to a diagnostic report JSON ({"success":bool,"diagnostics":[...]}).</summary>
    public string ToJson()
    {
        var doc = new
        {
            success = !HasErrors,
            diagnostics = _entries.Select(e => new
            {
                severity = e.Severity == DiagnosticSeverity.Error ? "error" : "warning",
                line = e.Line,
                column = e.Column,
                code = e.Code,
                message = e.Message
            })
        };
        return JsonSerializer.Serialize(doc);
    }
}
