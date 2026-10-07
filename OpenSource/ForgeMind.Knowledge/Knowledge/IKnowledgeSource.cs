// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer.Engine;

namespace ForgeMind.Knowledge;

/// <summary>
/// Knowledge base backend abstraction: a versioned store of knowledge entries
/// addressed by engine-version bucket + entry id. Implementations read either a
/// folder tree (<see cref="FolderKnowledgeSource"/>) or a Speedy pack
/// (<see cref="IndexPackSource"/>).
/// </summary>
public interface IKnowledgeSource : IDisposable
{
    /// <summary>Human-readable backend description (for diagnostics).</summary>
    string Description { get; }

    /// <summary>
    /// Lists all version buckets present in the backend.
    /// 桶标记即 <see cref="EngineVersion"/> 的<b>枚举值名</b>（如 "UE_5_4"）；
    /// 通用桶为 <see cref="EngineVersions.UniversalBucket"/>（"universal"）。
    /// </summary>
    IReadOnlyList<string> ListVersions();

    /// <summary>Lists the entry metadata of one single bucket.</summary>
    IReadOnlyList<KnowledgeEntry> ListEntries(string bucket);

    /// <summary>Reads the Markdown body of an entry; null when missing.</summary>
    string? ReadEntryBody(string bucket, string id);
}
