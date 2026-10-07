// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Mcp;

/// <summary>
/// Persistent HTTP MCP server so the process (and the UE TCP bridge it owns)
/// stays alive independently of any client. Serves both current and legacy
/// transports on one loopback port:
/// <list type="bullet">
/// <item>Streamable HTTP — <c>POST /mcp</c> (single or batch JSON-RPC), <c>DELETE /mcp</c></item>
/// <item>Legacy HTTP+SSE — <c>GET /sse</c> (also <c>GET /mcp</c> as fallback),
///       <c>POST /messages?sessionId=…</c></item>
/// </list>
/// Protocol dispatch is shared with the stdio transport via <see cref="McpRequestHandler"/>.
/// </summary>
internal sealed class McpHttpServer : IDisposable
{
    /// <summary>Streamable-HTTP session header (spec 2025-03-26 onward).</summary>
    private const string SessionHeader = "Mcp-Session-Id";

    private readonly HttpListener _listener = new();
    private readonly McpRequestHandler _handler;
    private readonly ILogger _logger = LogManager.Instance.GetLogger<McpHttpServer>();

    /// <summary>Legacy SSE streams keyed by session id.</summary>
    private readonly ConcurrentDictionary<string, SseSession> _sseSessions = new();

    public McpHttpServer(IReadOnlyList<ITool> tools, int port)
    {
        _handler = new McpRequestHandler(tools);
        Port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public int Port { get; }

    /// <summary>Binds the port; throws <see cref="HttpListenerException"/> when taken.</summary>
    public void Start() => _listener.Start();

    /// <summary>Accept loop; returns once cancelled or the listener stops.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped underneath us
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // Requests are independent; never let one block the accept loop
                _ = Task.Run(() => HandleContextAsync(context, ct));
            }
        }
        finally
        {
            foreach (KeyValuePair<string, SseSession> entry in _sseSessions)
                entry.Value.Dispose();
            _sseSessions.Clear();
        }
    }

    // ===== routing =====

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        string path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";
        string method = context.Request.HttpMethod;

        try
        {
            if (method == "POST" && path == "/mcp")
            {
                await HandleStreamablePostAsync(context);
            }
            else if (method == "DELETE" && path == "/mcp")
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
            }
            else if (method == "GET" && (path == "/sse" || path == "/mcp"))
            {
                // GET /mcp doubles as the SSE endpoint so legacy clients that
                // fall back from Streamable HTTP still find a stream at the
                // same URL they were configured with.
                await HandleSseStreamAsync(context, ct);
            }
            else if (method == "POST" && path == "/messages")
            {
                await HandleSseMessageAsync(context);
            }
            else if (method == "GET" && path == "/health")
            {
                await WriteTextAsync(context.Response, 200, "text/plain", "ok");
            }
            else
            {
                await WriteTextAsync(context.Response, 404, "text/plain", "Not found");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(null, "[MCP] {0} {1} failed: {2}", method, path, ex.Message);
            try
            {
                await WriteTextAsync(context.Response, 500, "text/plain", "Internal server error");
            }
            catch
            {
                // Response already committed (e.g. SSE stream) — nothing to do
            }
        }
    }

    // ===== Streamable HTTP (current transport) =====

    private async Task HandleStreamablePostAsync(HttpListenerContext context)
    {
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        _logger.Info(null, "[MCP] Received: {0}", body);

        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            string parseError = JsonSerializer.Serialize(McpRequestHandler.BuildError(null, -32700, "Parse error"));
            await WriteTextAsync(context.Response, 400, "application/json", parseError);
            return;
        }

        var responses = new List<string>();
        bool sawInitialize = false;

        IEnumerable<JsonElement> frames = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : [root];

        foreach (JsonElement frame in frames)
        {
            if (McpRequestHandler.IsInitialize(frame))
                sawInitialize = true;

            object? response = _handler.Handle(frame);
            if (response != null)
                responses.Add(JsonSerializer.Serialize(response));
        }

        // Session lifecycle is lenient on loopback: a fresh id is minted at
        // initialize and echoed back, but unknown ids are still served (a
        // restarted server must not strand previously configured clients).
        if (sawInitialize)
            context.Response.Headers[SessionHeader] = Guid.NewGuid().ToString("D");

        if (responses.Count == 0)
        {
            // Only notifications in the request — nothing to return
            _logger.Info(null, "[MCP] Notification received - no reply needed");
            context.Response.StatusCode = 202;
            context.Response.Close();
            return;
        }

        string payload = responses.Count == 1
            ? responses[0]
            : "[" + string.Join(",", responses) + "]";
        _logger.Info(null, "[MCP] Sent: {0}", payload);
        await WriteTextAsync(context.Response, 200, "application/json", payload);
    }

    // ===== Legacy HTTP+SSE transport =====

    private async Task HandleSseStreamAsync(HttpListenerContext context, CancellationToken ct)
    {
        string sessionId = Guid.NewGuid().ToString("N");
        HttpListenerResponse response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.ContentEncoding = Encoding.UTF8;
        // No ContentLength64 — the stream stays open until the client leaves

        var session = new SseSession(response);
        _sseSessions[sessionId] = session;
        _logger.Info(null, "[MCP] SSE client connected (session {0})", sessionId);

        try
        {
            await session.WriteEventAsync("endpoint", $"/messages?sessionId={sessionId}");

            // Keepalives double as dead-client detection on the open stream
            while (!ct.IsCancellationRequested && !session.Closed)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                await session.WriteRawAsync(": keepalive\n\n");
            }
        }
        catch (OperationCanceledException)
        {
            // Server shutting down
        }
        catch (Exception)
        {
            // Client disconnected mid-stream — expected
        }
        finally
        {
            _sseSessions.TryRemove(sessionId, out _);
            session.Dispose();
            _logger.Info(null, "[MCP] SSE client disconnected (session {0})", sessionId);
        }
    }

    private async Task HandleSseMessageAsync(HttpListenerContext context)
    {
        string? sessionId = context.Request.QueryString["sessionId"];
        if (string.IsNullOrEmpty(sessionId) || !_sseSessions.TryGetValue(sessionId, out SseSession? session))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        _logger.Info(null, "[MCP] Received (SSE session {0}): {1}", sessionId, body);

        // Legacy spec: acknowledge immediately; the reply rides the SSE stream
        context.Response.StatusCode = 202;
        context.Response.Close();

        object? frame = _handler.HandleText(body);
        if (frame != null)
        {
            try
            {
                string json = JsonSerializer.Serialize(frame);
                _logger.Info(null, "[MCP] Sent (SSE session {0}): {1}", sessionId, json);
                await session.WriteEventAsync("message", json);
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[MCP] SSE push to {0} failed: {1}", sessionId, ex.Message);
            }
        }
        else
        {
            _logger.Info(null, "[MCP] Notification received (SSE session {0}) - no reply needed", sessionId);
        }
    }

    // ===== helpers =====

    private static async Task WriteTextAsync(HttpListenerResponse response, int status, string contentType, string text)
    {
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentEncoding = Encoding.UTF8;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose()
    {
        foreach (KeyValuePair<string, SseSession> entry in _sseSessions)
            entry.Value.Dispose();
        _sseSessions.Clear();

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Best effort on shutdown
        }
    }

    /// <summary>One open legacy SSE stream; writes are serialized to keep events intact.</summary>
    private sealed class SseSession : IDisposable
    {
        private readonly HttpListenerResponse _response;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private volatile bool _closed;

        public SseSession(HttpListenerResponse response) => _response = response;

        public bool Closed => _closed;

        public async Task WriteRawAsync(string text)
        {
            if (_closed)
                throw new IOException("SSE session is closed");

            await _writeLock.WaitAsync();
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                await _response.OutputStream.WriteAsync(bytes);
                await _response.OutputStream.FlushAsync();
            }
            catch
            {
                _closed = true;
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task WriteEventAsync(string eventName, string data) =>
            WriteRawAsync($"event: {eventName}\ndata: {data}\n\n");

        public void Dispose()
        {
            _closed = true;
            try
            {
                _response.Close();
            }
            catch
            {
                // Stream may already be torn down by the client
            }
        }
    }
}
