// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan.Abstractions;
using ForgeMind.Scan.Unreal;
using ForgeMindServer.Abstractions;


namespace ForgeMind.EngineScanner;

/// <summary>
/// 扫描索引目录数据源：把文档引擎的扫描索引（ScanIndexStore）接入
/// <see cref="DomainCatalogProvider"/>，翻译器的签名 / 引脚查询由此落地。
/// <para>
/// 函数查询走<b>继承聚合</b>（子类同名优先；查不到的父类塌陷，不报错）；
/// 索引为空（尚未扫描 / 未接入）时 <see cref="TryLoad"/> 返回 false，翻译器回退宽松模式。
/// <see cref="TryLoad"/> 同时是查询行为的扫描触发点：项目路径入扫描队列（幂等，异步处理）。
/// </para>
/// </summary>
internal class ScanSignatureCatalog
{
    /// <summary>单例。</summary>
    public static ScanSignatureCatalog Instance { get; } = new();

    private static IScanDataProvider Self => ScanIndexDataProvider.Instance!;

    protected ScanSignatureCatalog()
    {
    }

    /// <inheritdoc />
    public bool HasIndexData => Self.Roots.Count > 0;

    /// <inheritdoc />
    public bool TryFindFunction(string owner, string method, int positionalCount,
                                IReadOnlyList<string>? namedArguments, out ScanFunctionSignature? signature)
    {
        signature = null;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(method))
            return false;

        // 类查找（含 U / A / F / I / E 前缀兼容）+ 继承聚合成员中定位函数。
        // 函数名未命中时重试 "K2_" 前缀（引擎惯例：K2_GetActorLocation 等蓝图
        // 包装函数的真实 FName 带 K2_ 前缀，编辑器显示名不带）。
        foreach (ScanClassView cls in ScanDataProvider.Current
                     .FindClassesFlexible(owner))
        {
            var functions = Self.CollectFunctions(cls.Name);
            ScanFunctionView? function = functions
                .FirstOrDefault(f => f.Name.Equals(method, StringComparison.OrdinalIgnoreCase));
            if (function is null)
                function = functions
                    .FirstOrDefault(f => f.Name.Equals("K2_" + method, StringComparison.OrdinalIgnoreCase));
            if (function is null)
                continue;

            // 匹配规则：位置实参不超参数总数（省略尾部默认参数）；具名实参名必须存在
            if (positionalCount > function.Parameters.Count)
                continue;

            HashSet<string> parameterNames = function.Parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (namedArguments is not null && namedArguments.Any(n => !parameterNames.Contains(n)))
                continue;

            signature = new ScanFunctionSignature(
                cls.Name,
                function.Name,
                function.IsStatic,
                function.Parameters
                    .Select(p => new ScanParameterSignature(p.Name, p.Type))
                    .ToList())
            {
                // BlueprintPure = 蓝图纯函数（无 exec 引脚，不占执行链）——
                // 翻译器据此决定调用节点是否串入执行链（VInterpTo 纯 vs
                // CreateSaveGameObject impure，Save_Create_B 的 entry → create
                // 执行链即此判定）。
                IsPure = function.IsPure,
            };
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public ScanOwnerLookup TryFindFunctionOwner(string method, IReadOnlyList<string?>? argumentTypes,
                                                    string? expectedReturnType, out string ownerClass)
    {
        ownerClass = null!;

        if (string.IsNullOrWhiteSpace(method))
            return ScanOwnerLookup.NoDeclaration;

        // 精确反查全部声明处；函数名未命中时重试 "K2_" 前缀（引擎蓝图包装函数惯例）。
        var declarations = Self.FindFunctionDeclarations(method);
        if (declarations.Count == 0)
            declarations = Self.FindFunctionDeclarations("K2_" + method);
        if (declarations.Count == 0)
        {
            // 索引里没有同名声明：不是引擎/库函数——宽松回退（本蓝图自有函数，
            // 如无引擎对应的占位方法 GetCurrent）；与"有声明但不匹配"严格区分。
            return ScanOwnerLookup.NoDeclaration;
        }

        // 调用点类型匹配（先定调用点类型，再过滤同名声明）：
        // 位置实参数不超参数总数（省略尾部默认参数）；已知实参类型逐一兼容；
        // 期望返回类型兼容。未知类型（null）作通配。
        // 静态库首参 WorldContextObject 由引擎自动连 self，不占位置实参——
        // 与翻译器的引脚对位（ExtractLiteralProperties / ExpectedArgType）同一约定：
        // 位置实参从它之后开始对位（PrintString("x") 的 FString 对 InString，
        // 不对 WorldContextObject）。逐声明判定（同名声明的首参未必都是 WorldContext）。
        int argCount = argumentTypes?.Count ?? 0;
        var candidates = declarations.Where(d =>
        {
            var ps = d.Function.Parameters;
            int offset = ps.Count > 0 &&
                         ps[0].Name.Equals("WorldContextObject", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            if (argCount + offset > ps.Count)
                return false;
            for (int i = 0; i < argCount; i++)
            {
                if (!IsTypeCompatible(argumentTypes![i], ps[i + offset].Type))
                    return false;
            }
            return IsTypeCompatible(d.Function.ReturnType, expectedReturnType);
        }).ToList();

        if (candidates.Count == 0)
            return ScanOwnerLookup.NoTypeMatch;

        // 多候选裁决：层级降序（引擎直属模块最优先）。OrderByDescending 稳定——
        // 同层保持索引序，不再依赖 List.Sort（同 Scope 并列时结果任意）。
        ownerClass = candidates
            .OrderByDescending(d => (int)d.Class.Scope)
            .First().Class.Name;
        return ScanOwnerLookup.Resolved;
    }

/// <summary>
/// UE 语义的类型兼容判定（实参 → 形参 / 声明返回值 → 期望返回值）：
/// 任一侧未知（null / 空）通放；基名一致即兼容（const T&amp; 接受 T 值）。
/// 基名不同时按以下顺序裁决：
/// <list type="number">
/// <item>引擎蓝图自动转换（编辑器连线时自动插 Conv_ 节点，均带 BlueprintAutocast
/// 标记）：数值族内部互转、数值 → FString（实测 int / float 连到 InString 时编辑器
/// 自动插"转换为字符串（整型）/（浮点）"）、FString → FName（Conv_StringToName，
/// KismetStringLibrary.h:85 "To Name (String)"——关卡流送
/// LoadStreamLevel("Sub", ...) 的 LevelName(FName) 即此场景）。</item>
/// <item>模板 / 容器类型（TArray&lt;...&gt; 等）要求基名一致，不做派生推断。</item>
/// <item>任一侧是判定不认识的类型（DSL 类型名、索引未收录的结构）通放——
/// 不认识就无法裁决，不猜测、不阻断。</item>
/// <item>两侧都认识时按扫描索引基类链上溯判定派生兼容（AActor* → const UObject*）；
/// FString / FText / FName 等已知值类型族互不兼容——蓝图引脚间无隐式转换
///（唯一例外：上一条的 FString → FName 引擎自动转换）。</item>
/// </list>
/// </summary>
private static bool IsTypeCompatible(string? from, string? to)
{
    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        return true;

    (string fromBase, _, _) = DecomposeType(from);
    (string toBase, _, _) = DecomposeType(to);
    fromBase = CanonicalType(fromBase);
    toBase = CanonicalType(toBase);
    if (fromBase.Length == 0 || toBase.Length == 0)
        return true;
    if (fromBase.Equals(toBase, StringComparison.Ordinal))
        return true;

    if (IsNumericType(fromBase) &&
        (IsNumericType(toBase) || toBase.Equals("FString", StringComparison.Ordinal)))
        return true;

    // FString → FName：引擎 BlueprintAutocast 自动转换（Conv_StringToName）。
    // 单向豁免——FName → FString 虽有 Conv_NameToString 但未有调用点暴露，不放行。
    if (fromBase.Equals("FString", StringComparison.Ordinal) &&
        toBase.Equals("FName", StringComparison.Ordinal))
        return true;

    if (fromBase.Contains('<') || toBase.Contains('<'))
        return false;

        if (!IsKnownType(fromBase) || !IsKnownType(toBase))
            return true;

        return IsClassDerivedFrom(fromBase, toBase);
    }

    /// <summary>DSL / 蓝图显示类型名 → 引擎类型名（位置实参类型来自 DSL 声明文本，如 Vector / string）。</summary>
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.Ordinal)
    {
        ["Vector"] = "FVector",
        ["Vector2D"] = "FVector2D",
        ["Rotator"] = "FRotator",
        ["Transform"] = "FTransform",
        ["Quat"] = "FQuat",
        ["string"] = "FString",
        ["name"] = "FName",
        ["text"] = "FText",
        ["int"] = "int32",
        ["long"] = "int64",
        ["short"] = "int16",
        ["byte"] = "uint8",
    };

    /// <summary>数值族（引擎自动转换互通：byte / int / int64 / float / double）。</summary>
    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64",
        "float", "double",
    };

    /// <summary>
    /// 已知非 UObject 值类型族（蓝图引脚间无隐式转换，基名一致才兼容）。
    /// 判定"认不认识"用——未收录其中的类型按"未知"通放。
    /// </summary>
    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "FString", "FText", "FName", "FVector", "FVector2D", "FVector4", "FIntPoint",
        "FRotator", "FQuat", "FTransform", "FLinearColor", "FColor", "FGuid", "FDateTime",
        "bool",
    };

    private static string CanonicalType(string baseName)
        => TypeAliases.TryGetValue(baseName, out string? canonical) ? canonical : baseName;

    private static bool IsNumericType(string baseName) => NumericTypes.Contains(baseName);

    /// <summary>该基名是否为判定认识的类型：数值族 / 已知值类型族 / 扫描索引收录的类（含结构）。</summary>
    private static bool IsKnownType(string baseName)
        => IsNumericType(baseName)
           || ValueTypes.Contains(baseName)
           || Self.FindClassesFlexible(baseName).Count > 0;

    /// <summary>拆类型串 → (基名, 是否指针, 是否引用)：去尾部 * / &amp; 标记与 const 修饰，压缩空白。</summary>
    private static (string Base, bool IsPointer, bool IsRef) DecomposeType(string type)
    {
        string t = type.Trim();
        bool isPointer = false, isRef = false;
        while (t.EndsWith("*"))
        {
            isPointer = true;
            t = t[..^1].TrimEnd();
        }
        while (t.EndsWith("&"))
        {
            isRef = true;
            t = t[..^1].TrimEnd();
        }
        if (t.StartsWith("const ", StringComparison.Ordinal))
            t = t["const ".Length..].Trim();
        t = string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (t, isPointer, isRef);
    }

    /// <summary>
    /// from 类是否沿基类链派生自 to 类（扫描索引；U/A/F/I/E 前缀灵活定位起点，
    /// 基类子句截掉模板参数后逐级上溯，已访问集合防环）。
    /// </summary>
    private static bool IsClassDerivedFrom(string from, string to)
    {
        var store = ScanDataProvider.Current;
        // 已访问集合按<b>条目本身</b>（引用身份）去重，不能按类名：索引里同名条目可能有多条
        // （跨模块的重复声明等），按名去重时第一条会把其余全部吃掉——该条若是无基类的空条目，
        // 派生链就地断掉（AActor 曾因索引里排在前头的伪条目被判"不派生自 UObject"）。
        var visited = new HashSet<ScanClassView>();
        var queue = new Queue<ScanClassView>(store.FindClassesFlexible(from));
        while (queue.Count > 0)
        {
            var cls = queue.Dequeue();
            if (!visited.Add(cls))
                continue;

            foreach (string baseDecl in cls.AllBaseClasses)
            {
                string baseName = baseDecl.Trim();
                int lt = baseName.IndexOf('<');
                if (lt >= 0)
                    baseName = baseName[..lt].Trim();

                if (baseName.Equals(to, StringComparison.Ordinal))
                    return true;

                foreach (var next in store.FindClassesFlexible(baseName))
                    queue.Enqueue(next);
            }
        }

        return false;
    }
}
