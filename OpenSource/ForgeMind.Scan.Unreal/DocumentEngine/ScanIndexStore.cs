// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using ForgeMindServer;
using ForgeMindServer.Compat;
using ForgeMindServer.Engine;

namespace ForgeMind.Scan.Unreal;

/// <summary>引擎构建形态（决定引擎侧索引的扫描触发规则）。</summary>
public enum EngineBuildKind
{
    /// <summary>安装版（Epic Launcher）：MCP 进程内仅首次编辑器启动时扫一次，之后沿用。</summary>
    Installed,

    /// <summary>源码版：每次启动编辑器都重扫（源码可能被改动）。</summary>
    Source,
}

/// <summary>
/// 扫描索引存储（进程内唯一，<see cref="Current"/>）：内存持有到进程结束，<b>不落盘</b>。
/// <para>
/// 触发模型：Tool 永不直接触发扫描——查询行为（DSL 查询等）与会话建立把项目路径
/// 加入扫描队列（<see cref="EnqueueScan"/>，幂等），由专用线程（<see cref="ScanLoop"/>）逐条消费，
/// 每条路径内部走 <see cref="OnEditorLaunched"/>（引擎定位 / 形态判定 / 重扫规则不变）；
/// 调用方不等待——索引未就绪时查询走宽松回退。
/// 重扫 = 一条根路径一个范围，整块丢弃 → 重建（非增量）。
/// </para>
/// </summary>
public sealed class ScanIndexStore
{
    /// <summary>进程内唯一实例（与 DomainCatalogStore&lt;T&gt;.Current 同一模式）。</summary>
    public static ScanIndexStore Current { get; } = new();

    private readonly object _gate = new();

    private readonly ILogger _logger = LogManager.Instance.GetLogger<ScanIndexStore>();

    /// <summary>根路径 → 索引数据（一条根路径 = 一个范围；key 不含版本——版本绝不作身份标识）。</summary>
    private readonly Dictionary<string, ScanRoot> _roots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>类名 → 类记录（同名可能多条：多引擎 / 引擎与项目同名）；整范围替换后全量重建。</summary>
    private Dictionary<string, List<UeClassInfo>> _classesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近一次扫描的完整诊断清单（规则编译错误 = 缺失存根清单；进程内持有，下次扫描覆盖）。</summary>
    private List<string> _lastDiagnostics = [];

    /// <summary>最近一次扫描的完整诊断清单快照。</summary>
    public IReadOnlyList<string> LastDiagnostics
    {
        get
        {
            lock (_gate)
                return [.. _lastDiagnostics];
        }
    }

    /// <summary>最近一次扫描的诊断条数（未扫描过 = 0）。</summary>
    public int LastDiagnosticsCount
    {
        get
        {
            lock (_gate)
                return _lastDiagnostics.Count;
        }
    }

    // ---- 扫描队列（查询行为触发；专用线程消费）----

    private readonly object _queueGate = new();
    private readonly Queue<string> _scanQueue = new();

    /// <summary>排队中 / 扫描中的路径（扫描完成后移除，允许后续查询再次触发）。</summary>
    private readonly HashSet<string> _queuedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>专用扫描线程（首次入队时惰性拉起；后台线程，随进程消亡）。</summary>
    private Thread? _scanThread;

    /// <summary>当前排队 / 扫描中的项目路径数（观察用）。</summary>
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
    /// <summary>当前全部根的快照。</summary>
    public IReadOnlyList<ScanRoot> Roots
    {
        get
        {
            lock (_gate)
                return [.. _roots.Values];
        }
    }

    /// <summary>按根路径查索引。</summary>
    public ScanRoot? FindRoot(string rootPath)
    {
        lock (_gate)
            return _roots.TryGetValue(rootPath, out ScanRoot? root) ? root : null;
    }

    /// <summary>
    /// 扫描队列的处理函数（生产触发点经 <see cref="EnqueueScan"/> 入队，由专用线程调用）。
    /// <para>
    /// 调用方只提供项目路径（.uproject 文件或项目目录）；其余全部内部推导：
    /// 项目路径 → EngineAssociation → 引擎定位（Launcher 安装版 / 注册表源码构建）
    /// → engineDir = {InstallLocation}\Engine（插件 / 源码 / 资产三段扫描的共同根）
    /// → 形态判定（GUID 关联 = 源码版，版本号关联 = 安装版）。
    /// </para>
    /// </summary>
    /// <returns>扫描结果概要（含统计与诊断计数）；解析失败时不扫描，返回跳过原因。</returns>
    public string OnEditorLaunched(string projectPath)
    {
        // ---- 1. 解析项目 ----
        FileInfo? projectFile = UeProjectFileManager.ResolveProjectFile(projectPath);
        if (projectFile is null)
            return $"[ScanIndex] 未在 '{projectPath}' 解析到有效 .uproject，跳过扫描";

        // ---- 2. 引擎定位与形态判定 ----
        string association = UeProjectFileManager.GetEngineAssociation(projectFile.Directory!) ?? "";
        UnrealEngineInstallInfo? engineInfo = UnrealEngineLocator.FindUnrealEngine(association);
        if (engineInfo is null)
            return $"[ScanIndex] EngineAssociation '{association}' 未能定位到本机引擎，跳过扫描";

        EngineBuildKind kind = Guid.TryParse(association, out _)
            ? EngineBuildKind.Source
            : EngineBuildKind.Installed;

        // ---- 3. 扫描根：安装根下的 Engine 目录 ----
        string engineDir = Path.Combine(engineInfo.Value.InstallLocation, "Engine");
        if (!Directory.Exists(engineDir))
            return $"[ScanIndex] 引擎目录不存在（{engineDir}），跳过扫描";

        // 版本仅作数据属性，不作身份标识：安装版用关联版本号，源码版用 Build.version 全串
        string version = kind == EngineBuildKind.Source ? engineInfo.Value.AppVersion : association;

        var engine = new Ue5DocumentEngine(
            new DirectoryInfo(engineDir), new DirectoryInfo(engineDir), version)
        {
            ProjectDir = projectFile.Directory
        };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var diag = new List<string>();
        bool engineCodeFromSpk = false;

        // ---- 4. 引擎代码扫描（优先级最高——签名查询依赖此数据）----
        // 安装版引擎代码不变：进程内仅首次扫，后续沿用；
        // 源码版每次都扫（源码可能被改动）。
        // SPK data (pre-shipped) is loaded separately via SpkScanDataProvider; this live-scan
        // handles project-specific code not covered by SPK.
        bool needEngineCode = kind == EngineBuildKind.Source
            || FindRoot(engine.SourceCodeDir.FullName) is null;

        if (!needEngineCode)
        {
            engineCodeFromSpk = true;
            _logger.Info(null, "[ScanIndex] 引擎代码索引已由 SPK 提供 (version={0})", version);
        }

        if (needEngineCode)
        {
            ReplaceRange(engine.ScanEngineCode());
        }

        // ---- 5. 项目代码扫描（独立投递——不等引擎资产）----
        ReplaceRange(engine.ScanProjectCode());

        // ---- 6. 引擎资产扫描（耗时但不阻塞签名查询——代码已就绪）----
        string engineAssetKey = engine.InstallDir.FullName + ":assets";
        if (FindRoot(engineAssetKey) is null)
        {
            ReplaceRange(engine.ScanEngineAssets());
        }

        // ---- 7. 项目资产扫描 ----
        if (projectFile.Directory is not null)
        {
            string projectAssetKey = projectFile.Directory.FullName + ":assets";
            if (FindRoot(projectAssetKey) is null)
            {
                ReplaceRange(engine.ScanProjectAssets());
            }
        }

        clock.Stop();

        // 记录本次扫描的完整诊断
        diag.AddRange(engine.ScanDiagnostics);
        lock (_gate)
            _lastDiagnostics = diag;

        IReadOnlyList<ScanRoot> roots = Roots;
        int classCount = roots.Sum(root =>
            root.Modules.Concat(root.Plugins.SelectMany(p => p.Modules)).Sum(m => m.Classes.Count));
        int assetCount = roots.Sum(root => root.Assets.Count);

        return $"[ScanIndex] 扫描完成 {clock.Elapsed.TotalSeconds:F1}s " +
               $"(引擎代码: {(engineCodeFromSpk ? "SPK" : needEngineCode ? "重扫" : "沿用")}) " +
               $"roots={roots.Count} classes={classCount} assets={assetCount} " +
               $"diagnostics={diag.Count}";
    }

    /// <summary>
    /// 查询行为触发的入队口（Tool 永不直接触发扫描）：把项目路径加入扫描队列，
    /// 由专用线程异步处理——调用方不等待（索引未就绪时查询走宽松回退）。
    /// <para>
    /// 幂等：同一路径排队 / 扫描中时重复入队无效；扫描完成后允许再次触发，
    /// 是否重扫由 <see cref="OnEditorLaunched"/> 的既有规则决定
    /// （安装版引擎仅首次、源码版每次、项目侧每次）。
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
    /// 专用扫描线程主循环：等待扫描指令 → 逐条消费队列 → <see cref="OnEditorLaunched"/>。
    /// 单线程串行处理（扫描本身非线程安全，队列天然串行化）；异常不中断线程。
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
                _logger.Error(null, "[ScanIndex] 扫描队列处理 '{0}' 异常：{1}", projectPath, ex.Message);
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

    /// <summary>整范围替换：一条根路径一个范围，整块丢弃 → 重建（非增量）。</summary>
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

    /// <summary>移除一个根范围（编辑器退出时可调用；数据本来也只持有到进程结束）。</summary>
    public void RemoveRoot(string rootPath)
    {
        lock (_gate)
        {
            _roots.Remove(rootPath);
            RebuildNameIndex();
            unchecked { ++_generation; }
        }
    }

    /// <summary>清空全部索引（进程内测试 / 重置用；正常生命周期靠进程退出消亡）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _roots.Clear();
            _classesByName = new Dictionary<string, List<UeClassInfo>>(StringComparer.OrdinalIgnoreCase);
            unchecked { ++_generation; }
        }
    }

    // ---- 查询 ----

    /// <summary>按名字查类（所有同名候选；多引擎同名时由调用方按需过滤）。</summary>
    public List<UeClassInfo> FindClasses(string name)
    {
        lock (_gate)
            return _classesByName.TryGetValue(name, out List<UeClassInfo>? list) ? [.. list] : [];
    }

    /// <summary>
    /// 灵活类名查找：先精确匹配，未命中再依次尝试 U / A / F / I / E 前缀
    /// （DSL 侧常写「Actor」「KismetSystemLibrary」这类无前缀名）。
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

    // ---- 版本作用域查询 ----
    // 契约：查询入口携带**项目路径**，版本由内部从项目推定；
    // 版本永远不作为入参传入（避免调用方传错版本串）。
    // 推定不出版本时不猜测、不中断——退回全局宽松查找，保持原有行为。

    /// <summary>根集合变更代次；版本作用域索引据此判断是否失效。</summary>
    private int _generation;

    /// <summary>版本 →（代次, 类名索引）。代次不符即重建。</summary>
    private readonly Dictionary<string, (int Generation, Dictionary<string, List<UeClassInfo>> Index)>
        _classIndexByScope = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>候选名序列：原名 + U/A/F/I/E 前缀（与既有灵活查找顺序一致）。</summary>
    private static IEnumerable<string> FlexibleNameCandidates(string name)
    {
        yield return name;
        foreach (string prefix in new[] { "U", "A", "F", "I", "E" })
            yield return prefix + name;
    }

    /// <summary>
    /// 项目路径 → 作用域根路径集合（引擎根 + 项目根）。
    /// 复用 OnEditorLaunched 的引擎定位链路：GetEngineAssociation →
    /// FindUnrealEngine → InstallLocation/Engine。安装版与源码版走同一条
    /// 路径逻辑（源码版靠 GUID 由 FindUnrealEngine 解析出源码引擎目录），
    /// 全程不依赖版本串——根路径以安装/源码引擎目录为身份。
    /// 定位失败时返回空集合，由调用方宽松回退。
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
            // 定位失败不阻断查询
        }

        return scope;
    }

    /// <summary>作用域内的根（按根路径集合收窄——根路径是索引的唯一身份）。</summary>
    public List<ScanRoot> RootsForScope(IEnumerable<string> rootPaths)
    {
        var wanted = new HashSet<string>(rootPaths, StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            return _roots.Values.Where(r => wanted.Contains(r.RootPath)).ToList();
    }

    /// <summary>作用域的「类名 → 类记录」索引（按代次缓存，键为排序后的根路径集合）。</summary>
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

    /// <summary>作用域键 →（代次, 事件名索引）。键为排序后的根路径集合。</summary>
    private readonly Dictionary<string, (int Generation, Dictionary<string, string> Index)>
        _eventIndexByScope = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>可蓝图重载事件的 UFUNCTION specifier（对应 FUNC_BlueprintEvent 家族）。</summary>
    private static readonly string[] EventSpecifiers =
        ["BlueprintEvent", "BlueprintImplementableEvent", "BlueprintNativeEvent"];

    /// <summary>同名别名不覆盖（先到先得，避免多类同名互相打翻）。</summary>
    private static void AddEventAlias(Dictionary<string, string> index, string alias, string canonical)
    {
        if (!string.IsNullOrWhiteSpace(alias) && !index.ContainsKey(alias))
            index[alias] = canonical;
    }

    /// <summary>作用域的「事件别名 → 引擎真名」索引（按代次缓存）。</summary>
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
                            // Receive 前缀 → 显示名（BeginPlay ← ReceiveBeginPlay）
                            if (fn.Name.StartsWith("Receive", StringComparison.OrdinalIgnoreCase))
                                AddEventAlias(index, fn.Name["Receive".Length..], fn.Name);
                            // K2_ 前缀 → 去前缀名
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
    /// 调用方/DSL 写的事件名（常是显示名，如 BeginPlay）→ 引擎真名
    /// （ReceiveBeginPlay）。按项目路径确定的作用域查；查不到返回 null，
    /// 由调用方原样放行（不猜测、不中断）。
    /// <para>
    /// 注意：这是**全引擎同名**查询——只要引擎里任意类有同名可重载事件就命中。
    /// 判定"某个方法是不是本蓝图的可重载事件"时不能用它：`void Run()` 会被
    /// EditorUtilityActor / EditorUtilityObject / EditorUtilityWidget 的
    /// BlueprintImplementableEvent `Run()` 误判成可重载事件（For_Basic_B 回归）。
    /// 那种场景请用带 <c>parentClassName</c> 的重载（限定在父类链内）。
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
    /// 同 <see cref="ResolveEventFunctionName(string?, string?)"/>，但**只在
    /// parentClassName 的父类链（含该类自身）内**匹配可重载事件——避免把引擎里
    /// 毫不相干的同名事件当成"本蓝图可重载事件"。
    /// 父类链一个类都定位不到（索引未就绪 / 类名无法解析）时退回全引擎查询，
    /// 防止"索引没准备好 → 事件节点退化成自定义事件"的回归。
    /// </summary>
    public string? ResolveEventFunctionName(string? name, string? projectPath, string? parentClassName)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parentClassName))
            return ResolveEventFunctionName(name, projectPath);

        List<string> scope = ResolveScopePaths(projectPath);
        if (scope.Count == 0)
            return ResolveEventFunctionName(name, projectPath);

        // 父类链（含自身）：起始类 → BaseClasses 逐级上溯；按条目去重防环
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
            return ResolveEventFunctionName(name, projectPath); // 索引未就绪 → 退回宽松

        // 链内匹配：引擎真名 / Receive 去前缀（显示名）/ K2_ 去前缀
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

    /// <summary>可重载事件函数名是否等于该方法名，或其 Receive / K2_ 去前缀形式。</summary>
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
    /// 按<b>项目路径对应版本</b>查类——同名类在多引擎版本并存时结果确定。
    /// 推定不出版本则退回全局查找（<see cref="FindClassesFlexible(string)"/>）保持原行为。
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
    /// 父类查找：只在<b>同级或更大标号</b>的候选中找（以当前类的 <see cref="ScopeLevel"/> 为界）。
    /// 多个命中时取遍历序第一个；查不到返回 null（由调用方塌陷，不报错——MCP 不是编译器）。
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
    /// 收集一个类的全部可用函数（<b>含继承</b>）：沿基类链向上聚合，
    /// 子类同名优先；查不到的父类塌陷为 UObject（停止该支，不报错）。
    /// 聚合在本存储内完成（返回前计算），调用方不算。
    /// </summary>
    public List<UeFunctionInfo> CollectFunctions(UeClassInfo start)
        => Collect(start, static c => c.Functions, static f => f.Name);

    /// <summary>
    /// 函数名反查<b>全部声明处</b>（精确匹配，不查继承——声明在哪个类，宿主就是哪个类）。
    /// 无接收者的 this 调用归属判定用（如 GetDisplayName 在引擎里有多处同名声明，
    /// 宿主裁决按调用点参数 / 返回值类型过滤后进行，见 ScanIndexCatalogSource）。
    /// 返回索引遍历序，不排序——多候选的层级裁决由调用方在类型过滤后做，此处排序
    /// 只会把类型过滤前的顺序钉死。索引为空返回空列表（宽松回退，不报错）。
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

    /// <summary>收集一个类的全部可用属性（含继承），规则同 <see cref="CollectFunctions"/>。</summary>
    public List<UePropertyInfo> CollectProperties(UeClassInfo start)
        => Collect(start, static c => c.Properties, static p => p.Name);

    /// <summary>
    /// 关键词检索（与点查共用同一索引、同一份内存数据）：匹配类名、函数名、属性名、
    /// 资产名与资产导出名；命中数量以 <paramref name="maxResults"/> 截断。
    /// 消费方通用——不与任何域（蓝图 / 材质 / …）绑死。
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
                // 查不到 → 塌陷（该支停止；源码可编译则继承链合法，索引覆盖问题不归 MCP 报错）
            }
        }

        return result;
    }

    // ---- 内部 ----

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
