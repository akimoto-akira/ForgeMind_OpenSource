// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeMindServer.Settings;

/// <summary>
/// Target for translating assignment operators (+=, -=, *=, /=, etc.) in DSL.
/// </summary>
public enum AssignmentOperatorTranslation
{
    /// <summary>Compose the operation from multiple pure blueprint nodes (read → compute → assign).</summary>
    PureNodes = 0,

    /// <summary>Use a single Blueprint Macro node (UK2Node_MacroInstance).</summary>
    Macro = 1,

    /// <summary>
    /// Use the engine's built-in Math nodes. Math nodes are essentially Unreal Engine sub-blueprints
    /// that can convert a whole block of math directly into a sub-blueprint — more flexible than macros.
    /// </summary>
    MathNodes = 2,
}

/// <summary>
/// Persistence model for MCP server settings, serialized to
/// <c>%USERPROFILE%\.forgemind\settings.json</c>.
/// </summary>
public sealed class McpSettings
{
    /// <summary>
    /// What assignment operators (+=, -=, *=, /=, etc.) translate to. Defaults to Macro nodes.
    /// Serialized as the enum-name string (PureNodes / Macro / MathNodes).
    /// </summary>
    [JsonPropertyName("convertAssignmentOperatorsToBlueprintMacros")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AssignmentOperatorTranslation ConvertAssignmentOperatorsToBlueprintMacros { get; set; } =
        AssignmentOperatorTranslation.Macro;

    /// <summary>Milliseconds to synchronously wait for an engine response; exceeding it qualifies as a long task (returns executing + task_id).</summary>
    [JsonPropertyName("commandTimeoutMs")]
    public int CommandTimeoutMs { get; set; } = 30_000;

    /// <summary>Task-slot time-to-live (ms), measured from the last data update time.</summary>
    [JsonPropertyName("taskResultTtlMs")]
    public int TaskResultTtlMs { get; set; } = 3_600_000;

    /// <summary>Maximum number of concurrent long tasks; further new long tasks are refused past this limit.</summary>
    [JsonPropertyName("maxConcurrentLongTasks")]
    public int MaxConcurrentLongTasks { get; set; } = 100;

    /// <summary>Engine PID background-check interval (ms), used by Watchdog to poll process liveness.</summary>
    [JsonPropertyName("enginePidCheckIntervalMs")]
    public int EnginePidCheckIntervalMs { get; set; } = 120_000;
}

/// <summary>
/// MCP settings manager. Handles loading / saving the settings file and reads / writes individual values by key.
/// Falls back to defaults when the file is missing or corrupt; changes persist immediately.
/// </summary>
public sealed class McpSettingsManager
{
    /// <summary>Setting key: translation target for assignment operators.</summary>
    public const string ConvertAssignmentOperatorsKey = "convertAssignmentOperatorsToBlueprintMacros";

    /// <summary>Setting key: synchronous wait timeout for engine response (ms).</summary>
    public const string CommandTimeoutMsKey = "commandTimeoutMs";

    /// <summary>Setting key: task-slot time-to-live (ms).</summary>
    public const string TaskResultTtlMsKey = "taskResultTtlMs";

    /// <summary>Setting key: maximum number of concurrent long tasks.</summary>
    public const string MaxConcurrentLongTasksKey = "maxConcurrentLongTasks";

    /// <summary>Setting key: engine PID background-check interval (ms).</summary>
    public const string EnginePidCheckIntervalMsKey = "enginePidCheckIntervalMs";

    /// <summary>Settings file path: {UserProfile}/.forgemind/settings.json</summary>
    public static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".forgemind", "settings.json");

    /// <summary>All registered setting keys.</summary>
    public static readonly string[] Keys =
    [
        ConvertAssignmentOperatorsKey,
        CommandTimeoutMsKey,
        TaskResultTtlMsKey,
        MaxConcurrentLongTasksKey,
        EnginePidCheckIntervalMsKey
    ];

    private static readonly McpSettingsManager _shared = new();

    /// <summary>
    /// Process-wide shared instance. Used at host startup; subsystems that cannot obtain a dependency-injected
    /// instance (e.g. DSL domain translators) read the current settings through it.
    /// </summary>
    public static McpSettingsManager Shared => _shared;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _lock = new();

    /// <summary>Currently loaded settings.</summary>
    public McpSettings Current { get; private set; } = new();

    public McpSettingsManager()
    {
        Current = LoadFromFile();
    }

    /// <summary>Reads the current value of a given setting key. Throws <see cref="KeyNotFoundException"/> when the key is missing.</summary>
    public object Get(string key)
    {
        lock (_lock)
        {
            return key switch
            {
                ConvertAssignmentOperatorsKey => Current.ConvertAssignmentOperatorsToBlueprintMacros,
                CommandTimeoutMsKey => Current.CommandTimeoutMs,
                TaskResultTtlMsKey => Current.TaskResultTtlMs,
                MaxConcurrentLongTasksKey => Current.MaxConcurrentLongTasks,
                EnginePidCheckIntervalMsKey => Current.EnginePidCheckIntervalMs,
                _ => throw new KeyNotFoundException($"Unknown setting key: {key}")
            };
        }
    }

    /// <summary>Updates a setting key's value and saves it to the JSON file immediately. Throws when the key is missing or the value type mismatches.</summary>
    public void Set(string key, object value)
    {
        lock (_lock)
        {
            switch (key)
            {
                case ConvertAssignmentOperatorsKey:
                    Current.ConvertAssignmentOperatorsToBlueprintMacros = ConvertToTranslation(value);
                    break;
                case CommandTimeoutMsKey:
                    Current.CommandTimeoutMs = ConvertToInt(value, CommandTimeoutMsKey, 1, 3_600_000);
                    break;
                case TaskResultTtlMsKey:
                    Current.TaskResultTtlMs = ConvertToInt(value, TaskResultTtlMsKey, 1, 86_400_000);
                    break;
                case MaxConcurrentLongTasksKey:
                    Current.MaxConcurrentLongTasks = ConvertToInt(value, MaxConcurrentLongTasksKey, 1, 10_000);
                    break;
                case EnginePidCheckIntervalMsKey:
                    Current.EnginePidCheckIntervalMs = ConvertToInt(value, EnginePidCheckIntervalMsKey, 1_000, 86_400_000);
                    break;
                default:
                    throw new KeyNotFoundException($"Unknown setting key: {key}");
            }

            SaveToFile();
        }
    }

    private static McpSettings LoadFromFile()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new McpSettings();

            McpSettings? settings = JsonSerializer.Deserialize<McpSettings>(File.ReadAllText(SettingsFile));
            return settings ?? new McpSettings();
        }
        catch (Exception)
        {
            // When the settings file is corrupt, fall back to defaults and overwrite on the next save
            return new McpSettings();
        }
    }

    /// <summary>
    /// Converts an incoming value to <see cref="AssignmentOperatorTranslation"/>.
    /// Accepts the enum itself, the enum-name string (case-insensitive), or a numeric value.
    /// </summary>
    private static AssignmentOperatorTranslation ConvertToTranslation(object value)
    {
        if (value is AssignmentOperatorTranslation translation)
            return translation;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => ParseTranslation(element.GetString()),
                JsonValueKind.Number when element.TryGetInt32(out int number) => ParseTranslation(number),
                _ => throw new FormatException(
                    $"Expected an {nameof(AssignmentOperatorTranslation)} value, got {element.ValueKind}")
            };
        }

        if (value is string text)
            return ParseTranslation(text);

        if (value is int or long or short or byte)
            return ParseTranslation(Convert.ToInt32(value));

        throw new FormatException(
            $"Expected an {nameof(AssignmentOperatorTranslation)} value, got {value.GetType().Name}");
    }

    private static AssignmentOperatorTranslation ParseTranslation(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            Enum.TryParse(text, ignoreCase: true, out AssignmentOperatorTranslation parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new FormatException(
            $"Expected one of {string.Join(", ", Enum.GetNames<AssignmentOperatorTranslation>())}, got '{text}'");
    }

    private static AssignmentOperatorTranslation ParseTranslation(int number)
    {
        if (!Enum.IsDefined(typeof(AssignmentOperatorTranslation), number))
        {
            throw new FormatException(
                $"Expected one of {string.Join(", ", Enum.GetNames<AssignmentOperatorTranslation>())} (or its numeric value), got {number}");
        }

        return (AssignmentOperatorTranslation)number;
    }

    /// <summary>
    /// Converts an incoming value to int and validates its bounds. Accepts numbers and numeric strings.
    /// </summary>
    private static int ConvertToInt(object value, string key, int min, int max)
    {
        int parsed = value switch
        {
            int or long or short or byte => Convert.ToInt32(value),
            string s => int.Parse(s, CultureInfo.InvariantCulture),
            JsonElement e when e.ValueKind == JsonValueKind.Number => e.GetInt32(),
            JsonElement e when e.ValueKind == JsonValueKind.String =>
                int.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            _ => throw new FormatException($"Expected an integer value for '{key}', got {value.GetType().Name}")
        };

        if (parsed < min || parsed > max)
            throw new FormatException($"Value for '{key}' must be between {min} and {max}");

        return parsed;
    }

    private void SaveToFile()
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to save settings file: {SettingsFile} ({ex.Message})", ex);
        }
    }
}
