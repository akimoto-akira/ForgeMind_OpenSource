// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using System.Text;
using System.Text.Json;
using ForgeMindServer.Compat;

namespace ForgeMindServer.Test;

/// <summary>
/// Test suite: discovers all <see cref="ITestCase"/> implementations via reflection,
/// groups by <see cref="ITestCase.Group"/>, sorts by <see cref="ITestCase.Order"/> within each group, executes sequentially with fail-fast.
///
/// "Launch editor" and "Close editor" are also ordinary <see cref="ITestCase"/> instances——
/// their execution position is controlled via <see cref="ITestCase.Order"/>
/// (<c>Order = 0</c> runs first = launch, <c>Order = int.MaxValue</c> runs last = close).
///
/// CLI filtering (see <see cref="Program"/> --Test parameter):
/// - No args: all groups, all test cases;
/// - Group name: execute that group only (entire group);
/// - Group name + order: cases with matching Order in that group + all
///   <see cref="MustRunAttribute"/> must-run cases (launch / close editor).
///
/// After execution, outputs JSON report and human-readable summary to output directory.
/// </summary>
public sealed class TestSuite
{
    private readonly IReadOnlyList<ITool> _tools;
    private readonly ILogger _logger = LogManager.Instance.GetLogger<TestSuite>();
    private TestFileLogger? _fileLogger;

    /// <summary>Sentinel string for ungrouped cases (avoid using null as Dictionary key).</summary>
    private const string NoGroup = "";

    public TestSuite(IReadOnlyList<ITool> tools)
    {
        _tools = tools;
    }

    /// <summary>
    /// Discovers and executes test cases. Returns 0 = all passed, 1 = failures (or invalid filter).
    /// </summary>
    /// <param name="groupFilter">Execute only this group (null = all groups).</param>
    /// <param name="orderFilter">Execute cases with this Order within the group + all
    /// <see cref="MustRunAttribute"/> must-run cases (null = entire group).
    /// On failure, remaining must-run cases still execute (ensures close editor runs).</param>
    /// <param name="outputDir">Run directory to write into. When supplied, the caller has already
    /// created it and attached the test.log sink, so the suite neither creates a new directory nor
    /// attaches a second sink — that is what lets the pre-test build log into the same file.</param>
    public int RunAll(string? groupFilter = null, int? orderFilter = null, string? outputDir = null)
    {
        // Discover all ITestCase implementations
        List<ITestCase> allCases = DiscoverImplementations<ITestCase>();

        // Bucket cases by Group (null / whitespace -> NoGroup = "")
        var grouped = new Dictionary<string, List<ITestCase>>();
        foreach (ITestCase tc in allCases)
        {
            string key = string.IsNullOrWhiteSpace(tc.Group) ? NoGroup : tc.Group;
            if (!grouped.TryGetValue(key, out var list))
            {
                list = new List<ITestCase>();
                grouped[key] = list;
            }
            list.Add(tc);
        }

        // Group ordering: plain string sort (ordinal). The ungrouped bucket ("")
        // naturally sorts first since an empty string precedes any group name.
        var orderedGroups = grouped.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        // ── Filter: --Test [groupName] [order] ──
        if (groupFilter != null)
        {
            if (!grouped.ContainsKey(groupFilter))
            {
                var known = orderedGroups.Where(k => k != NoGroup).ToList();
                _logger.Error(null, "[Test] Group '{0}' not found. Available: {1}",
                    groupFilter, known.Count > 0 ? string.Join(", ", known) : "(none)");
                return 1;
            }
            orderedGroups = new List<string> { groupFilter };
        }

        if (orderFilter != null)
        {
            List<ITestCase> groupCases = grouped[groupFilter!];
            if (!groupCases.Any(c => c.Order == orderFilter.Value))
            {
                string orders = string.Join(", ",
                    groupCases.Select(c => c.Order).OrderBy(o => o).Distinct());
                _logger.Error(null, "[Test] Order {0} not found in group '{1}'. Available orders: {2}",
                    orderFilter.Value, groupFilter!, orders);
                return 1;
            }
            // Cases matching the order + all must-run cases (e.g., launch / close editor)
            grouped[groupFilter!] = groupCases
                .Where(c => IsMustRun(c) || c.Order == orderFilter.Value)
                .ToList();
        }

        int totalCases = orderedGroups.Sum(g => grouped[g].Count);
        if (totalCases == 0)
        {
            _logger.Warn(null, "[Test] No test cases found");
            return 1;
        }

        // Create output directory: d:\ForgeMind\TestRuns\<yyyyMMdd_HHmm>\
        // When the caller supplied one it also attached the test.log sink, so we must not open
        // the same file a second time (StreamWriter would fail with a sharing violation).
        bool sinkOwnedByCaller = outputDir != null;
        outputDir ??= Path.Combine(
            @"d:\ForgeMind\TestRuns",
            DateTime.Now.ToString("yyyyMMdd_HHmm"));
        Directory.CreateDirectory(outputDir);

        // Start file logger——output to both stderr and test.log.
        // Skipped when the caller already attached one (pre-test build must reach this file).
        if (!sinkOwnedByCaller)
        {
            _fileLogger = new TestFileLogger(Path.Combine(outputDir, "test.log"));
            LogManager.Instance.AddSink(_fileLogger);
        }

        // Filter parameters and case list must be output after file sink is attached to land in test.log
        if (groupFilter != null || orderFilter != null)
        {
            _logger.Info(null, "[Test] Filter: group={0}, order={1}",
                groupFilter ?? "(all)", orderFilter?.ToString() ?? "(all)");
        }

        _logger.Info(null, "[Test] Discovered {0} test case(s) in {1} group(s):",
            totalCases, orderedGroups.Count);
        foreach (string groupName in orderedGroups)
        {
            List<ITestCase> groupCases = grouped[groupName];
            string displayName = groupName == NoGroup ? "(no group)" : groupName;
            _logger.Info(null, "[Test]   Group: {0} — {1} case(s)", displayName, groupCases.Count);
            foreach (ITestCase tc in groupCases.OrderBy(c => c.Order))
            {
                _logger.Info(null, "[Test]     [{0,3}] {1}", tc.Order, tc.Name);
            }
        }

        _logger.Info(null, "[Test] Output directory: {0}", outputDir);

        var report = new TestSuiteReport
        {
            StartedAt = DateTime.UtcNow,
            OutputDir = outputDir,
            TotalCases = totalCases,
        };

        bool allPassed = true;

        foreach (string groupName in orderedGroups)
        {
            List<ITestCase> groupCases = grouped[groupName];
            string displayName = groupName == NoGroup ? "(no group)" : groupName;

            // Within-group ordering: Order first, then Name
            groupCases.Sort((a, b) =>
            {
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            // Group-level context (same group shares groupDir)
            string groupDir = groupName != NoGroup
                ? Path.Combine(outputDir, SanitizeDirName(groupName))
                : outputDir;
            Directory.CreateDirectory(groupDir);

            _logger.Info(null, "[Test] ═══ Group: {0} ═══", displayName);

            // ── Group cases (execute sequentially by Order) ──
            // Full / group mode: fail-fast (stop on failure).
            // Order filter mode: skip normal cases after target failure, must-run cases still execute.
            bool groupFailed = false;
            foreach (ITestCase tc in groupCases)
            {
                string caseName = tc.Name;
                if (groupFailed && !IsMustRun(tc))
                {
                    _logger.Info(null, "[Test] -- SKIP (preceding failure): {0} --", caseName);
                    continue;
                }
                _logger.Info(null, "[Test] ── Running: {0} ──", caseName);

                var caseLogger = LogManager.Instance.GetLogger($"Test.{displayName}.{caseName}");
                var caseCtx = new TestContext(_tools, outputDir, groupDir, caseLogger);

                var caseReport = new TestCaseReport
                {
                    Name = caseName,
                    Group = groupName == NoGroup ? null : groupName,
                    Order = tc.Order,
                    StartedAt = DateTime.UtcNow,
                };

                bool passed;
                string? failureReason = null;

                try
                {
                    tc.Run(caseCtx).GetAwaiter().GetResult();
                    passed = true;
                    _logger.Info(null, "[Test] PASS: {0}", caseName);
                }
                catch (TestFailureException ex)
                {
                    passed = false;
                    failureReason = ex.Message;
                    _logger.Error(null, "[Test] FAIL: {0} — {1}", caseName, ex.Message);
                }
                catch (Exception ex)
                {
                    passed = false;
                    failureReason = ex.ToString();
                    _logger.Error(null, "[Test] FAIL: {0} — {1}", caseName, ex);
                }

                caseReport.Passed = passed;
                caseReport.FailureReason = failureReason;
                caseReport.FinishedAt = DateTime.UtcNow;
                report.Cases.Add(caseReport);

                if (!passed)
                {
                    groupFailed = true;
                    allPassed = false;
                    if (orderFilter == null)
                    {
                        break; // Group-level fail-fast (full / group mode)
                    }
                    // Order filter mode: continue loop, remaining must-run cases still execute
                }
            }

            if (groupFailed)
            {
                _logger.Error(null, "[Test] Group '{0}' failed{1}",
                    displayName, orderFilter == null ? " — stopping (fail-fast)" : "");
                if (orderFilter == null)
                {
                    break; // Inter-group fail-fast
                }
            }
            else
            {
                _logger.Info(null, "[Test] Group '{0}' passed", displayName);
            }
        }

        report.FinishedAt = DateTime.UtcNow;
        report.AllPassed = allPassed;
        report.PassedCount = report.Cases.Count(c => c.Passed);
        report.FailedCount = report.Cases.Count(c => !c.Passed);

        WriteReport(outputDir, report);

        _logger.Info(null, "[Test] Suite finished: {0}/{1} passed — {2}",
            report.PassedCount, report.TotalCases,
            allPassed ? "ALL PASSED" : "FAILED");

        // Close file logger (when the caller attached one, it disposes it itself)
        if (_fileLogger is not null)
        {
            LogManager.Instance.RemoveSink(_fileLogger);
            _fileLogger.Dispose();
        }

        return allPassed ? 0 : 1;
    }

    /// <summary>
    /// Whether the test case has the must-run marker (<see cref="MustRunAttribute"/>).
    /// </summary>
    private static bool IsMustRun(ITestCase testCase)
    {
        return testCase.GetType().GetCustomAttribute<MustRunAttribute>() != null;
    }

    /// <summary>
    /// Discover all implementations of a specific interface in the current assembly via reflection.
    /// </summary>
    private List<T> DiscoverImplementations<T>() where T : class
    {
        Type interfaceType = typeof(T);
        Assembly asm = interfaceType.Assembly;

        var list = new List<T>();

        foreach (Type type in asm.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (!interfaceType.IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                _logger.Warn(null, "[Test] Skipping {0}: no parameterless constructor", type.Name);
                continue;
            }

            try
            {
                var instance = (T)Activator.CreateInstance(type)!;
                list.Add(instance);
            }
            catch (Exception ex)
            {
                _logger.Error(null, "[Test] Failed to instantiate {0}: {1}", type.Name, ex.Message);
            }
        }

        return list;
    }

    /// <summary>
    /// Convert group name to a safe directory name (remove invalid characters).
    /// </summary>
    private static string SanitizeDirName(string name)
    {
        char[] invalid = Path.GetInvalidPathChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }

    private void WriteReport(string outputDir, TestSuiteReport report)
    {
        try
        {
            // JSON report
            string reportPath = Path.Combine(outputDir, "report.json");
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            };
            string reportJson = JsonSerializer.Serialize(report, options);
            File.WriteAllText(reportPath, reportJson);

            // Human-readable summary
            string summaryPath = Path.Combine(outputDir, "summary.txt");
            using StringWriter sw = new();
            sw.WriteLine("=== Test Suite Report ===");
            sw.WriteLine($"Started:  {report.StartedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sw.WriteLine($"Finished: {report.FinishedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sw.WriteLine($"Result:   {(report.AllPassed ? "ALL PASSED" : "FAILED")} " +
                         $"({report.PassedCount}/{report.TotalCases} passed)");
            sw.WriteLine();

            string? currentGroup = null;
            foreach (TestCaseReport cr in report.Cases)
            {
                string groupName = cr.Group ?? "(no group)";
                if (groupName != currentGroup)
                {
                    sw.WriteLine($"  [{groupName}]");
                    currentGroup = groupName;
                }

                string status = cr.Passed ? "PASS" : "FAIL";
                sw.WriteLine($"    [{status}] [{cr.Order,3}] {cr.Name}");
                if (!cr.Passed && !string.IsNullOrEmpty(cr.FailureReason))
                {
                    sw.WriteLine($"           → {cr.FailureReason}");
                }
            }

            File.WriteAllText(summaryPath, sw.ToString());

            _logger.Info(null, "[Test] Report: {0}", reportPath);
        }
        catch (Exception ex)
        {
            _logger.Warn(null, "[Test] Failed to write report: {0}", ex.Message);
        }
    }
}

// ── Report data models ──

public sealed class TestSuiteReport
{
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public bool AllPassed { get; set; }
    public int TotalCases { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public string OutputDir { get; set; } = string.Empty;
    public List<TestCaseReport> Cases { get; set; } = new();
}

public sealed class TestCaseReport
{
    public string Name { get; set; } = string.Empty;
    public string? Group { get; set; }
    public int Order { get; set; }
    public bool Passed { get; set; }
    public string? FailureReason { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}

// ── File logger: writes all logs to file simultaneously ──

/// <summary>
/// Logger sink that writes log lines to a specified file.
/// Works in parallel with <see cref="LogManager"/>'s stderr logger,
/// implementing "output to both command line and file".
/// </summary>
internal sealed class TestFileLogger : ILogger, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public TestFileLogger(string filePath)
    {
        _writer = new StreamWriter(filePath, append: false, Encoding.UTF8)
        {
            AutoFlush = true,
        };
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

        try
        {
            lock (_lock)
            {
                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {formatted}");
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try { _writer.Dispose(); } catch { }
    }
}
