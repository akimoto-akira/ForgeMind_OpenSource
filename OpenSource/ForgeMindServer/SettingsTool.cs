// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using System.Text.Json.Serialization;
using ForgeMindServer.Compat;
using ForgeMindServer.Settings;

namespace ForgeMindServer;

/// <summary>
/// Settings read/write tool (requirements.md §2.4.1). Engine-independent, always available. Dispatches by
/// the <c>action</c> argument: <c>get</c> reads a single setting, <c>set</c> writes and immediately
/// persists to <c>%USERPROFILE%\.forgemind\settings.json</c>.
/// </summary>
public class SettingsTool : ITool
{
    private readonly McpSettingsManager _manager;

    public SettingsTool(McpSettingsManager manager)
    {
        _manager = manager;
    }

    public string Name => "settings";

    public string Description =>
        "Read or write a single ForgeMind global setting, or list them all. " +
        "action = 'get' reads the value of 'key'; action = 'set' writes 'value' to 'key' and persists immediately; " +
        "action = 'list' returns every setting with its key, type (enum options included), and current value. " +
        "For 'convertAssignmentOperatorsToBlueprintMacros' use PureNodes | Macro | MathNodes; " +
        "for the timeout/limit settings use an integer value.";

    public string GetDisplayName() => "Settings";

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
                    ["description"] = "Operation to perform. 'get'/'set' require 'key'; 'list' requires none.",
                    ["enum"] = new[] { "get", "set", "list" }
                },
                ["key"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The setting key",
                    ["enum"] = McpSettingsManager.Keys
                },
                ["value"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The new value (required for action = 'set'), ALWAYS as a string. " +
                                      "Enum name for convertAssignmentOperatorsToBlueprintMacros; for the " +
                                      "timeout/limit settings pass the integer as text (e.g. \"120000\", not 120000)"
                }
            },
            ["required"] = new[] { "action" }
        };
    }

    public ToolResult Execute(Guid callerId, Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("action", out object? actionObj) ||
            string.IsNullOrWhiteSpace(actionObj?.ToString()))
        {
            return ToolResult.Failed("Missing 'action' parameter (expected 'get' or 'set')");
        }

        string action = actionObj!.ToString()!;

        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
            return ListAll();

        if (!parameters.TryGetValue("key", out object? keyObj) ||
            string.IsNullOrWhiteSpace(keyObj?.ToString()))
        {
            return ToolResult.Failed("Missing 'key' parameter");
        }

        string key = keyObj!.ToString()!;

        if (string.Equals(action, "get", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                object value = _manager.Get(key);
                return ToolResult.Successful(
                    $"{key} = {FormatValue(value)}",
                    new Dictionary<string, object?> { ["key"] = key, ["value"] = value });
            }
            catch (KeyNotFoundException)
            {
                return ToolResult.Failed(
                    $"Unknown setting key: {key}. Available keys: {string.Join(", ", McpSettingsManager.Keys)}");
            }
        }

        if (!string.Equals(action, "set", StringComparison.OrdinalIgnoreCase))
            return ToolResult.Failed($"Unknown action: {action} (expected 'get' or 'set')");

        if (!parameters.TryGetValue("value", out object? valueObj) || valueObj is null)
            return ToolResult.Failed("Missing 'value' parameter (required for action = 'set')");

        try
        {
            _manager.Set(key, valueObj);
            object updated = _manager.Get(key);
            return ToolResult.Successful(
                $"{key} = {FormatValue(updated)} (saved to {McpSettingsManager.SettingsFile})",
                new Dictionary<string, object?> { ["key"] = key, ["value"] = updated });
        }
        catch (KeyNotFoundException)
        {
            return ToolResult.Failed(
                $"Unknown setting key: {key}. Available keys: {string.Join(", ", McpSettingsManager.Keys)}");
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            return ToolResult.Failed(ex.Message);
        }
    }

    /// <summary>Returns the key, type (enums include options), and current value for every setting.</summary>
    private ToolResult ListAll()
    {
        IReadOnlyList<Dictionary<string, object?>> items = BuildSettingDescriptors(_manager.Current);
        return ToolResult.Successful(
            $"{items.Count} settings.",
            new Dictionary<string, object?> { ["count"] = items.Count, ["settings"] = items });
    }

    /// <summary>
    /// Uses reflection to walk the public properties of <see cref="McpSettings"/>, reading the setting key
    /// from <see cref="JsonPropertyNameAttribute"/> and inferring the type and current value (enums
    /// additionally list every option).
    /// </summary>
    private static IReadOnlyList<Dictionary<string, object?>> BuildSettingDescriptors(McpSettings settings)
    {
        var list = new List<Dictionary<string, object?>>();

        foreach (PropertyInfo prop in typeof(McpSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            JsonPropertyNameAttribute? jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonAttr is null)
                continue;

            string key = jsonAttr.Name;
            object? rawValue = prop.GetValue(settings);
            Type propType = prop.PropertyType;

            object? value = propType.IsEnum ? rawValue?.ToString() : rawValue;
            var entry = new Dictionary<string, object?>
            {
                ["key"] = key,
                ["value"] = value
            };

            if (propType.IsEnum)
            {
                entry["type"] = "enum";
                entry["options"] = Enum.GetNames(propType);
            }
            else if (propType == typeof(int))
            {
                entry["type"] = "integer";
            }
            else
            {
                entry["type"] = propType.Name;
            }

            list.Add(entry);
        }

        return list;
    }

    /// <summary>Formats a setting value as a JSON-flavored lowercase string (bool prints as true/false).</summary>
    internal static string FormatValue(object value) => value switch
    {
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? ""
    };
}
