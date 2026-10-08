// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using ForgeMindServer.Engine;

namespace ForgeMind.Knowledge;

/// <summary>
/// Versioned Unreal Engine knowledge base ("textbook") facade.
/// <para>Backend selection depends on the build configuration, resolved next to
/// the plugin assembly: DEBUG builds always use the <c>Knowledge/</c> folder;
/// release builds prefer the read-only <c>ue-knowledge.spk</c> pack and fall
/// back to the <c>Knowledge/</c> folder when the pack is absent or empty.</para>
/// <para>Query scope for a target version is always
/// <c>universal ∪ target</c>; a same-id entry in the target bucket
/// shadows the universal one.</para>
/// </summary>
public sealed class KnowledgeStore : IDisposable
{
    /// <summary>File name of the release-stage read-only knowledge pack.</summary>
    public const string PackFileName = "ue-knowledge.spk";

    /// <summary>Directory name of the development-stage knowledge folder.</summary>
    public const string FolderName = "Knowledge";

    private readonly IKnowledgeSource _source;

    private KnowledgeStore(IKnowledgeSource source) => _source = source;

    /// <summary>Human-readable backend description (for diagnostics).</summary>
    public string SourceDescription => _source.Description;

    /// <summary>
    /// Opens the knowledge store rooted at this assembly's directory
    /// (<see cref="Open(string)"/>). When hosted as a plugin, the Knowledge\
    /// folder (and the release pack) are deployed next to the plugin DLL, not the
    /// host executable, so we resolve the base directory from the assembly that
    /// owns this type rather than <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public static KnowledgeStore? Open() =>
        Open(Path.GetDirectoryName(typeof(KnowledgeStore).Assembly.Location) ?? AppContext.BaseDirectory);

    /// <summary>
    /// Opens the knowledge store found under <paramref name="baseDirectory"/>.
    /// DEBUG builds use the development folder; release builds prefer the
    /// read-only .spk pack (<see cref="OpenPack"/>) and fall back to the folder
    /// when the pack is absent or empty. Returns null when no backend is available.
    /// </summary>
    public static KnowledgeStore? Open(string baseDirectory)
    {
#if DEBUG
        // Development stage — always edit-friendly folder tree, even if a pack exists.
        return OpenFolder(baseDirectory);
#else
        // Release stage — read-only pack wins; folder fallback when the pack is missing.
        return OpenPack(baseDirectory) ?? OpenFolder(baseDirectory);
#endif
    }

    /// <summary>
    /// Opens the release-stage read-only asset pack (<see cref="PackFileName"/>)
    /// under <paramref name="baseDirectory"/>. The pack is read through
    /// <see cref="IndexPackSource"/> (ForgeMind.Speedy). When the pack file is
    /// absent it returns null so the folder backend is used; when present but
    /// unreadable/corrupt it also returns null (folder fallback keeps the tool
    /// functional). The pack is never created here — Open is read-only.
    /// </summary>
    public static KnowledgeStore? OpenPack(string baseDirectory)
    {
        string packPath = Path.Combine(baseDirectory, PackFileName);
        if (!File.Exists(packPath))
            return null;

        try
        {
            return new KnowledgeStore(new IndexPackSource(packPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the development-stage folder tree (<see cref="FolderName"/>) under
    /// <paramref name="baseDirectory"/>. Returns null when the folder does not exist.
    /// </summary>
    public static KnowledgeStore? OpenFolder(string baseDirectory)
    {
        try
        {
            string folderPath = Path.Combine(baseDirectory, FolderName);
            if (Directory.Exists(folderPath))
                return new KnowledgeStore(new FolderKnowledgeSource(folderPath));
        }
        catch
        {
            // Fall through — no knowledge base available
        }

        return null;
    }

    /// <summary>
    /// Lists all version buckets present in the backend.
    /// Bucket tag is the enum value name of <see cref="EngineVersion"/> ("UE_5_4") or the universal bucket "universal".
    /// </summary>
    public IReadOnlyList<string> ListVersions() => _source.ListVersions();

    /// <summary>Lists the entry metadata of one single bucket.</summary>
    public IReadOnlyList<KnowledgeEntry> ListEntries(string bucket) => _source.ListEntries(bucket);

    /// <summary>
    /// Lists entries of the merged scope (universal ∪ target).
    /// A same-id entry in the target bucket shadows the universal one.
    /// </summary>
    public IReadOnlyList<ScopedEntry> ListEntriesInScope(string bucket)
    {
        var result = new List<ScopedEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Target bucket first so it shadows universal entries with the same id
        foreach (KnowledgeEntry entry in _source.ListEntries(bucket))
        {
            if (EngineVersions.IsValidBucketName(entry.Id) && seen.Add(entry.Id))
                result.Add(new ScopedEntry(entry, bucket));
        }

        // Universal bucket is directly matched: searched alongside any version query.
        // When the query target itself is the universal bucket it was already taken above; avoid duplicate here.
        if (!string.Equals(bucket, EngineVersions.UniversalBucket, StringComparison.OrdinalIgnoreCase))
        {
            foreach (KnowledgeEntry entry in _source.ListEntries(EngineVersions.UniversalBucket))
            {
                if (EngineVersions.IsValidBucketName(entry.Id) && seen.Add(entry.Id))
                    result.Add(new ScopedEntry(entry, EngineVersions.UniversalBucket));
            }
        }

        return result;
    }

    /// <summary>
    /// Finds an entry by id within the merged scope (universal ∪ target).
    /// Returns null when the id is absent from the scope.
    /// </summary>
    public ScopedEntry? FindInScope(string bucket, string id)
    {
        foreach (ScopedEntry scoped in ListEntriesInScope(bucket))
        {
            if (string.Equals(scoped.Entry.Id, id, StringComparison.OrdinalIgnoreCase))
                return scoped;
        }

        return null;
    }

    /// <summary>
    /// Finds an entry by id in any bucket — used to report where an
    /// out-of-scope entry lives.
    /// </summary>
    public IReadOnlyList<ScopedEntry> FindEverywhere(string id)
    {
        var result = new List<ScopedEntry>();

        foreach (string bucket in _source.ListVersions())
        {
            foreach (KnowledgeEntry entry in _source.ListEntries(bucket))
            {
                if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                    result.Add(new ScopedEntry(entry, bucket));
            }
        }

        return result;
    }

    /// <summary>Reads the Markdown body of an entry; null when missing.</summary>
    public string? ReadEntryBody(string bucket, string id) => _source.ReadEntryBody(bucket, id);

    /// <summary>
    /// Keyword search over the merged scope. Tokens are matched
    /// case-insensitively against id, keywords, title, question and answer;
    /// results are sorted by descending score.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string bucket, string query)
    {
        // ToCharArray() from a string constant instead of a char array initializer:
        // both [..] and new char[] {..} make Roslyn lower constant value-type arrays
        // through MemoryMarshal.CreateReadOnlySpan / Unsafe, and the host's plugin
        // security scanner rejects those TypeRefs
        string[] tokens = (query ?? "")
            .Split(" \t,,".ToCharArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        if (tokens.Length == 0)
            return [];

        var hits = new List<SearchHit>();

        foreach (ScopedEntry scoped in ListEntriesInScope(bucket))
        {
            int score = 0;
            KnowledgeEntry entry = scoped.Entry;

            foreach (string token in tokens)
            {
                if (entry.Id.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 3;

                if (entry.Keywords.Any(k => k.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    score += 3;

                if (entry.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 2;

                if (entry.Question.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 1;

                if (entry.Answer.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 1;
            }

            if (score > 0)
                hits.Add(new SearchHit(scoped, score));
        }

        return hits.OrderByDescending(h => h.Score).ToArray();
    }

    public void Dispose() => _source.Dispose();

    /// <summary>An entry together with the version bucket it was found in.</summary>
    public readonly record struct ScopedEntry(KnowledgeEntry Entry, string Bucket);

    /// <summary>A scored search result.</summary>
    public readonly record struct SearchHit(ScopedEntry Scoped, int Score);
}
