// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Mcp;

/// <summary>
/// Model Context Protocol server over stdio (newline-delimited JSON-RPC 2.0).
/// stdout carries protocol frames only — every diagnostic goes to stderr.
/// Protocol dispatch lives in <see cref="McpRequestHandler"/> and is shared
/// with the HTTP transport.
/// </summary>
internal sealed class McpStdioServer
{
    private readonly McpRequestHandler _handler;
    private readonly ILogger _logger = LogManager.Instance.GetLogger<McpStdioServer>();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public McpStdioServer(IReadOnlyList<ITool> tools) => _handler = new McpRequestHandler(tools);

    /// <summary>
    /// Runs the read loop until stdin closes (the client exited).
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await Console.In.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // stdin closed — the MCP client (host) is gone
            if (line == null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            _logger.Info(null, "[MCP] Received: {0}", line);

            object? frame = _handler.HandleText(line);
            if (frame != null)
                await WriteAsync(frame, ct);
            else
                _logger.Info(null, "[MCP] Notification received - no reply needed");
        }
    }

    /// <summary>
    /// Serializes one frame onto stdout; writes are serialized to keep frames intact.
    /// The envelope is serialized without null-omission so JSON-RPC mandates like
    /// "id: null on parse errors" stay spec-compliant.
    /// </summary>
    private async Task WriteAsync(object frame, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            string json = JsonSerializer.Serialize(frame);
            _logger.Info(null, "[MCP] Sent: {0}", json);
            await Console.Out.WriteLineAsync(json);
            await Console.Out.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
