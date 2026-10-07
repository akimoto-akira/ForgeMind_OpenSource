// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Compat;

/// <summary>
/// Interface for tools exposed to AI clients.
/// Tool parameters are described using JSON Schema format.
/// Local copy of the SiliconLife host's ITool contract, kept signature-identical
/// so the tool implementations stay unchanged after the decoupling.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Gets the unique name of this tool (used as function name in AI requests)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a human-readable description of this tool
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the localized display name of this tool
    /// </summary>
    /// <param name="language">The language to use for the display name</param>
    /// <returns>The localized display name</returns>
    string GetDisplayName();

    /// <summary>
    /// Gets the JSON Schema describing the parameters for this tool.
    /// The schema must have "type": "object" at the top level.
    /// </summary>
    Dictionary<string, object> GetParameterSchema();

    /// <summary>
    /// Executes the tool with the given parameters
    /// </summary>
    /// <param name="callerId">The GUID of the caller invoking this tool</param>
    /// <param name="parameters">The parameters provided by the AI</param>
    /// <returns>The result of the tool execution</returns>
    ToolResult Execute(Guid callerId, Dictionary<string, object> parameters);
}
