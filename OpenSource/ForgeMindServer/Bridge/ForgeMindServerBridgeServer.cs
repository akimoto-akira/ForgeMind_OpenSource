// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Bridge;

/// <summary>
/// TCP bridge server on the host side. Listens on a loopback-only,
/// dynamically assigned port and publishes <c>{port, token, pid}</c> to
/// <c>%USERPROFILE%\.forgemind\bridge.json</c> for companions to discover.
/// Single-host model: when a bridge file of a still-living host exists,
/// this server refuses to start.
/// </summary>
public sealed class ForgeMindServerBridgeServer : TickObject
{
    /// <summary>Discovery file directory: {UserProfile}/.forgemind.</summary>
    private static readonly string DiscoveryDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".forgemind");

    /// <summary>Discovery file: bridge.json under <see cref="DiscoveryDirectory"/>.</summary>
    public static readonly string DiscoveryFile = Path.Combine(DiscoveryDirectory, "bridge.json");

    /// <summary>Silence window before a session is considered dead.</summary>
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(90);

    private readonly ILogger _logger = LogManager.Instance.GetLogger<ForgeMindServerBridgeServer>();
    private readonly List<BridgeSession> _sessions = [];
    private readonly object _sessionLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _token = "";
    private string _aesKey = "";
    private string[] _requestTokens = [];

    public int Port { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Snapshot of the current companion sessions (project file, engine
    /// version, advertised commands, ready state) for context reporting.
    /// </summary>
    internal IReadOnlyList<(string ProjectFile, string EngineVersion, string[] Commands, bool IsReady)> GetSessionSnapshot()
    {
        lock (_sessionLock)
        {
            return _sessions
                .Select(s => (s.ProjectFile, s.EngineVersion, s.Commands, s.IsReady))
                .ToList();
        }
    }

    /// <summary>Tick every 15 seconds for the silent-session sweep; MainLoop registration happens in the base ctor.</summary>
    public ForgeMindServerBridgeServer() : base(TimeSpan.FromSeconds(15))
    {
    }

    /// <summary>
    /// Starts the listener and publishes the discovery file.
    /// Returns false (with a warning log) when another host already owns the bridge.
    /// </summary>
    public bool Start()
    {
        if (IsRunning)
            return true;

        // Single-host guard: refuse when a live host still owns the discovery file
        if (File.Exists(DiscoveryFile))
        {
            try
            {
                using var existing = JsonDocument.Parse(File.ReadAllText(DiscoveryFile));
                if (existing.RootElement.TryGetProperty("pid", out JsonElement pidEl) &&
                    pidEl.TryGetInt32(out int existingPid))
                {
                    using var process = Process.GetProcessById(existingPid);
                    if (!process.HasExited)
                    {
                        _logger.Warn(null,
                            "[Bridge] Another host (pid {0}) already runs the bridge — skipping bridge startup", existingPid);
                        return false;
                    }
                }
            }
            catch (ArgumentException)
            {
                // Process id not found — stale file, safe to take over
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[Bridge] Could not inspect existing bridge.json: {0}", ex.Message);
            }
        }

        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        // AES-128 key derived from machine code (SHA-256 first 16 bytes).
        // Same machine always produces the same key, so even if bridge.json
        // is deleted the UE side can re-derive the key and decrypt reserve1
        // to recover the original launch time — preventing trial reset by
        // deleting the discovery file.
        string machineCode = global::ForgeMindServer.MachineIdentity.Generate();
        byte[] machineHash = SHA256.HashData(Encoding.UTF8.GetBytes(machineCode));
        _aesKey = Convert.ToHexString(machineHash, 0, 16).ToLowerInvariant();

        // reserve1: pure-numeric datetime (yyyyMMddHHmmss) AES-encrypted → Base64.
        // If an existing bridge.json already has a reserve1 from a prior launch,
        // reuse it so the first-launch timestamp survives restarts.
        string reserve1 = "";
        if (File.Exists(DiscoveryFile))
        {
            try
            {
                using var existing = JsonDocument.Parse(File.ReadAllText(DiscoveryFile));
                if (existing.RootElement.TryGetProperty("reserve1", out JsonElement r1El) &&
                    r1El.ValueKind == JsonValueKind.String)
                {
                    reserve1 = r1El.GetString() ?? "";
                }
            }
            catch { /* stale/corrupt file — regenerate below */ }
        }

        if (string.IsNullOrEmpty(reserve1))
        {
            string numericDateTime = DateTime.Now.ToString("yyyyMMddHHmmss");
            byte[] plainBytes = Encoding.UTF8.GetBytes(numericDateTime);
            using var aes = Aes.Create();
            aes.Key = Convert.FromHexString(_aesKey);
            aes.IV = new byte[16]; // zero IV — single-block payload, no chaining needed
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using (var ms = new MemoryStream())
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
                reserve1 = Convert.ToBase64String(ms.ToArray());
            }
        }

        // reserve2: 20 random request tokens, AES-encrypted → Base64.
        // The MCP side picks one at random per request and carries it in
        // the "rt" field; the UE bridge rejects requests whose token is
        // not in this set.
        //
        // If bridge.json already has a reserve2 from a prior launch, reuse
        // it so the token set stays stable across restarts.
        string reserve2 = "";
        if (File.Exists(DiscoveryFile))
        {
            try
            {
                using var existing2 = JsonDocument.Parse(File.ReadAllText(DiscoveryFile));
                if (existing2.RootElement.TryGetProperty("reserve2", out JsonElement r2El) &&
                    r2El.ValueKind == JsonValueKind.String)
                {
                    reserve2 = r2El.GetString() ?? "";
                }
            }
            catch { /* stale/corrupt file — regenerate below */ }
        }

        if (string.IsNullOrEmpty(reserve2))
        {
            // Generate 20 random 32-char hex tokens
            _requestTokens = new string[20];
            for (int i = 0; i < 20; i++)
                _requestTokens[i] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

            // Serialize as JSON array, then AES-encrypt the JSON → Base64
            string tokensJson = JsonSerializer.Serialize(_requestTokens);
            byte[] plainBytes2 = Encoding.UTF8.GetBytes(tokensJson);
            using var aes2 = Aes.Create();
            aes2.Key = Convert.FromHexString(_aesKey);
            aes2.IV = new byte[16];
            aes2.Mode = CipherMode.CBC;
            aes2.Padding = PaddingMode.PKCS7;
            using (var ms2 = new MemoryStream())
            using (var cs2 = new CryptoStream(ms2, aes2.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs2.Write(plainBytes2, 0, plainBytes2.Length);
                cs2.FlushFinalBlock();
                reserve2 = Convert.ToBase64String(ms2.ToArray());
            }
        }
        else
        {
            // Decrypt existing reserve2 to recover the 20 tokens
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(reserve2);
                using var aes3 = Aes.Create();
                aes3.Key = Convert.FromHexString(_aesKey);
                aes3.IV = new byte[16];
                aes3.Mode = CipherMode.CBC;
                aes3.Padding = PaddingMode.PKCS7;
                using (var ms3 = new MemoryStream(cipherBytes))
                using (var cs3 = new CryptoStream(ms3, aes3.CreateDecryptor(), CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs3, Encoding.UTF8))
                {
                    string tokensJson = sr.ReadToEnd();
                    _requestTokens = JsonSerializer.Deserialize<string[]>(tokensJson) ?? [];
                }
            }
            catch
            {
                // Decryption failed — regenerate fresh tokens
                _requestTokens = new string[20];
                for (int i = 0; i < 20; i++)
                    _requestTokens[i] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                string tokensJson = JsonSerializer.Serialize(_requestTokens);
                byte[] plainBytes2 = Encoding.UTF8.GetBytes(tokensJson);
                using var aes2 = Aes.Create();
                aes2.Key = Convert.FromHexString(_aesKey);
                aes2.IV = new byte[16];
                aes2.Mode = CipherMode.CBC;
                aes2.Padding = PaddingMode.PKCS7;
                using (var ms2 = new MemoryStream())
                using (var cs2 = new CryptoStream(ms2, aes2.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs2.Write(plainBytes2, 0, plainBytes2.Length);
                    cs2.FlushFinalBlock();
                    reserve2 = Convert.ToBase64String(ms2.ToArray());
                }
            }
        }

        // Dynamic port: bind loopback port 0 and read back the OS assignment
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var info = new Dictionary<string, object?>
        {
            ["port"] = Port,
            ["token"] = _token,
            ["reserve1"] = reserve1,
            ["reserve2"] = reserve2,
            ["reserve3"] = _aesKey,
            ["pid"] = Environment.ProcessId,
            ["hostVersion"] = typeof(ForgeMindServerBridgeServer).Assembly.GetName().Version?.ToString() ?? ""
        };
        Directory.CreateDirectory(DiscoveryDirectory);
        File.WriteAllText(DiscoveryFile, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);

        IsRunning = true;
        _logger.Info(null, "[Bridge] Bridge server listening on 127.0.0.1:{0} (discovery: {1})", Port, DiscoveryFile);
        return true;
    }

    /// <summary>Stops the listener, closes all sessions and removes the discovery file.</summary>
    public void Stop()
    {
        if (!IsRunning)
            return;

        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* already stopped */ }

        lock (_sessionLock)
        {
            foreach (BridgeSession session in _sessions)
                session.Dispose();
            _sessions.Clear();
        }

        try { if (File.Exists(DiscoveryFile)) File.Delete(DiscoveryFile); } catch { /* best effort */ }

        IsRunning = false;
        _logger.Info(null, "[Bridge] Bridge server stopped");
    }

    /// <summary>Ready sessions only.</summary>
    public BridgeSession[] GetSessions()
    {
        lock (_sessionLock)
            return _sessions.Where(s => s.IsReady).ToArray();
    }

    /// <summary>Locates the session for a project by absolute .uproject path (case-insensitive).</summary>
    public BridgeSession? GetSessionByProject(string projectFile)
    {
        string query = NormalizePath(projectFile);
        lock (_sessionLock)
            return _sessions.FirstOrDefault(s =>
                s.IsReady && string.Equals(NormalizePath(s.ProjectFile), query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Normalizes a path for cross-platform comparison: the companion reports
    /// forward-slash paths (UE FPaths convention) while .NET produces
    /// backslashes, so both sides are canonicalized before matching.
    /// Falls back to separator-normalized text when the path is not resolvable.
    /// </summary>
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }
    }

    /// <summary>Accept loop — one session task per companion connection.</summary>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn(null, "[Bridge] Accept failed: {0}", ex.Message);
                break;
            }

            var session = new BridgeSession(client, _token, _aesKey, _requestTokens);
            lock (_sessionLock)
                _sessions.Add(session);

            _ = RunSessionAsync(session, ct);
        }
    }

    private async Task RunSessionAsync(BridgeSession session, CancellationToken ct)
    {
        try
        {
            await session.RunAsync(HandleRequestAsync, HandleEvent, ct);
        }
        finally
        {
            lock (_sessionLock)
                _sessions.Remove(session);
            session.Dispose();
            // Editor session disconnected (crash / network loss / host shutdown) → immediately fail its related long tasks;
            // no need to wait for TTL — the AI gets the failed status on next poll (Requirements §2.5 engine process monitoring).
            if (!string.IsNullOrEmpty(session.ProjectFile))
                LongTaskManager.Instance.FailByProject(
                    session.ProjectFile,
                    $"Editor session disconnected (project {session.ProjectFile}) — the editor may have crashed or lost the network connection.");
        }
    }

    /// <summary>Host-side request handler: companion-originated requests.</summary>
    private async Task HandleRequestAsync(BridgeSession session, BridgeMessage request)
    {
        try
        {
            switch (request.Name)
            {
                case "ping":
                    await session.SendAsync(BridgeMessage.NewResponse(request.Id ?? "", "ping", new { pong = true }), CancellationToken.None);
                    break;
                default:
                    await session.SendAsync(
                        BridgeMessage.NewErrorResponse(request.Id ?? "", request.Name, $"Unknown host command '{request.Name}'"),
                        CancellationToken.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(null, "[Bridge] Handling '{0}' failed: {1}", request.Name, ex.Message);
        }
    }

    /// <summary>Host-side event handler: companion-originated events.</summary>
    private void HandleEvent(BridgeSession session, BridgeMessage message)
    {
        _logger.Info(null, "[Bridge] Event '{0}' from {1}: {2}",
            message.Name, session.ProjectFile, message.Payload?.GetRawText() ?? "{}");
    }

    /// <summary>MainLoop heartbeat (15s interval): drop silent sessions.</summary>
    protected override void OnTick(TimeSpan deltaTime)
    {
        if (!IsRunning)
            return;

        BridgeSession[] silent;
        lock (_sessionLock)
            silent = _sessions.Where(s => DateTime.UtcNow - s.LastReceivedUtc > SessionTimeout).ToArray();

        foreach (BridgeSession session in silent)
        {
            _logger.Warn(null, "[Bridge] Dropping silent session: {0}", session.ProjectFile);
            lock (_sessionLock)
                _sessions.Remove(session);
            session.Dispose();
            // Silent timed-out sessions are also treated as disconnected; immediately fail their related long tasks.
            if (!string.IsNullOrEmpty(session.ProjectFile))
                LongTaskManager.Instance.FailByProject(
                    session.ProjectFile,
                    $"Editor session timed out with no response (project {session.ProjectFile}) — marked as disconnected.");
        }
    }
}
