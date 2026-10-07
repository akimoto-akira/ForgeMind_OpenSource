// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Test;

/// <summary>
/// Test case interface. Each implementation class represents an independent test case.
///
/// Test logic (tool calls, assertions, IR persistence) is entirely written in the <see cref="Run"/> method body—
/// no dependency on external JSON / MD instruction files; pure C# code.
///
/// <see cref="TestSuite"/> discovers all <see cref="ITestCase"/> implementations via reflection at startup,
/// groups them by <see cref="Group"/>, sorts by <see cref="Order"/> within each group, and executes them sequentially, fail-fast.
///
/// "Launch editor" and "Close editor" are also <see cref="ITestCase"/>—
/// their execution position within the group is controlled by <see cref="Order"/>
/// (e.g., <c>Order = 0</c> launches first, <c>Order = int.MaxValue</c> closes last).
/// </summary>
public interface ITestCase
{
    /// <summary>
    /// Test case name (used for logging and report display).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Group name. Cases with the same <see cref="Group"/> execute consecutively in the same session—
    /// suitable for a batch of cases that need to share an editor session (launch → run → close).
    /// Empty string or null means "no group" (standalone execution).
    /// </summary>
    string? Group { get; }

    /// <summary>
    /// Execution order within the group (smaller runs first). Ties broken by name.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Execute the test. Internally calls tools, asserts, and persists IR via <paramref name="ctx"/>.
    /// Throwing an exception = failure; normal return = pass.
    /// </summary>
    /// <remarks>Task uses fully-qualified name: the child namespace ForgeMindServer.Test.Task would shadow
    /// the simple name of System.Threading.Tasks.Task.</remarks>
    System.Threading.Tasks.Task Run(TestContext ctx);
}

/// <summary>
/// Must-run marker: applied to test case classes to indicate that the case must always execute under any filtering mode.
/// Used for infrastructure cases within a group such as launch / close editor (Order = 0 / int.MaxValue)—
/// when <c>--Test &lt;group&gt; &lt;index&gt;</c> filters a single case, the matched case plus all
/// must-run cases are executed in Order order (ensuring the editor is launched and closed).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MustRunAttribute : Attribute
{
}

/// <summary>
/// Test context: infrastructure provided to <see cref="ITestCase"/> implementations.
///
/// - Find and execute tools by name
/// - Assertions (AssertTrue / AssertSuccess / AssertField)
/// - IR persistence
/// - Logging
/// </summary>
public sealed class TestContext
{
    private readonly IReadOnlyList<ITool> _tools;
    private readonly Guid _callerId = Guid.NewGuid();
    private readonly string _outputDir;
    private readonly string _groupDir;
    private readonly ILogger _logger;
    private readonly string _bridgeDir;
    private int _bridgeSeq;

    internal TestContext(
        IReadOnlyList<ITool> tools,
        string outputDir,
        string groupDir,
        ILogger logger)
    {
        _tools = tools;
        _outputDir = outputDir;
        _groupDir = groupDir;
        _logger = logger;
        _bridgeDir = Path.Combine(groupDir, "bridge");
        Directory.CreateDirectory(_bridgeDir);
    }

    /// <summary>Output directory for this batch (already created).</summary>
    public string OutputDir => _outputDir;

    /// <summary>Output subdirectory for this group (already created). Shared by cases in the same group.</summary>
    public string GroupDir => _groupDir;

    /// <summary>Logger (test cases can write logs directly).</summary>
    public ILogger Logger => _logger;

    // ── Tool calls ──

    /// <summary>
    /// Find a tool by name. Throws <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    private ITool GetTool(string toolName)
    {
        foreach (ITool t in _tools)
        {
            if (string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }
        throw new InvalidOperationException($"Tool '{toolName}' not found");
    }

    /// <summary>
    /// Execute a tool and return the result. Throws <see cref="TestFailureException"/> on tool execution failure.
    /// </summary>
    public ToolResult CallTool(string toolName, Dictionary<string, object> parameters)
    {
        ITool tool = GetTool(toolName);

        // Save request data (outgoing)
        SaveBridgeData(toolName, parameters, isSend: true);

        _logger.Info(null, "[Test]     → {0}({1})", toolName, FormatParams(parameters));

        ToolResult result = tool.Execute(_callerId, parameters);
        // Save response data (incoming)
        SaveBridgeData(toolName, result, isSend: false);

        if (!result.Success)
        {
            throw new TestFailureException(
                $"Tool '{toolName}' failed: {result.Message}");
        }

        _logger.Info(null, "[Test]     ✓ {0}: {1}", toolName, result.Message);
        return result;
    }

    /// <summary>
    /// Execute a tool without throwing—returns the result even on failure.
    /// Suitable for scenarios that tolerate failure, such as closing the editor.
    /// </summary>
    public ToolResult TryCallTool(string toolName, Dictionary<string, object> parameters)
    {
        ITool tool = GetTool(toolName);

        // Save request data (outgoing)
        SaveBridgeData(toolName, parameters, isSend: true);

        _logger.Info(null, "[Test]     → {0}({1})", toolName, FormatParams(parameters));

        ToolResult result = tool.Execute(_callerId, parameters);

        // Save response data (incoming)
        SaveBridgeData(toolName, result, isSend: false);

        if (!result.Success)
        {
            _logger.Warn(null, "[Test]     ✗ {0}: {1}", toolName, result.Message);
        }
        else
        {
            _logger.Info(null, "[Test]     ✓ {0}: {1}", toolName, result.Message);
        }

        return result;
    }

    // ── Assertions ──

    /// <summary>
    /// Assert that a condition is true, otherwise throw <see cref="TestFailureException"/>.
    /// </summary>
    public void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestFailureException($"Assertion failed: {message}");
        }
    }

    /// <summary>
    /// Assert that ToolResult.Success is true.
    /// </summary>
    public void AssertSuccess(ToolResult result, string context = "")
    {
        if (!result.Success)
        {
            throw new TestFailureException(
                $"Expected success but got failure{(
                    string.IsNullOrEmpty(context) ? "" : $" ({context})")}: {result.Message}");
        }
    }

    /// <summary>
    /// Assert that a field at the specified path in the result JSON equals the expected value.
    /// </summary>
    public void AssertField(ToolResult result, string fieldPath, bool expected)
    {
        string json = SerializeResult(result);
        JsonNode? root = JsonNode.Parse(json);
        JsonNode? value = NavigatePath(root, fieldPath);

        if (value == null)
        {
            throw new TestFailureException(
                $"Field '{fieldPath}' not found in result");
        }

        if (value is JsonValue jv && jv.TryGetValue<bool>(out bool actual))
        {
            if (actual != expected)
            {
                throw new TestFailureException(
                    $"Field '{fieldPath}': expected {expected}, got {actual}");
            }
        }
        else
        {
            throw new TestFailureException(
                $"Field '{fieldPath}' is not a boolean: {value.ToJsonString()}");
        }
    }

    // ── IR persistence ──

    /// <summary>
    /// Write IR from the tool response data to a file.
    /// </summary>
    /// <param name="result">Tool return result</param>
    /// <param name="dataField">Field path of IR data in the result (e.g., "data")</param>
    /// <param name="filePath">Output file path (absolute)</param>
    public void SaveIr(ToolResult result, string dataField, string filePath)
    {
        string json = SerializeResult(result);
        JsonNode? root = JsonNode.Parse(json);
        JsonNode? irNode = NavigatePath(root, dataField);

        string irJson;
        if (irNode != null)
        {
            irJson = irNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        else
        {
            // Fallback: serialize the entire Data
            irJson = JsonSerializer.Serialize(result.Data,
                new JsonSerializerOptions { WriteIndented = true });
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(filePath, irJson);
        _logger.Info(null, "[Test]     IR saved: {0} ({1} bytes)", filePath, irJson.Length);
    }

    // ── Helpers ──

    private static string SerializeResult(ToolResult result)
    {
        var obj = new JsonObject
        {
            ["success"] = result.Success,
            ["message"] = result.Message,
        };

        if (result.Data != null)
        {
            string dataJson = JsonSerializer.Serialize(result.Data, TestJsonOptions);
            try
            {
                obj["data"] = JsonNode.Parse(dataJson);
            }
            catch
            {
                obj["data"] = JsonValue.Create(dataJson);
            }
        }

        return obj.ToJsonString();
    }

    private static JsonNode? NavigatePath(JsonNode? node, string path)
    {
        if (node == null || string.IsNullOrWhiteSpace(path))
        {
            return node;
        }

        foreach (string segment in path.Split('.'))
        {
            if (node is JsonObject obj && obj.TryGetPropertyValue(segment, out JsonNode? value))
            {
                node = value;
            }
            else
            {
                return null;
            }
        }

        return node;
    }

    // ── Bridge round-trip data persistence ──

    /// <summary>
    /// Save tool request/response data to the bridge directory, filenames precise to the millisecond.
    /// Send (before call) uses _send suffix; receive (after response) uses _recv suffix.
    /// </summary>
    private void SaveBridgeData(string toolName, object data, bool isSend)
    {
        try
        {
            int seq = System.Threading.Interlocked.Increment(ref _bridgeSeq);
            string timestamp = DateTime.Now.ToString("HHmmss_fff");
            string suffix = isSend ? "send" : "recv";
            string fileName = $"{seq:D4}_{timestamp}_{toolName}_{suffix}.json";
            string filePath = Path.Combine(_bridgeDir, fileName);

            string json;
            if (data is ToolResult result)
            {
                json = SerializeResult(result);
            }
            else if (data is Dictionary<string, object> dict)
            {
                json = JsonSerializer.Serialize(dict, TestJsonOptions);
            }
            else
            {
                json = JsonSerializer.Serialize(data, TestJsonOptions);
            }

            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Persistence failure should not affect test execution
        }
    }

    private static string FormatParams(Dictionary<string, object> parameters)
    {
        if (parameters.Count == 0) return "{}";

        // Compact format: only show action and path
        var parts = new List<string>();
        foreach (var kvp in parameters.Take(3))
        {
            string val = kvp.Value?.ToString() ?? "null";
            if (val.Length > 60) val = val[..60] + "...";
            parts.Add($"{kvp.Key}={val}");
        }
        return $"{{{string.Join(", ", parts)}{(parameters.Count > 3 ? ", ..." : "")}}}";
    }

    private static readonly JsonSerializerOptions TestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>
/// Test failure exception. Throwing this represents a failed test case.
/// </summary>
public sealed class TestFailureException : Exception
{
    public TestFailureException(string message) : base(message) { }
    public TestFailureException(string message, Exception inner) : base(message, inner) { }
}
