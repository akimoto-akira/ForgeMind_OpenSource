// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using System.Text.RegularExpressions;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Test.Demo;

/// <summary>
/// The demo script's vocabulary, and the single place that knows how to find a tool and
/// run it. A script stays pure scenario text: <see cref="Do"/> names a tool and its
/// parameters, <see cref="Wait"/> asks for a delay; neither exposes the tool table.
/// </summary>
public static class DemoStep
{
    private static readonly Guid CallerId = Guid.NewGuid();

    /// <summary>
    /// One real action: the turn shows <paramref name="zh"/> / <paramref name="en"/> and
    /// invokes <paramref name="toolName"/>. <paramref name="optional"/> marks a turn that may
    /// legitimately find nothing to do (see <see cref="DemoTurn.Optional"/>).
    /// <paramref name="result"/> names the answer fields the turn exists to show — the runner prints
    /// those and nothing else (see <see cref="DemoTurn.ResultKeys"/>).
    /// </summary>
    public static DemoTurn Do(string zh, string en, string toolName,
        IReadOnlyDictionary<string, object>? parameters = null, bool optional = false,
        string[]? result = null)
    {
        return new DemoTurn(zh, en, toolName, parameters, null, optional, result);
    }

    /// <summary>
    /// The clearing turn every script starts with: a re-recording rebuilds the same asset, so the
    /// previous one has to go first. Nothing to clear is the normal case on a first run.
    /// </summary>
    public static DemoTurn Clear(string zh, string en, string assetPath)
        => Do(zh, en, "editor", Editor("delete_asset", new Dictionary<string, object>
        {
            ["path"] = assetPath,
        }), optional: true);

    /// <summary>
    /// The built-in wait instruction — the only turn without a tool. It exists to show that
    /// processing takes time (engine startup, for example); the run just counts
    /// <paramref name="seconds"/> down while it waits.
    /// </summary>
    public static DemoTurn Wait(string zh, string en, int seconds)
    {
        return new DemoTurn(zh, en, null, null, seconds);
    }

    /// <summary>
    /// A step may refer to an id an earlier step produced: <c>${n.id}</c>, n being the 1-based
    /// turn number. A step-by-step demo builds one asset out of many commands, and the next
    /// command needs the node id the previous answer carried — that is the whole point of
    /// showing the commands separately instead of sending one DSL block.
    /// <para><c>${n.node.Class}</c> reaches into a graph listing: <see cref="Entry"/> and
    /// <see cref="Result"/> use it to name the terminals a function graph starts with.</para>
    /// </summary>
    private static readonly Regex StepReference = new(@"\$\{(\d+)\.([\w.]+)\}", RegexOptions.Compiled);

    /// <summary>What step <paramref name="step"/> answered: a node id or an expression id.</summary>
    public static string Ref(int step) => $"${{{step}.id}}";

    /// <summary>Entry node of the graph that step <paramref name="step"/> listed.</summary>
    public static string Entry(int step) => $"${{{step}.node.K2Node_FunctionEntry}}";

    /// <summary>Result (return) node of the graph that step <paramref name="step"/> listed.</summary>
    public static string Result(int step) => $"${{{step}.node.K2Node_FunctionResult}}";

    /// <summary>
    /// A companion command: <paramref name="action"/> carrying <paramref name="payload"/>, aimed
    /// at the running project. Every editor turn goes through here so a script never repeats the
    /// action / project-path wrapper.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Editor(string action,
        IReadOnlyDictionary<string, object> payload)
        => new Dictionary<string, object>
        {
            ["action"] = action,
            ["path"] = BlueprintTestHelper.ProjectPath,
            ["payload"] = payload,
        };

    /// <summary>
    /// Runs one tool with no script turn behind it. The engine session the window opens around a
    /// script is infrastructure, not a step: it has no caption, no step reference, and no row of
    /// its own beyond the lifecycle row the window draws for it.
    /// </summary>
    internal static ToolResult Call(IReadOnlyList<ITool> tools, string toolName,
        IReadOnlyDictionary<string, object> parameters)
        => Execute(new DemoTurn(string.Empty, string.Empty, toolName, parameters, null),
            tools, Array.Empty<ToolResult>());

    /// <summary>
    /// Finds the tool by name (case-insensitive) and executes it — the same
    /// <see cref="ITool.Execute"/> path the test cases use, without any of their logging.
    /// A missing tool throws: that is a system problem, not a demo outcome.
    /// <para><paramref name="previous"/> is what the earlier turns answered, so a reference
    /// like <c>${2.id}</c> can be filled in before the call goes out.</para>
    /// </summary>
    internal static ToolResult Execute(DemoTurn turn, IReadOnlyList<ITool> tools,
        IReadOnlyList<ToolResult> previous)
    {
        if (turn.ToolName is null)
        {
            throw new InvalidOperationException("Wait instructions have no tool to execute");
        }

        ITool? tool = null;
        foreach (ITool candidate in tools)
        {
            if (string.Equals(candidate.Name, turn.ToolName, StringComparison.OrdinalIgnoreCase))
            {
                tool = candidate;
                break;
            }
        }

        if (tool is null)
        {
            throw new InvalidOperationException($"Tool '{turn.ToolName}' not found");
        }

        var parameters = new Dictionary<string, object>();
        if (turn.Parameters is not null)
        {
            foreach (KeyValuePair<string, object> pair in turn.Parameters)
            {
                parameters[pair.Key] = Resolve(pair.Value, previous);
            }
        }

        return tool.Execute(CallerId, parameters);
    }

    /// <summary>
    /// Copies a parameter value, filling in step references on the way down. Nested payload
    /// dictionaries (the 'payload' the editor tool forwards) are copied too, so a reference
    /// inside one is resolved exactly like a top-level one.
    /// </summary>
    private static object Resolve(object value, IReadOnlyList<ToolResult> previous)
    {
        if (value is string text)
        {
            return StepReference.Replace(text, match => Referenced(match, previous));
        }

        if (value is IReadOnlyDictionary<string, object> source)
        {
            var copy = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> pair in source)
            {
                copy[pair.Key] = Resolve(pair.Value, previous);
            }
            return copy;
        }

        return value;
    }

    /// <summary>
    /// Answers a <c>${n.…}</c> reference: <c>id</c> is the node / expression id the n-th turn got
    /// back (<c>nodeId</c> for graph nodes, <c>expressionId</c> for material expressions — the same
    /// pair the test helpers read), <c>node.&lt;Class&gt;</c> a node of that class in the graph the
    /// n-th turn listed. An unusable reference is left verbatim so the failure shows up as itself
    /// instead of silently becoming an empty id.
    /// </summary>
    private static string Referenced(Match match, IReadOnlyList<ToolResult> previous)
    {
        int index = int.Parse(match.Groups[1].Value);
        if (index < 1 || index > previous.Count)
        {
            return match.Value;
        }

        object? data = previous[index - 1].Data;
        JsonElement root = data is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(data);

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("response", out JsonElement response)
            || response.ValueKind != JsonValueKind.Object)
        {
            return match.Value;
        }

        string what = match.Groups[2].Value;
        if (what.StartsWith("node.", StringComparison.Ordinal))
        {
            return GraphNode(response, what[5..]) ?? match.Value;
        }

        foreach (string field in new[] { "nodeId", "expressionId" })
        {
            if (response.TryGetProperty(field, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }
        }

        return match.Value;
    }

    /// <summary>
    /// Id of the first node of <paramref name="nodeClass"/> in a <c>list_blueprint_graph</c> answer.
    /// The class names are the engine's own (<c>K2Node_FunctionEntry</c> / <c>K2Node_FunctionResult</c>),
    /// which is what the listing reports.
    /// </summary>
    private static string? GraphNode(JsonElement response, string nodeClass)
    {
        if (!response.TryGetProperty("graphs", out JsonElement graphs)
            || graphs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement graph in graphs.EnumerateArray())
        {
            if (graph.ValueKind != JsonValueKind.Object
                || !graph.TryGetProperty("nodes", out JsonElement nodes)
                || nodes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object
                    || !node.TryGetProperty("class", out JsonElement cls)
                    || cls.ValueKind != JsonValueKind.String
                    || !string.Equals(cls.GetString(), nodeClass, StringComparison.Ordinal))
                {
                    continue;
                }

                return node.TryGetProperty("id", out JsonElement id)
                    && id.ValueKind == JsonValueKind.String
                        ? id.GetString()
                        : null;
            }
        }

        return null;
    }
}
