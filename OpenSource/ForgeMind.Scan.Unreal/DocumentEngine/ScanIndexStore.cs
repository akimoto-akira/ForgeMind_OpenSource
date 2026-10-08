// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.Scan.Unreal;

/// <summary>Engine build form (determines engine-side index scan triggering rules).</summary>
public enum EngineBuildKind
{
    /// <summary>Installed build (Epic Launcher): within MCP process, only first editor launch triggers a scan; subsequent launches reuse.</summary>
    Installed,

    /// <summary>Source build: rescans on every editor launch (source code may have changed).</summary>
    Source,
}

/// <summary>
/// Scan index store (unique within process, <see cref="Current"/>): held in memory until process exit, <b>not persisted to disk</b>.
/// <para>
/// Trigger model: Tool never directly triggers a scan — query behavior (DSL queries, etc.) and session establishment
/// add project paths to the scan queue (<see cref="EnqueueScan"/>, idempotent), consumed by a dedicated thread (<see cref="ScanLoop"/>),
/// each path internally processing via <see cref="OnEditorLaunched"/> (engine location / build form determination / rescan rules unchanged);
/// callers do not wait — queries fall back to relaxed mode when index is not ready.
/// Rescan = one root path = one scope, whole block discarded → rebuilt (not incremental).
/// </para>
/// </summary>
public sealed class ScanIndexStore
{
    /// <summary>Unique process-level instance (same pattern as DomainCatalogStore&lt;T&gt;.Current).</summary>
    public static ScanIndexStore Current { get; } = new();

    private readonly object _gate = new();

    private readonly ILogger _logger = LogManager.Instance.GetLogger<ScanIndexStore>();

    /// <summary>Root path → index data (one root path = one scope; key does not contain version — version is never an identity).</summary>
    private readonly Dictionary<string, ScanRoot> _roots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Class name → class records (multiple records possible for same name: multiple engines / engine and project same name); full rebuild after whole-scope replacement.</summary>
    private Dictionary<string, List<UeClassInfo>> _classesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Full diagnostics list from last scan (rule compile errors = missing stub list; held in memory, overwritten on next scan).</summary>
    private List<string> _lastDiagnostics = [];

    /// <summary>Snapshot of the last scan's full diagnostics list.</summary>
    public IReadOnlyList<string> LastDiagnostics
    {
        get
        {
            lock (_gate)
                return [.. _lastDiagnostics];
        }
    }

    /// <summary>Diagnostics count from last scan (0 if never scanned).</summary>
    public int LastDiagnosticsCount
    {
        get
        {
            lock (_gate)
                return _lastDiagnostics.Count;
        }
    }

    // ---- Scan queue (query behavior triggers; dedicated thread consumes) ----

    private readonly object _queueGate = new();
    private readonly Queue<string> _scanQueue = new();

    /// <summary>Currently queued / scanning paths (removed after scan completes, allowing subsequent queries to re-trigger).</summary>
    private readonly HashSet<string> _queuedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dedicated scan thread (lazily started on first enqueue; background thread, dies with process).</summary>
    private Thread? _scanThread;

    /// <summary>Count of currently queued / scanning project paths (observation only).</summary>
    public int PendingScanCount
    {
        get
        {
            lock (_queueGate)
                return _queuedPaths.Count;
        }
    }

    private ScanIndexStore()
    {
    }

    /// <summary>
    /// <summary>Snapshot of all current roots.</summary>
    public IReadOnlyList<ScanRoot> Roots
    {
        get
        {
            lock (_gate)
                return [.. _roots.Values];
        }
    }

    /// <summary>Look up index by root path.</summary>
    public ScanRoot? FindRoot(string rootPath)
    {
        lock (_gate)
            return _roots.TryGetValue(rootPath, out ScanRoot? root) ? root : null;
    }

    /// <summary>
    /// Scan queue processing function (production triggers enter queue via <see cref="EnqueueScan"/>, called by dedicated thread).
    /// <para>
    /// Caller only provides project path (.uproject file or project directory); everything else is derived internally:
    /// project path → EngineAssociation → engine location (Launcher installed / registry source build)
    /// → engineDir = {InstallLocation}\Engine (common root for plugin/source/asset scan segments)
    /// → form determination (GUID association = source build; version number association = installed build).
    /// </para>
    /// </summary>
    /// <returns>Scan result summary (with stats and diagnostic count); if parsing fails, no scan and returns skip reason.</returns>
    public string OnEditorLaunched(string projectPath)
    {
        // ---- 1. Resolve project ----
        FileInfo? projectFile = UeProjectFileManager.ResolveProjectFile(projectPath);
        if (projectFile is null)
            return $"[ScanIndex] No valid .uproject resolved at '{projectPath}', skipping scan";

        // ---- 2. Engine location and form determination ----
        string association = UeProjectFileManager.GetEngineAssociation(projectFile.Directory!) ?? "";
        UnrealEngineInstallInfo? engineInfo = UnrealEngineLocator.FindUnrealEngine(association);
        if (engineInfo is null)
            return $"[ScanIndex] EngineAssociation '{association}' could not be resolved to a local engine, skipping scan";

        EngineBuildKind kind = Guid.TryParse(association, out _)
            ? EngineBuildKind.Source
            : EngineBuildKind.Installed;

        // ---- 3. Scan root: Engine dir under install root ----
        string engineDir = Path.Combine(engineInfo.Value.InstallLocation, "Engine");
        if (!Directory.Exists(engineDir))
            return $"[ScanIndex] Engine directory does not exist ({engineDir}), skipping scan";

        // Version is a data attribute, not an identity: installed build uses association version, source build uses full Build.version string
        string version = kind == EngineBuildKind.Source ? engineInfo.Value.AppVersion : association;

        var engine = new Ue5DocumentEngine(
            new DirectoryInfo(engineDir), new DirectoryInfo(engineDir), version)
        {
            ProjectDir = projectFile.Directory
        };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var diag = new List<string>();
        bool engineCodeFromSpk = false;

        // ---- 4. Engine code scan (highest priority — signature queries depend on this data) ----
        // Installed build engine code unchanged: scanned only on first load within process, reused afterwards;
        // source build scans every time (source code may have changed).
        // SPK data (pre-shipped) is loaded separately via SpkScanDataProvider; this live-scan
        // handles project-specific code not covered by SPK.
        bool needEngineCode = kind == EngineBuildKind.Source
            || FindRoot(engine.SourceCodeDir.FullName) is null;

        if (!needEngineCode)
        {
            engineCodeFromSpk = true;
            _logger.Info(null, "[ScanIndex] Engine code index already provided by SPK (version={0})", version);
        }

        if (needEngineCode)
        {
            ReplaceRange(engine.ScanEngineCode());
        }

        // ---- 5. Project code scan (independent dispatch — does not wait for engine assets) ----
        ReplaceRange(engine.ScanProjectCode());

        // ---- 6. Engine asset scan (time-consuming but non-blocking for signature queries — code already ready) ----
        string engineAssetKey = engine.InstallDir.FullName + ":assets";
        if (FindRoot(engineAssetKey) is null)
        {
            ReplaceRange(engine.ScanEngineAssets());
        }

        // ---- 7. Project asset scan ----
        if (projectFile.Directory is not null)
        {
            string projectAssetKey = projectFile.Directory.FullName + ":assets";
            if (FindRoot(projectAssetKey) is null)
            {
                ReplaceRange(engine.ScanProjectAssets());
            }
        }

        clock.Stop();

        // Record this scan's full diagnostics
        diag.AddRange(engine.ScanDiagnostics);
        lock (_gate)
            _lastDiagnostics = diag;

        IReadOnlyList<ScanRoot> roots = Roots;
        int classCount = roots.Sum(root =>
            root.Modules.Concat(root.Plugins.SelectMany(p => p.Modules)).Sum(m => m.Classes.Count));
        int assetCount = roots.Sum(root => root.Assets.Count);

        return $"[ScanIndex] scan completed {clock.Elapsed.TotalSeconds:F1}s " +
               $"(engine code: {(engineCodeFromSpk ? "SPK" : needEngineCode ? "rescan" : "reuse")}) " +
               $"roots={roots.Count} classes={classCount} assets={assetCount} " +
               $"diagnostics={diag.Count}";
    }

    /// <summary>
    /// Enqueue entry for query-triggered scanning (Tool never directly triggers scan): adds project path to scan queue,
    /// processed asynchronously by a dedicated thread — callers do not wait (queries fall back to relaxed mode when index not ready).
    /// <para>
    /// Idempotent: repeated enqueue of same path already queued/scanning is no-op; allows re-trigger after scan completes;
    /// whether to rescan is determined by existing rules in <see cref="OnEditorLaunched"/>
    /// (installed engine only on first encounter, source build every time, project side every time).
    /// </para>
    /// </summary>
    public void EnqueueScan(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return;

        lock (_queueGate)
        {
            if (!_queuedPaths.Add(projectPath))
                return;

            _scanQueue.Enqueue(projectPath);
            _scanThread ??= StartScanThread();
            Monitor.Pulse(_queueGate);
        }
    }

    /// <summary>
    /// Dedicated scan thread main loop: wait for scan instruction → consume queue items one by one → <see cref="OnEditorLaunched"/>.
    /// Single-thread serial processing (scan itself not thread-safe; queue is naturally serialized); exceptions do not terminate thread.
    /// </summary>
    private void ScanLoop()
    {
        while (true)
        {
            string projectPath;
            lock (_queueGate)
            {
                while (_scanQueue.Count == 0)
                    Monitor.Wait(_queueGate);
                projectPath = _scanQueue.Dequeue();
            }

            try
            {
                _logger.Info(null, "{0}", OnEditorLaunched(projectPath));
            }
            catch (Exception ex)
            {
                _logger.Error(null, "[ScanIndex] Scan queue processing '{0}' exception: {1}", projectPath, ex.Message);
            }
            finally
            {
                lock (_queueGate)
                    _queuedPaths.Remove(projectPath);
            }
        }
    }

    private Thread StartScanThread()
    {
        var thread = new Thread(ScanLoop)
        {
            Name = "ScanIndexWorker",
            IsBackground = true
        };
        thread.Start();
        return thread;
    }

    /// <summary>Full-scope replacement: one root path = one scope, discard whole block → rebuild (not incremental).</summary>
    public void ReplaceRange(IEnumerable<ScanRoot> roots)
    {
        lock (_gate)
        {
            foreach (ScanRoot root in roots)
                _roots[root.RootPath] = root;

            RebuildNameIndex();
            unchecked { ++_generation; }
        }
    }

    /// <summary>Remove a root scope (callable on editor exit; data is only held until process exit anyway).</summary>
    public void RemoveRoot(string rootPath)
    {
        lock (_gate)
        {
            _roots.Remove(rootPath);
            RebuildNameIndex();
            unchecked { ++_generation; }
        }
    }

    /// <summary>Clear all indices (for in-process testing / reset; normal lifecycle relies on process exit).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _roots.Clear();
            _classesByName = new Dictionary<string, List<UeClassInfo>>(StringComparer.OrdinalIgnoreCase);
            unchecked { ++_generation; }
        }
    }

    // ---- Queries ----

    /// <summary>Find classes by name (all same-name candidates; callers filter as needed when multiple engines share names).</summary>
    public List<UeClassInfo> FindClasses(string name)
    {
        lock (_gate)
            return _classesByName.TryGetValue(name, out List<UeClassInfo>? list) ? [.. list] : [];
    }

    /// <summary>
    /// Flexible class name lookup: exact match first, then try U / A / F / I / E prefixes in order
    /// (DSL side often writes unprefixed names like "Actor", "KismetSystemLibrary").
    /// </summary>
    public List<UeClassInfo> FindClassesFlexible(string name)
    {
        List<UeClassInfo> result = FindClasses(name);
        if (result.Count > 0)
            return result;

        foreach (string prefix in new[] { "U", "A", "F", "I", "E" })
        {
            result = FindClasses(prefix + name);
            if (result.Count > 0)
                return result;
        }

        return [];
    }

    // ---- Version-scoped queries ----
    // Contract: query entries carry **project path**; version is derived internally from the project;
    // version is never passed as a parameter (avoids callers passing wrong version strings).
    // When version cannot be derived, no guessing, no interruption — falls back to global relaxed lookup, preserving original behavior.

    /// <summary>Root set change generation; version-scoped index uses this to determine staleness.</summary>
    private int _generation;

    /// <summary>Version → (generation, class name index). Rebuilds when generation mismatch.</summary>
    private readonly Dictionary<string, (int Generation, Dictionary<string, List<UeClassInfo>> Index)>
        _classIndexByScope = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Candidate name sequence: original name + U/A/F/I/E prefixes (consistent with existing flexible lookup order).</summary>
    private static IEnumerable<string> FlexibleNameCandidates(string name)
    {
        yield return name;
        foreach (string prefix in new[] { "U", "A", "F", "I", "E" })
            yield return prefix + name;
    }

    /// <summary>
    /// Project path → set of scope root paths (engine root + project root).
    /// Reuses OnEditorLaunched engine location chain: GetEngineAssociation →
    /// FindUnrealEngine → InstallLocation/Engine. Installed and source builds share the same
    /// path logic (source build resolves source engine dir via FindUnrealEngine using GUID),
    /// never depends on version string — root paths use install/source engine dir as identity.
    /// Returns empty collection on location failure; caller falls back to relaxed lookup.
    /// </summary>
    public List<string> ResolveScopePaths(string? projectPath)
    {
        var scope = new List<string>();
        if (string.IsNullOrWhiteSpace(projectPath))
            return scope;

        try
        {
            FileInfo? projectFile = UeProjectFileManager.ResolveProjectFile(projectPath);
            if (projectFile?.Directory is null || !projectFile.Exists)
                return scope;

            scope.Add(projectFile.Directory.FullName);

            string association = UeProjectFileManager.GetEngineAssociation(projectFile.Directory) ?? "";
            UnrealEngineInstallInfo? engineInfo = UnrealEngineLocator.FindUnrealEngine(association);
            if (engineInfo is not null)
            {
                string engineDir = Path.Combine(engineInfo.Value.InstallLocation, "Engine");
                if (Directory.Exists(engineDir))
                    scope.Add(engineDir);
            }
        }
        catch
        {
            // Location failure does not block query
        }

        return scope;
    }

    /// <summary>Roots within scope (narrowed by root path set — root path is the unique identity of index).</summary>
    public List<ScanRoot> RootsForScope(IEnumerable<string> rootPaths)
    {
        var wanted = new HashSet<string>(rootPaths, StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            return _roots.Values.Where(r => wanted.Contains(r.RootPath)).ToList();
    }

    /// <summary>Scope's class-name → class-records index (cached by generation; key is sorted root path set).</summary>
    private Dictionary<string, List<UeClassInfo>> ScopeClassIndex(IEnumerable<string> rootPaths)
    {
        var wanted = new HashSet<string>(rootPaths, StringComparer.OrdinalIgnoreCase);
        string key = string.Join('|', wanted.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

        lock (_gate)
        {
            if (_classIndexByScope.TryGetValue(key, out var cached) && cached.Generation == _generation)
                return cached.Index;

            var index = new Dictionary<string, List<UeClassInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (ScanRoot root in _roots.Values)
            {
                if (!wanted.Contains(root.RootPath))
                    continue;

                foreach (var module in root.Modules.Concat(root.Plugins.SelectMany(p => p.Modules)))
                {
                    foreach (UeClassInfo cls in module.Classes)
                    {
                        if (!index.TryGetValue(cls.Name, out List<UeClassInfo>? list))
                        {
                            list = [];
                            index[cls.Name] = list;
                        }
                        list.Add(cls);
                    }
                }
            }

            _classIndexByScope[key] = (_generation, index);
            return index;
        }
    }

    /// <summary>Scope key → (generation, event-name index). Key is sorted root path set.</summary>
    private readonly Dictionary<string, (int Generation, Dictionary<string, string> Index)>
        _eventIndexByScope = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>UFUNCTION specifiers for blueprint-overridable events (corresponding to FUNC_BlueprintEvent family).</summary>
    private static readonly string[] EventSpecifiers =
        ["BlueprintEvent", "BlueprintImplementableEvent", "BlueprintNativeEvent"];

    /// <summary>Same-name aliases do not overwrite (first-wins; avoids multiple same-name events overwriting each other).</summary>
    private static void AddEventAlias(Dictionary<string, string> index, string alias, string canonical)
    {
        if (!string.IsNullOrWhiteSpace(alias) && !index.ContainsKey(alias))
            index[alias] = canonical;
    }

    /// <summary>Scope's event-alias → engine-canonical-name index (cached by generation).</summary>
    private Dictionary<string, string> ScopeEventIndex(IEnumerable<string> rootPaths)
    {
        var wanted = new HashSet<string>(rootPaths, StringComparer.OrdinalIgnoreCase);
        string key = string.Join('|', wanted.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

        lock (_gate)
        {
            if (_eventIndexByScope.TryGetValue(key, out var cached) && cached.Generation == _generation)
                return cached.Index;

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScanRoot root in _roots.Values)
            {
                if (!wanted.Contains(root.RootPath))
                    continue;

                foreach (var module in root.Modules.Concat(root.Plugins.SelectMany(p => p.Modules)))
                {
                    foreach (UeClassInfo cls in module.Classes)
                    {
                        foreach (UeFunctionInfo fn in cls.Functions)
                        {
                            if (!fn.Specifiers.Any(s =>
                                    EventSpecifiers.Contains(s, StringComparer.OrdinalIgnoreCase)))
                                continue;

                            AddEventAlias(index, fn.Name, fn.Name);
                            // Receive prefix → display name (BeginPlay ← ReceiveBeginPlay)
                            if (fn.Name.StartsWith("Receive", StringComparison.OrdinalIgnoreCase))
                                AddEventAlias(index, fn.Name["Receive".Length..], fn.Name);
                            // K2_ prefix → strip prefix name
                            if (fn.Name.StartsWith("K2_", StringComparison.OrdinalIgnoreCase))
                                AddEventAlias(index, fn.Name["K2_".Length..], fn.Name);
                        }
                    }
                }
            }

            _eventIndexByScope[key] = (_generation, index);
            return index;
        }
    }

    /// <summary>
    /// Resolve caller/DSL written event name (often display name, e.g. BeginPlay) to engine canonical name
    /// (ReceiveBeginPlay). Queries by project-path-derived scope; returns null if not found,
    /// caller passes through as-is (no guessing, no interruption).
    /// <para>
    /// Note: this is an **all-engine same-name** query — any class in the engine having a same-name
    /// overridable event counts as a hit. Cannot be used to judge "whether a method is this blueprint's
    /// overridable event": `void Run()` would be misjudged as an overridable event from
    /// EditorUtilityActor / EditorUtilityObject / EditorUtilityWidget's BlueprintImplementableEvent
    /// `Run()` (For_Basic_B regression).
    /// For that scenario, use the overload with <c>parentClassName</c> (scoped to parent class chain).
    /// </para>
    /// </summary>
    public string? ResolveEventFunctionName(string? name, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        List<string> scope = ResolveScopePaths(projectPath);
        if (scope.Count == 0)
            return null;

        return ScopeEventIndex(scope).TryGetValue(name, out string? canonical) ? canonical : null;
    }

    /// <summary>
    /// Same as <see cref="ResolveEventFunctionName(string?, string?)"/>, but **only matches overridable
    /// events within parentClassName's parent class chain (including the class itself)** — avoids treating
    /// unrelated same-name events in the engine as "this blueprint's overridable event".
    /// When parent chain cannot locate any class (index not ready / class name unresolvable),
    /// falls back to all-engine query to prevent "index not ready → event node degrades to custom event" regression.
    /// </summary>
    public string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parentClassName))
            return ResolveEventFunctionName(name, projectPath);

        List<string> scope = ResolveScopePaths(projectPath);
        if (scope.Count == 0)
            return ResolveEventFunctionName(name, projectPath);

        // Parent class chain (inclusive): starting class → BaseClasses walking up; entry dedup prevents cycles
        var visited = new HashSet<UeClassInfo>();
        var queue = new Queue<UeClassInfo>(FindClassesFlexible(parentClassName, projectPath));
        var chain = new List<UeClassInfo>();
        while (queue.Count > 0)
        {
            UeClassInfo cls = queue.Dequeue();
            if (!visited.Add(cls))
                continue;

            chain.Add(cls);
            foreach (string baseDecl in cls.BaseClasses)
            {
                string baseName = baseDecl.Trim();
                int lt = baseName.IndexOf('<');
                if (lt >= 0)
                    baseName = baseName[..lt].Trim();
                if (baseName.Length == 0)
                    continue;

                foreach (UeClassInfo next in FindClassesFlexible(baseName, projectPath))
                    queue.Enqueue(next);
            }
        }

        if (chain.Count == 0)
            return ResolveEventFunctionName(name, projectPath); // Index not ready → fall back to relaxed

        // In-chain matching: engine canonical name / Receive stripped (display name) / K2_ stripped
        foreach (UeClassInfo cls in chain)
        {
            foreach (UeFunctionInfo fn in cls.Functions)
            {
                if (!fn.Specifiers.Any(s => EventSpecifiers.Contains(s, StringComparer.OrdinalIgnoreCase)))
                    continue;

                if (MatchesEventAlias(fn.Name, name))
                    return fn.Name;
            }
        }

        return null;
    }

    /// <summary>Whether the overridable event function name equals the method name, or its Receive / K2_ stripped form.</summary>
    private static bool MatchesEventAlias(string functionName, string wanted)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            return false;

        if (functionName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            return true;

        if (functionName.StartsWith("Receive", StringComparison.OrdinalIgnoreCase)
            && functionName["Receive".Length..].Equals(wanted, StringComparison.OrdinalIgnoreCase))
            return true;

        return functionName.StartsWith("K2_", StringComparison.OrdinalIgnoreCase)
               && functionName["K2_".Length..].Equals(wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Find class by <b>project-path-derived version</b> — deterministic results when same-name classes coexist across engine versions.
    /// Falls back to global lookup (<see cref="FindClassesFlexible(string)"/>) when version cannot be derived, preserving original behavior.
    /// </summary>
    public List<UeClassInfo> FindClassesFlexible(string name, string? projectPath)
    {
        List<string> scope = ResolveScopePaths(projectPath);
        if (scope.Count == 0)
            return FindClassesFlexible(name);

        Dictionary<string, List<UeClassInfo>> index = ScopeClassIndex(scope);
        foreach (string candidate in FlexibleNameCandidates(name))
        {
            if (index.TryGetValue(candidate, out List<UeClassInfo>? list) && list.Count > 0)
                return [.. list];
        }

        return [];
    }

    /// <summary>
    /// Find base class: only search among candidates with <b>same or higher scope level</b> (bounded by current class's <see cref="ScopeLevel"/>).
    /// Take traversal-order first among multiple hits; return null if not found (caller collapses, no error — MCP is not a compiler).
    /// </summary>
    public UeClassInfo? FindBaseClass(string baseName, ScopeLevel selfScope)
    {
        lock (_gate)
        {
            if (!_classesByName.TryGetValue(baseName, out List<UeClassInfo>? candidates))
                return null;

            foreach (UeClassInfo candidate in candidates)
            {
                if (candidate.Scope >= selfScope)
                    return candidate;
            }

            return null;
        }
    }

    /// <summary>
    /// Collect all available functions of a class (<b>including inheritance</b>): aggregate up the base class chain,
    /// subclass same-name takes priority; unresolved parent class collapses to UObject (stops that branch, no error).
    /// Aggregation done within this store (computed before return), not by caller.
    /// </summary>
    public List<UeFunctionInfo> CollectFunctions(UeClassInfo start)
        => Collect(start, static c => c.Functions, static f => f.Name);

    /// <summary>
    /// Reverse lookup function name to <b>all declaration sites</b> (exact match, no inheritance — which class declares it is the host).
    /// Used for owner resolution of receiverless this-calls (e.g. GetDisplayName has multiple same-name declarations in engine;
    /// host arbitration filters by call-site parameter / return value types, see ScanIndexCatalogSource).
    /// Returns index traversal order, unsorted — multi-candidate level arbitration done by caller after type filtering; sorting here
    /// would freeze the pre-type-filter order. Returns empty list when index is empty (relaxed fallback, no error).
    /// </summary>
    public List<(UeClassInfo Class, UeFunctionInfo Function)> FindFunctionDeclarations(string functionName)
    {
        var result = new List<(UeClassInfo Class, UeFunctionInfo Function)>();
        if (string.IsNullOrWhiteSpace(functionName))
            return result;

        IReadOnlyList<ScanRoot> snapshot = Roots;
        foreach (ScanRoot root in snapshot)
        {
            foreach (ModuleInfo module in EnumerateModules(root))
            {
                foreach (UeClassInfo cls in module.Classes)
                {
                    foreach (UeFunctionInfo function in cls.Functions)
                    {
                        if (function.Name.Equals(functionName, StringComparison.Ordinal))
                            result.Add((cls, function));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Collect all available properties of a class (including inheritance); rules same as <see cref="CollectFunctions"/>.</summary>
    public List<UePropertyInfo> CollectProperties(UeClassInfo start)
        => Collect(start, static c => c.Properties, static p => p.Name);

    /// <summary>
    /// Keyword search (shares same index and memory data as point queries): matches class names, function names, property names,
    /// asset names and asset export names; hit count capped by <paramref name="maxResults"/>.
    /// Consumer-generic — not bound to any domain (blueprint / material / …).
    /// </summary>
    public List<ScanSearchHit> Search(string keyword, int maxResults = 20)
    {
        var hits = new List<ScanSearchHit>();
        if (string.IsNullOrWhiteSpace(keyword))
            return hits;

        IReadOnlyList<ScanRoot> snapshot = Roots;
        foreach (ScanRoot root in snapshot)
        {
            foreach (ModuleInfo module in EnumerateModules(root))
            {
                foreach (UeClassInfo cls in module.Classes)
                {
                    if (hits.Count >= maxResults)
                        return hits;

                    if (cls.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new ScanSearchHit
                        {
                            Kind = ScanSearchHitKind.Class,
                            Name = cls.Name,
                            Module = module.Name,
                            Location = $"{cls.FilePath}:{cls.LineNumber}",
                            Scope = cls.Scope,
                        });
                    }

                    foreach (UeFunctionInfo function in cls.Functions)
                    {
                        if (hits.Count >= maxResults)
                            return hits;

                        if (function.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add(new ScanSearchHit
                            {
                                Kind = ScanSearchHitKind.Function,
                                Name = function.Name,
                                Owner = cls.Name,
                                Module = module.Name,
                                Location = $"{function.FilePath}:{function.LineNumber}",
                                Scope = cls.Scope,
                            });
                        }
                    }

                    foreach (UePropertyInfo property in cls.Properties)
                    {
                        if (hits.Count >= maxResults)
                            return hits;

                        if (property.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add(new ScanSearchHit
                            {
                                Kind = ScanSearchHitKind.Property,
                                Name = property.Name,
                                Owner = cls.Name,
                                Module = module.Name,
                                Location = $"{property.FilePath}:{property.LineNumber}",
                                Scope = cls.Scope,
                            });
                        }
                    }
                }
            }

            foreach (UeAssetEntry asset in root.Assets)
            {
                if (hits.Count >= maxResults)
                    return hits;

                bool nameHit = asset.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                bool exportHit = !nameHit && asset.Exports.Any(e =>
                    e.ObjectName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                if (!nameHit && !exportHit)
                    continue;

                hits.Add(new ScanSearchHit
                {
                    Kind = ScanSearchHitKind.Asset,
                    Name = asset.Name,
                    Location = asset.FilePath,
                    Scope = asset.Scope,
                });
            }
        }

        return hits;
    }

    private List<T> Collect<T>(UeClassInfo start, Func<UeClassInfo, IEnumerable<T>> members, Func<T, string> nameOf)
    {
        var result = new List<T>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<UeClassInfo>();

        visitedClasses.Add(start.Name);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            UeClassInfo current = queue.Dequeue();

            foreach (T member in members(current))
            {
                if (seenNames.Add(nameOf(member)))
                    result.Add(member);
            }

            foreach (string baseName in current.BaseClasses)
            {
                if (!visitedClasses.Add(baseName))
                    continue;

                UeClassInfo? parent = FindBaseClass(baseName, current.Scope);
                if (parent is not null)
                    queue.Enqueue(parent);
                // Not found → collapse (branch stops; source compiles means inheritance chain is valid; index coverage issues not MCP's to report)
            }
        }

        return result;
    }

    // ---- Internal ----

    private void RebuildNameIndex()
    {
        var index = new Dictionary<string, List<UeClassInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (ScanRoot root in _roots.Values)
        {
            foreach (ModuleInfo module in EnumerateModules(root))
            {
                foreach (UeClassInfo info in module.Classes)
                {
                    if (!index.TryGetValue(info.Name, out List<UeClassInfo>? list))
                        index[info.Name] = list = [];
                    list.Add(info);
                }
            }
        }

        _classesByName = index;
    }

    private static IEnumerable<ModuleInfo> EnumerateModules(ScanRoot root)
    {
        foreach (ModuleInfo module in root.Modules)
            yield return module;

        foreach (PluginInfo plugin in root.Plugins)
        {
            foreach (ModuleInfo module in plugin.Modules)
                yield return module;
        }
    }
}
