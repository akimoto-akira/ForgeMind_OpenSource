// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Net;
using System.Text;
using ForgeMindServer;
using ForgeMindServer.Compat;
using ForgeMindServer.Mcp;

// ForgeMindServer MCP server.
// Default: persistent HTTP service on loopback (Streamable HTTP + legacy SSE),
// so the process — and the UE TCP bridge it hosts — outlives any single
// client session. Pass --stdio for the old spawn-per-client mode instead.
// Test mode lives in the standalone ForgeMindServerTest runner project.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

bool stdioMode = args.Any(a => string.Equals(a, "--stdio", StringComparison.OrdinalIgnoreCase));
int httpPort = ResolveHttpPort(args);

ILogger logger = LogManager.Instance.GetLogger<ForgeMindServerEntry>();
logger.Info(null, "[ForgeMindServer] MCP server starting ({0})", stdioMode ? "stdio" : $"http on 127.0.0.1:{httpPort}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// The document engine's scan index feeds the DSL directory data source: translator
// signature / pin queries land on the scan index (engine-side + project-side;
// translators fall back to loose mode when the index is empty).

// Host services: the UE bridge for ForgeMindServer companions
ForgeMindServerHost.Start();

// The host never loads plugins by itself — the caller decides when, exactly like the test runner.
// Skipping this left the tool table at its built-ins, so clients saw no plugin tool at all.
ForgeMindServerHost.EnsurePluginsLoaded();

int exitCode = 0;
try
{
    if (stdioMode)
    {
        var server = new McpStdioServer(ForgeMindServerHost.Tools);
        await server.RunAsync(cts.Token);
    }
    else
    {
        using var server = new McpHttpServer(ForgeMindServerHost.Tools, httpPort);
        server.Start();
        logger.Info(null, "[ForgeMindServer] Listening on http://127.0.0.1:{0}/mcp", httpPort);
        logger.Info(null, "[ForgeMindServer] Server is ready and waiting for connections. Press Ctrl+C to stop.");
        await server.RunAsync(cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Normal shutdown path
}
catch (HttpListenerException ex)
{
    logger.Error(null, "[ForgeMindServer] Cannot listen on port {0}: {1} (already in use?)", httpPort, ex.Message);
    exitCode = 1;
}
catch (Exception ex)
{
    logger.Error(null, "[ForgeMindServer] Fatal error: {0}", ex);
    exitCode = 1;
}
finally
{
    ForgeMindServerHost.Stop();
    logger.Info(null, "[ForgeMindServer] MCP server stopped");
}

return exitCode;

/// <summary>
/// HTTP port precedence: <c>--port N</c> argument, then the FORGEMIND_PORT
/// environment variable, then the built-in default.
/// </summary>
static int ResolveHttpPort(string[] arguments)
{
    const int defaultPort = 8765;

    for (int i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], "--port", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(arguments[i + 1], out int parsed) && parsed is > 0 and <= 65535)
            return parsed;
    }

    string? fromEnv = Environment.GetEnvironmentVariable("FORGEMIND_PORT");
    if (int.TryParse(fromEnv, out int envPort) && envPort is > 0 and <= 65535)
        return envPort;

    return defaultPort;
}

/// <summary>Marker type for the entry-point logger category.</summary>
internal sealed class ForgeMindServerEntry
{
}
