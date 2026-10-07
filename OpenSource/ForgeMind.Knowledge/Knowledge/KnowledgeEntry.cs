// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Knowledge;

/// <summary>
/// Knowledge domains — the high-level buckets a lesson belongs to.
/// </summary>
public enum KnowledgeCategory
{
    /// <summary>C++ implementation knowledge.</summary>
    Cpp,

    /// <summary>Blueprint / visual scripting knowledge.</summary>
    Blueprint,

    /// <summary>Material editing knowledge.</summary>
    Material,

    /// <summary>Anything that does not fit the above.</summary>
    Other
}

/// <summary>
/// A single knowledge entry (one lesson): id, title, keywords, the Q&amp;A text,
/// and the version bucket it belongs to.
/// </summary>
public sealed class KnowledgeEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public KnowledgeCategory Category { get; set; } = KnowledgeCategory.Other;
    public List<string> Keywords { get; set; } = [];
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public List<string> Related { get; set; } = [];
    public bool RequiresCompanion { get; set; }
    public string Path { get; set; } = "";
}

/// <summary>
/// In-memory index model deserialized from a bucket's index.json.
/// </summary>
internal sealed class KnowledgeIndex
{
    public List<KnowledgeEntry> Entries { get; set; } = [];
}
