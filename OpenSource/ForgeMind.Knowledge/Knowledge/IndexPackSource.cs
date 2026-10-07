// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text;
using ForgeMind.Speedy;
using ForgeMindServer.Engine;

namespace ForgeMind.Knowledge;

/// <summary>
/// Release-stage knowledge backend backed by a Speedy <c>.spk</c> pack
/// (<see cref="KnowledgeStore.PackFileName"/>). Mirrors
/// <see cref="FolderKnowledgeSource"/> but reads entries from the pack instead of
/// the folder tree: <c>{version}/index.json</c> plus <c>{version}/entries/{id}.md</c>.
/// Opened read-only; the pack is never created here.
/// </summary>
internal sealed class IndexPackSource : IKnowledgeSource
{
    private readonly SpeedyPack _pack;
    private readonly string _filePath;

    public IndexPackSource(string filePath)
    {
        _filePath = filePath;
        _pack = SpeedyPack.Open(filePath, new SpeedyPackOptions { ReadOnly = true });
    }

    public string Description => "spk:" + _filePath;

    public IReadOnlyList<string> ListVersions()
    {
        var result = new List<string>();
        try
        {
            foreach (string dir in _pack.ListDirectories(""))
            {
                // 桶标记即枚举值名（"UE_5_4"）或通用桶 "universal"
                if (EngineVersions.IsValidBucketName(dir))
                    result.Add(dir);
            }
        }
        catch
        {
            // Corrupt pack — treat as no versions
        }

        return result;
    }

    public IReadOnlyList<KnowledgeEntry> ListEntries(string bucket)
    {
        if (!EngineVersions.IsValidBucketName(bucket))
            return [];

        string jsonPath = bucket + "/index.json";
        byte[]? json = ReadBytes(jsonPath);
        if (json is null)
            return [];

        return KnowledgeJson.ParseIndex(Encoding.UTF8.GetString(json)).Entries;
    }

    public string? ReadEntryBody(string bucket, string id)
    {
        if (!EngineVersions.IsValidBucketName(bucket) || !EngineVersions.IsValidBucketName(id))
            return null;

        string bodyPath = bucket + "/entries/" + id + ".md";
        byte[]? bytes = ReadBytes(bodyPath);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public void Dispose() => _pack.Dispose();

    private byte[]? ReadBytes(string path)
    {
        try
        {
            return _pack.Read(path);
        }
        catch
        {
            return null;
        }
    }
}
