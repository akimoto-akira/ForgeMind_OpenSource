// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Compat;

/// <summary>
/// Result of a tool execution.
/// Local copy of the SiliconLife host's ToolResult, kept signature-identical.
/// </summary>
public class ToolResult
{
    /// <summary>
    /// Gets whether the tool execution was successful
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the result message (may be displayed to the user)
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets optional structured data returned by the tool
    /// </summary>
    public object? Data { get; }

    private ToolResult(bool success, string message, object? data)
    {
        Success = success;
        Message = message;
        Data = data;
    }

    /// <summary>
    /// Creates a successful tool result
    /// </summary>
    public static ToolResult Successful(string message, object? data = null)
    {
        return new ToolResult(true, message, data);
    }

    /// <summary>
    /// Creates a failed tool result
    /// </summary>
    public static ToolResult Failed(string message, object? data = null)
    {
        return new ToolResult(false, message, data);
    }
}
