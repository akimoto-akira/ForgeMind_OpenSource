// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.Knowledge;

/// <summary>
/// Knowledge base ("textbook") tool (see spec §2.4.2). Tool name is
/// engine-agnostic ("knowledge").
/// <para>Single entry point, not split per engine and not registered per engine: version differences are unified
/// inside <see cref="Execute"/> via a switch on <see cref="EngineVersion"/>. Currently all versions
/// share identical logic, all branches fall through to <c>RunUnrealKnowledge</c>; when a version diverges in the future,
/// modify the corresponding case — no need for engine-specific files or extra registration.</para>
/// <para>Version bucket tag is the <b>enum value name</b> of <see cref="EngineVersion"/> (e.g. "UE_5_4");
/// the "universal" bucket is not an enum value, it <b>directly matches</b> — it is searched alongside any version query
/// (see <see cref="KnowledgeStore.ListEntriesInScope"/>).</para>
/// <para>Backed by <see cref="KnowledgeStore"/>: a read-only .spk pack in
/// release stage, a folder tree in development stage.</para>
/// </summary>
public sealed class KnowledgeTool : ITool
{
    /// <summary>Maximum number of search results returned to the caller.</summary>
    private const int MaxSearchResults = 10;

    private readonly ServerPermissionChecker _permChecker;
    private readonly string? _defaultEngineVersion;

    internal KnowledgeTool(EngineDescriptor? ue, ProjectInfo? project, ServerPermissionChecker permChecker)
    {
        _permChecker = permChecker;
        _defaultEngineVersion = !string.IsNullOrWhiteSpace(project?.EngineVersion)
            ? project.EngineVersion
            : ue?.Version?.ToString();
    }

    /// <summary>Engine-agnostic tool name.</summary>
    public string Name => "knowledge";

    public string Description =>
        "Knowledge base: curated answers and implementation solutions, organized by engine version buckets. " +
        "Actions: 'list' (browse entries, optional 'category' filter), " +
        "'search' (keyword search, requires 'query'), " +
        "'get' (fetch the full lesson, requires 'id'). " +
        "Version scope comes from 'engineVersion' — an EngineVersion enum member name " +
        "such as 'UE_5_4' (the universal bucket is always searched as well); " +
        "otherwise the host's current project engine version is used, then universal.";

    public string GetDisplayName() => "Knowledge Base";

    public Dictionary<string, object> GetParameterSchema()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["action"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The action to perform: list, search, get",
                    ["enum"] = new[] { "list", "search", "get" }
                },
                ["category"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "List only: filter by category",
                    ["enum"] = new[] { "Cpp", "Blueprint", "Material", "Other" }
                },
                ["query"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Search only: keywords to match against titles, keywords, questions and answers"
                },
                ["id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Get only: the entry id to fetch"
                },
                ["engineVersion"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Engine version bucket: an EngineVersion enum member name such as " +
                                      "'UE_5_4' (or 'universal'). Version numbers like '5.4' are NOT accepted. " +
                                      "Defaults to the host's current project engine version, then universal."
                }
            },
            ["required"] = new[] { "action" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        string? permError = _permChecker.RequirePermission(Name);
        if (permError != null)
            return ToolResult.Failed(permError);

        if (!parameters.TryGetValue("action", out object? actionObj))
            return ToolResult.Failed("Missing 'action' parameter");

        string action = actionObj?.ToString()?.ToLowerInvariant() ?? "";

        using KnowledgeStore? store = KnowledgeStore.Open();
        if (store == null)
        {
            return ToolResult.Failed(
                $"Knowledge base not available - no {KnowledgeStore.PackFileName} or {KnowledgeStore.FolderName}/ found next to the plugin assembly");
        }

        (string bucket, string versionSource, List<string> notes) = ResolveTargetVersion(parameters);

        // Route by engine version: one branch per supported version. Bucket tag is the enum value name ("UE_5_4"),
        // currently all versions share logic -> unified RunUnrealKnowledge; when a version diverges,
        // modify the corresponding case (no extra engine-specific file or registration needed).
        // "universal" is the universal bucket, not a specific version, falls through to the default branch.
        EngineVersion? engineVersion = EngineVersions.Parse(bucket);
        return engineVersion switch
        {
            EngineVersion.UE_5_0 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_1 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_2 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_3 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_4 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_5 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_6 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_7 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            EngineVersion.UE_5_8 =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters),
            _ =>
                RunUnrealKnowledge(action, store, bucket, versionSource, notes, parameters)
        };
    }

    /// <summary>
    /// <summary>Shared knowledge base implementation for all current versions (list / search / get).</summary>
        // When a version diverges in the future, modify the corresponding case in <see cref="Execute"/> to take a different branch.
    /// </summary>
    private static ToolResult RunUnrealKnowledge(
        string action, KnowledgeStore store, string bucket, string versionSource,
        List<string> notes, Dictionary<string, object> parameters)
    {
        return action switch
        {
            "list" => ExecuteList(store, bucket, versionSource, notes, parameters),
            "search" => ExecuteSearch(store, bucket, versionSource, notes, parameters),
            "get" => ExecuteGet(store, bucket, versionSource, notes, parameters),
            _ => ToolResult.Failed($"Unknown action: {action}")
        };
    }

    // ===== action: list =====

    private static ToolResult ExecuteList(
        KnowledgeStore store, string bucket, string versionSource,
        List<string> notes, Dictionary<string, object> parameters)
    {
        string? categoryText = parameters.TryGetValue("category", out object? categoryObj)
            ? categoryObj?.ToString()?.ToLowerInvariant()
            : null;

        IEnumerable<KnowledgeStore.ScopedEntry> entries = store.ListEntriesInScope(bucket);
        if (!string.IsNullOrEmpty(categoryText) &&
            Enum.TryParse<KnowledgeCategory>(categoryText, ignoreCase: true, out KnowledgeCategory category))
        {
            entries = entries.Where(e => e.Entry.Category == category);
        }

        var list = entries
            .Select(scoped => new
            {
                id = scoped.Entry.Id,
                category = scoped.Entry.Category,
                title = scoped.Entry.Title,
                question = scoped.Entry.Question,
                version = scoped.Bucket,
                requiresCompanion = scoped.Entry.RequiresCompanion
            })
            .ToArray();

        return ToolResult.Successful(
            $"Listed {list.Length} knowledge entries (scope: {ScopeName(bucket)})",
            new
            {
                knowledgeSource = store.SourceDescription,
                availableVersions = store.ListVersions().ToArray(),
                engineVersion = bucket,
                versionSource,
                notes,
                entries = list
            });
    }

    // ===== action: search =====

    private static ToolResult ExecuteSearch(
        KnowledgeStore store, string bucket, string versionSource,
        List<string> notes, Dictionary<string, object> parameters)
    {
        string? query = parameters.TryGetValue("query", out object? queryObj)
            ? queryObj?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Failed("Missing 'query' parameter");

        var results = store.Search(bucket, query)
            .Take(MaxSearchResults)
            .Select(hit => new
            {
                id = hit.Scoped.Entry.Id,
                category = hit.Scoped.Entry.Category,
                title = hit.Scoped.Entry.Title,
                answer = hit.Scoped.Entry.Answer,
                version = hit.Scoped.Bucket,
                requiresCompanion = hit.Scoped.Entry.RequiresCompanion,
                score = hit.Score
            })
            .ToArray();

        return ToolResult.Successful(
            $"Found {results.Length} matching entries for '{query}' (scope: {ScopeName(bucket)})",
            new
            {
                knowledgeSource = store.SourceDescription,
                engineVersion = bucket,
                versionSource,
                notes,
                query,
                results
            });
    }

    // ===== action: get =====

    private static ToolResult ExecuteGet(
        KnowledgeStore store, string bucket, string versionSource,
        List<string> notes, Dictionary<string, object> parameters)
    {
        string? id = parameters.TryGetValue("id", out object? idObj)
            ? idObj?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(id))
            return ToolResult.Failed("Missing 'id' parameter");

        KnowledgeStore.ScopedEntry? scoped = store.FindInScope(bucket, id);
        if (scoped == null)
        {
            // Report where the entry lives when it exists outside the current scope
            IReadOnlyList<KnowledgeStore.ScopedEntry> elsewhere = store.FindEverywhere(id);
            if (elsewhere.Count > 0)
            {
                return ToolResult.Successful(
                    $"Entry '{id}' is not available for scope {ScopeName(bucket)} - it belongs to: " +
                    string.Join(", ", elsewhere.Select(e => e.Bucket)),
                    new
                    {
                        foundInScope = false,
                        id,
                        engineVersion = bucket,
                        versionSource,
                        notes,
                        availableIn = elsewhere.Select(e => e.Bucket).ToArray()
                    });
            }

            return ToolResult.Failed($"Unknown knowledge entry id: {id}");
        }

        KnowledgeEntry entry = scoped.Value.Entry;
        string? body = store.ReadEntryBody(scoped.Value.Bucket, entry.Id);
        if (body == null)
            notes.Add($"Entry body file missing for '{entry.Id}'");

        if (entry.RequiresCompanion)
            notes.Add("This entry requires the ForgeMindServer companion plugin — verify via unreal_project analyze (companionPlugin)");

        return ToolResult.Successful(
            $"Knowledge entry '{entry.Id}' ({entry.Category}, version {scoped.Value.Bucket})",
            new
            {
                foundInScope = true,
                id = entry.Id,
                category = entry.Category,
                title = entry.Title,
                question = entry.Question,
                keywords = entry.Keywords,
                answer = entry.Answer,
                version = scoped.Value.Bucket,
                requiresCompanion = entry.RequiresCompanion,
                related = entry.Related,
                body,
                engineVersion = bucket,
                versionSource,
                notes
            });
    }

    // ===== Version resolution =====

    /// <summary>
    /// Resolves the target version bucket.
    /// <para>An explicit <c>engineVersion</c> parameter wins and is accepted
    /// <b>only as an EngineVersion enum member name</b> ("UE_5_4"); a version number
    /// like "5.4" is invalid and falls back to universal with a note.</para>
    /// <para>Otherwise the host-computed default is used — that value comes from the
    /// project's EngineAssociation, so it goes through
    /// <see cref="EngineVersions.FromAssociation"/> to map "5.4" onto the enum, and
    /// the resulting <b>enum member name</b> is used as the bucket marker.</para>
    /// </summary>
    private (string Bucket, string Source, List<string> Notes) ResolveTargetVersion(
        Dictionary<string, object> parameters)
    {
        var notes = new List<string>();

        if (parameters.TryGetValue("engineVersion", out object? versionObj) &&
            !string.IsNullOrWhiteSpace(versionObj?.ToString()))
        {
            string raw = versionObj!.ToString()!;
            EngineVersion? parsed = EngineVersions.Parse(raw);

            if (parsed == null)
            {
                notes.Add($"'{raw}' is not a valid engine version - expected an EngineVersion " +
                          "enum member name such as 'UE_5_4' (or 'universal'); falling back to universal");
                return (EngineVersions.UniversalBucket, "parameter(invalid)", notes);
            }

            return (EngineVersions.ToBucket(parsed.Value), "parameter", notes);
        }

        if (!string.IsNullOrWhiteSpace(_defaultEngineVersion))
        {
            EngineVersion? parsed = EngineVersions.FromAssociation(_defaultEngineVersion);
            return parsed == null
                ? (EngineVersions.UniversalBucket, "engine registry", notes)
                : (EngineVersions.ToBucket(parsed.Value), "engine registry", notes);
        }

        return (EngineVersions.UniversalBucket, "default", notes);
    }

    /// <summary>Human-readable scope name: universal alone, or universal plus the bucket.</summary>
    private static string ScopeName(string bucket) =>
        string.Equals(bucket, EngineVersions.UniversalBucket, StringComparison.OrdinalIgnoreCase)
            ? "universal"
            : "universal + " + bucket;
}
