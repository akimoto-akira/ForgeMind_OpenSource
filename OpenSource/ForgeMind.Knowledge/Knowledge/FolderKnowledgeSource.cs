// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer.Engine;

namespace ForgeMind.Knowledge;

/// <summary>
/// Shared JSON handling for knowledge index files.
/// </summary>
internal static class KnowledgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Parses an index.json payload; returns an empty index on failure.</summary>
    public static KnowledgeIndex ParseIndex(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new KnowledgeIndex();

        try
        {
            return JsonSerializer.Deserialize<KnowledgeIndex>(json, Options) ?? new KnowledgeIndex();
        }
        catch
        {
            return new KnowledgeIndex();
        }
    }
}

/// <summary>
/// Development-stage knowledge backend: reads the textbook straight from a
/// folder tree — <c>{version}/index.json</c> plus <c>{version}/entries/{id}.md</c>.
/// </summary>
internal sealed class FolderKnowledgeSource : IKnowledgeSource
{
    private readonly string _root;

    public FolderKnowledgeSource(string root) => _root = root;

    public string Description => "folder:" + _root;

    public IReadOnlyList<string> ListVersions()
    {
        try
        {
            return Directory.GetDirectories(_root)
                .Select(dir => Path.GetFileName(dir) ?? "")
                .Where(bucket => EngineVersions.IsValidBucketName(bucket))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<KnowledgeEntry> ListEntries(string bucket)
    {
        if (!EngineVersions.IsValidBucketName(bucket))
            return [];

        string indexFile = Path.Combine(_root, bucket, "index.json");
        if (!File.Exists(indexFile))
            return [];

        try
        {
            string? json = ForgeMindServer.UnrealTextReader.LoadStringAuto(new FileInfo(indexFile));
            return KnowledgeJson.ParseIndex(json).Entries;
        }
        catch
        {
            return [];
        }
    }

    public string? ReadEntryBody(string bucket, string id)
    {
        if (!EngineVersions.IsValidBucketName(bucket) || !EngineVersions.IsValidBucketName(id))
            return null;

        string bodyFile = Path.Combine(_root, bucket, "entries", id + ".md");
        if (!File.Exists(bodyFile))
            return null;

        try
        {
            return ForgeMindServer.UnrealTextReader.LoadStringAuto(new FileInfo(bodyFile));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        // No resources to release
    }
}
