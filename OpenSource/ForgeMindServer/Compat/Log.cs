// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji


namespace ForgeMindServer.Compat;

/// <summary>
/// Minimal logging contract matching the SiliconLife host's ILogger call sites
/// (context object first, composite format string, args).
/// </summary>
public interface ILogger
{
    void Debug(object? context, string message, params object[] args);

    void Info(object? context, string message, params object[] args);

    void Warn(object? context, string message, params object[] args);

    void Error(object? context, string message, params object[] args);
}

/// <summary>
/// Standalone logger factory. All output goes to stderr — stdout is reserved
/// for the MCP JSON-RPC channel and any stray console write would corrupt it.
/// Additional sinks (e.g. file logger) can be registered via <see cref="AddSink"/>.
/// </summary>
public sealed class LogManager
{
    private LogManager() { }

    /// <summary>Host-compatible singleton accessor.</summary>
    public static LogManager Instance { get; } = new();

    private readonly List<ILogger> _sinks = new();
    private readonly object _sinkLock = new();

    /// <summary>Registers an additional log sink (e.g., a file logger).</summary>
    public void AddSink(ILogger sink)
    {
        lock (_sinkLock) { _sinks.Add(sink); }
    }

    /// <summary>Removes a registered log sink.</summary>
    public void RemoveSink(ILogger sink)
    {
        lock (_sinkLock) { _sinks.Remove(sink); }
    }

    /// <summary>Gets a snapshot of currently registered sinks (thread-safe).</summary>
    private ILogger[] GetSinks()
    {
        lock (_sinkLock) { return _sinks.ToArray(); }
    }

    public ILogger GetLogger<T>() => new CompositeLogger(typeof(T).Name, this);

    /// <summary>Creates a logger for a category name (for static types and other scenarios where generics cannot be used).</summary>
    public ILogger GetLogger(string category) => new CompositeLogger(category, this);

    /// <summary>
    /// Composite logger: outputs to stderr and all registered sinks simultaneously.
    /// </summary>
    private sealed class CompositeLogger : ILogger
    {
        private readonly string _category;
        private readonly LogManager _owner;

        public CompositeLogger(string category, LogManager owner)
        {
            _category = category;
            _owner = owner;
        }

        public void Debug(object? context, string message, params object[] args) => Write("DEBUG", message, args);
        public void Info(object? context, string message, params object[] args) => Write("INFO", message, args);
        public void Warn(object? context, string message, params object[] args) => Write("WARN", message, args);
        public void Error(object? context, string message, params object[] args) => Write("ERROR", message, args);

        private void Write(string level, string message, object[] args)
        {
            string formatted;
            try { formatted = args is { Length: > 0 } ? string.Format(message, args) : message; }
            catch (FormatException) { formatted = message; }

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{_category}] {formatted}";

            // stderr
            try { Console.Error.WriteLine(line); } catch { }

            // sinks
            foreach (ILogger sink in _owner.GetSinks())
            {
                try
                {
                    if (level == "DEBUG") sink.Debug(null, formatted);
                    else if (level == "INFO") sink.Info(null, formatted);
                    else if (level == "WARN") sink.Warn(null, formatted);
                    else if (level == "ERROR") sink.Error(null, formatted);
                }
                catch { }
            }
        }
    }
}
