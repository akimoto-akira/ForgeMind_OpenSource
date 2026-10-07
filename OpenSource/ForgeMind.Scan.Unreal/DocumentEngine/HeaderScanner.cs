// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.RegularExpressions;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// C++ 头文件扫描器：提取 UE 反射类型与成员（正则版，后续可按需增强为完整词法分析）。
/// <para>
/// 提取内容：UCLASS / USTRUCT / UENUM / UINTERFACE / UDELEGATE（含 Specifiers 与 Metadata）、
/// 继承基类、UFUNCTION（含参数名 / 类型 / 默认值——参数名即引脚名）、UPROPERTY。
/// 只提取扫描事实，不做任何推导。
/// </para>
/// </summary>
internal static partial class HeaderScanner
{
    // ---- 正则（source-generated）----

    /// <summary>
    /// 宏参数表内容（UCLASS/USTRUCT/UENUM/UINTERFACE/UFUNCTION/UPROPERTY 括号内的
    /// 全部内容）。三处宏正则共用本常量——此前各自内联一份
    /// <c>(?:[^()]|\([^()]*\))*</c>，"新增一种形态"要改三处才不漏（规范第 8 点收束为唯一入口）。
    /// <para>
    /// 关键：必须把<b>字符串字面量当整体消费</b>。纯括号配对的写法遇到
    /// <c>DisplayName = "Load Stream Level (by Name)"</c> 这类<b>引号内含括号</b>的
    /// meta 时，会在 <c>(by Name)</c> 处配对失败、退不出参数表，于是整个 UFUNCTION
    /// 宏匹配不上、函数被整条漏扫。实测 5.0 索引因此缺 LoadStreamLevel /
    /// UnloadStreamLevel / OpenLevel（三条 DisplayName 都带 "(by Name)"），
    /// 而同文件 GetStreamingLevel / SetGamePaused 正常收录——DSL catalog 遂报
    /// FM1002「扫描索引无同名引擎函数」。
    /// 字符串分支排在最前优先于单字符分支；括号分支内部同样允许字符串，
    /// 覆盖 <c>meta=(...)</c> 这一层嵌套（引擎头文件的实际深度）。
    /// 单字符分支必须<b>排除引号</b>（<c>[^()"]</c>）：否则同一位置的引号既可被字符串
    /// 分支整体消费、又可被单字符分支逐字消费，两条路径并存；一旦宏之后的部分匹配失败
    /// （典型：<c>UENUM(meta=(ScriptName="..."))</c> 后跟的是 <c>namespace</c> 而不是
    /// class/struct/enum），回溯呈指数级——实测在 EditorTutorial.h 上直接把
    /// TypeDeclRegex 卡死。排除引号后两条分支互斥，回溯消失，行为不变。
    /// </para>
    /// </summary>
    private const string MacroSpecPattern = @"""[^""]*""|[^()""]|\((?:""[^""]*""|[^()""])*\)";

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineCommentRegex();

    /// <summary>UCLASS / USTRUCT / UENUM / UINTERFACE 宏 + 紧随的类型声明。</summary>
    [GeneratedRegex(
        @"\b(?<macro>UCLASS|USTRUCT|UENUM|UINTERFACE)\s*\((?<spec>(?>(?:" + MacroSpecPattern + @")*))\)" +
        @"\s*(?:class|struct|enum)\s+(?:[A-Z][A-Z_0-9]*\s+)*(?:(?:class|struct)\s+)?(?<name>\w+)(?<base>[^;{]*)[;{]",
        RegexOptions.Singleline)]
    private static partial Regex TypeDeclRegex();

    /// <summary>
    /// 委托声明宏 DECLARE_*DELEGATE*（非 _RetVal 变体）：委托名是<b>第一个</b>宏参数。
    /// <para>
    /// 此前一律按"第二个宏参数"取名：带参变体
    /// （<c>DECLARE_DELEGATE_TwoParams(FExtensionHandlerDelegateInternal, AActor*, FName)</c>）
    /// 的第二个宏参数是<b>形参类型</b>，于是索引里塞进名为 AActor / FName 的伪委托条目
    /// （无基类、无成员）。实测 5.0 索引里 "AActor" 共 43 条，前 42 条都是这类伪条目，
    /// 真身（baseClasses=["UObject"]）排在第 39 ——同名查找被伪条目遮蔽，派生链判定被掐断。
    /// _RetVal 变体的第一个宏参数是返回类型，委托名仍是第二个，走
    /// <see cref="DelegateRetValDeclRegex"/>（用否定前瞻把它从本正则里排除）。
    /// </para>
    /// </summary>
    [GeneratedRegex(@"\bDECLARE_(?!\w*_RetVal)\w*DELEGATE\w*\s*\(\s*(?<name>\w+)")]
    private static partial Regex DelegateDeclRegex();

    /// <summary>DECLARE_*DELEGATE*_RetVal* 变体：第一个宏参数是返回类型，委托名是第二个宏参数。</summary>
    [GeneratedRegex(@"\bDECLARE_\w*DELEGATE\w*_RetVal\w*\s*\(\s*[^,()<>]+\s*,\s*(?<name>\w+)")]
    private static partial Regex DelegateRetValDeclRegex();

    /// <summary>
    /// UFUNCTION 宏 + 紧随的函数声明（签名到分号 **或** 函数体左花括号）。
    /// <para>
    /// 收尾字符集必须是 <c>[;{]</c>：此前只认 <c>;</c>，而 body 的取字符分支
    /// <c>[^;{}]</c> 排除了 <c>{</c>——**头文件内联定义**的 UFUNCTION
    /// （<c>UFUNCTION(...) void Foo(...){ ... }</c>）永远匹配不到，整条函数漏扫。
    /// 实测 UE 5.0 SceneComponent.h:897-898 的
    /// <c>UFUNCTION(BlueprintCallable, Category="Rendering") void SetVisibility(
    /// bool bNewVisibility, bool bPropagateToChildren=false){ ... }</c>
    /// 即此形态：索引里 SetVisibility 只有 Datasmith / MovieScene / UMG /
    /// VREditor 四条，SceneComponent 那条为 0（而该文件另有 91 条条目），
    /// 于是 DSL catalog 报 FM1102「不存在函数 SetVisibility」。
    /// 改成 <c>[;{]</c> 后两种形态都能收尾；随后 FunctionSignatureRegex 只取
    /// 签名部分，尾部 <c>{</c> 不参与匹配。
    /// </para>
    /// </summary>
    [GeneratedRegex(
        @"\bUFUNCTION\s*\((?<spec>(?:" + MacroSpecPattern + @")*)\)" +
        @"(?<body>(?:[^;{}]|\([^()]*\))*?[;{])",
        RegexOptions.Singleline)]
    private static partial Regex FunctionDeclRegex();

    /// <summary>UPROPERTY 宏 + 紧随的属性声明（到分号）。</summary>
    [GeneratedRegex(
        @"\bUPROPERTY\s*\((?<spec>(?:" + MacroSpecPattern + @")*)\)" +
        @"(?<body>(?:[^;{}]|\([^()]*\))*?;)",
        RegexOptions.Singleline)]
    private static partial Regex PropertyDeclRegex();

    /// <summary>
    /// 函数签名：修饰符 + 返回类型 + 名字 + 参数表。
    /// <para>
    /// 参数表必须按<b>括号配对</b>取到收尾的右括号——此前用 <c>[^)]*</c> 会停在
    /// 第一个右括号上：默认值里只要出现圆括号（<c>FString(TEXT("Hello"))</c>、
    /// <c>FLinearColor(0.0, 0.66, 1.0)</c>），参数表就在此截断，后续参数全部丢失
    /// （PrintString 曾只剩 WorldContextObject / InString 两个参数）。
    /// 这里用"非括号字符 | 成对括号"的交替式（支持三层嵌套，覆盖引擎头文件实际深度），
    /// 两个分支首字符集互斥，无回溯爆炸风险。
    /// </para>
    /// </summary>
    [GeneratedRegex(@"(?<static>\bstatic\s+)?(?:virtual\s+)?(?:[A-Z][A-Z_0-9]*\s+)?(?<ret>[\w:<>&*\s]+?)\s+(?<name>\w+)\s*\((?<params>(?:[^()]|\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))*)\)")]
    private static partial Regex FunctionSignatureRegex();

    /// <summary>属性声明：类型 + 名字（+ 可选位域 / 初始化）。</summary>
    [GeneratedRegex(@"^(?<type>[\w:<>&*\s]+?)\s+(?<name>\w+)\s*(?::\s*\d+)?(?:=[^;]*)?;?\s*$")]
    private static partial Regex PropertySignatureRegex();

    /// <summary>扫描目录下全部 .h / .hpp 头文件。</summary>
    public static List<UeClassInfo> ScanAllHeaders(string directory, ScopeLevel scope)
    {
        var classes = new List<UeClassInfo>();
        if (!Directory.Exists(directory))
            return classes;

        // 单次目录遍历（此前 *.h / *.hpp 各做一次 AllDirectories 递归，
        // 文件系统开销翻倍），一次收集完再解析。
        List<string> files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(static file =>
                file.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase))
            // 稳定排序：.h 在前、.hpp 在后，组内保持枚举顺序——与原"先 *.h 再 *.hpp"
            // 的产出顺序完全一致（同名类的候选顺序不变）。
            .OrderBy(static file => file.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
            return classes;

        // 逐文件串行解析。曾改为 AsParallel 并行，但在个别模块上出现长尾停顿
        // （整个模块迟迟不返回），先退回串行——瓶径在单文件解析成本，不在并发度。
        foreach (string file in files)
            ScanHeader(file, scope, classes);

        return classes;
    }

    /// <summary>扫描单个头文件。</summary>
    public static void ScanHeader(string filePath, ScopeLevel scope, List<UeClassInfo> output)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            return; // 读取失败的头文件静默跳过（索引不因此中断）
        }

        // 预处理：去注释（块注释内容替换为空格但保留换行以维持行号；行注释直接删）
        text = BlockCommentRegex().Replace(text, static m => BlankOutKeepNewlines(m.Value));
        text = LineCommentRegex().Replace(text, string.Empty);

        // 行号游标：按位置单调推进，整体成本 O(文件长度)。
        // 此前每个匹配都从头数一遍换行（O(匹配数 × 文件长度)），
        // 大头文件上呈平方增长——这是单版本扫描耗时的主要来源。
        int cursorIndex = 0;
        int cursorLine = 1;

        int LineNumberAt(int index)
        {
            if (index < cursorIndex)
            {
                // 位置回退（换了一个匹配循环）：重置重来；一个文件内最多回退常数次
                cursorIndex = 0;
                cursorLine = 1;
            }

            int limit = Math.Min(index, text.Length);
            for (; cursorIndex < limit; cursorIndex++)
            {
                if (text[cursorIndex] == '\n')
                    cursorLine++;
            }

            return cursorLine;
        }

        // 收集全部匹配（类型 / 委托 / 函数 / 属性），按出现位置排序后顺序处理，
        // 成员归属 = 最近声明的宿主类型（C++ 成员总是位于宿主声明之后）
        var entries = new List<(int Index, Action<UeClassInfo?> Apply, bool isHost)>();

        foreach (Match match in TypeDeclRegex().Matches(text))
        {
            string macro = match.Groups["macro"].Value;
            string name = match.Groups["name"].Value;
            int line = LineNumberAt(match.Index);

            entries.Add((match.Index, host =>
            {
                var info = new UeClassInfo
                {
                    Name = name,
                    Kind = macro switch
                    {
                        "UCLASS" => UeTypeKind.Class,
                        "USTRUCT" => UeTypeKind.Struct,
                        "UENUM" => UeTypeKind.Enum,
                        "UINTERFACE" => UeTypeKind.Interface,
                        _ => UeTypeKind.Class,
                    },
                    FilePath = filePath,
                    LineNumber = line,
                    Scope = scope,
                };
                FillSpecifiers(match.Groups["spec"].Value, info.Specifiers, info.Metadata);
                FillBaseClasses(match.Groups["base"].Value, info.BaseClasses);
                output.Add(info);
            }, isHost: true));
        }

        // 委托宏：普通变体（委托名 = 第一个宏参数）与 _RetVal 变体（= 第二个宏参数）
        // 分开匹配，两条正则互斥（普通变体用否定前瞻排除 _RetVal）。
        foreach (Regex delegateRegex in new[] { DelegateDeclRegex(), DelegateRetValDeclRegex() })
        {
            foreach (Match match in delegateRegex.Matches(text))
            {
                int line = LineNumberAt(match.Index);
                entries.Add((match.Index, _ =>
                {
                    output.Add(new UeClassInfo
                    {
                        Name = match.Groups["name"].Value,
                        Kind = UeTypeKind.Delegate,
                        FilePath = filePath,
                        LineNumber = line,
                        Scope = scope,
                    });
                }, isHost: false));
            }
        }

        foreach (Match match in FunctionDeclRegex().Matches(text))
        {
            SignatureMatch? signature = MatchFunctionSignature(match.Groups["body"].Value);
            if (signature is null)
                continue;

            int line = LineNumberAt(match.Index);
            var function = new UeFunctionInfo
            {
                Name = signature.Value.Name,
                IsStatic = signature.Value.IsStatic,
                ReturnType = signature.Value.ReturnType,
                FilePath = filePath,
                LineNumber = line,
            };
            FillSpecifiers(match.Groups["spec"].Value, function.Specifiers, function.Metadata);
            foreach (UeParameterInfo parameter in ParseParameters(signature.Value.Parameters))
                function.Parameters.Add(parameter);

            entries.Add((match.Index, host =>
            {
                if (host is not null)
                    host.Functions.Add(function);
            }, isHost: false));
        }

        foreach (Match match in PropertyDeclRegex().Matches(text))
        {
            Match signature = PropertySignatureRegex().Match(match.Groups["body"].Value.TrimEnd());
            if (!signature.Success)
                continue;

            var property = new UePropertyInfo
            {
                Name = signature.Groups["name"].Value,
                Type = signature.Groups["type"].Value.Trim(),
                FilePath = filePath,
                LineNumber = LineNumberAt(match.Index),
            };
            FillSpecifiers(match.Groups["spec"].Value, property.Specifiers, property.Metadata);

            entries.Add((match.Index, host =>
            {
                if (host is not null)
                    host.Properties.Add(property);
            }, isHost: false));
        }

        // 按位置顺序处理：类型成为当前宿主（加入 output）；成员挂到当前宿主
        UeClassInfo? currentHost = null;
        foreach (var entry in entries.OrderBy(e => e.Index))
        {
            if (entry.isHost)
            {
                entry.Apply(currentHost);
                currentHost = output[^1];
            }
            else
            {
                entry.Apply(currentHost);
            }
        }
    }

    // ---- 内部辅助 ----

    private readonly record struct SignatureMatch(string Name, bool IsStatic, string? ReturnType, string Parameters);

    private static SignatureMatch? MatchFunctionSignature(string body)
    {
        // body 以 UFUNCTION 宏后的换行 / 缩进开头：不裁掉的话正则从串首起匹配，
        // 可选的 static 组匹配不上空位，"static" 被返回类型组（含 \s）吞掉——
        // IsStatic 全部落 false，ret 也带上 "static" 前缀。Trim 后 static
        // 位于串首，static / 返回类型各归各组。
        Match match = FunctionSignatureRegex().Match(body.TrimStart());
        if (!match.Success)
            return null;

        string name = match.Groups["name"].Value;
        if (name.Length == 0 || name is "operator" or "if" or "for" or "while" or "switch" or "return")
            return null;

        return new SignatureMatch(name, match.Groups["static"].Success,
            match.Groups["ret"].Value.Trim(), match.Groups["params"].Value);
    }

    /// <summary>解析参数表：按顶层逗号分段（考虑尖括号 / 圆括号 / 方括号嵌套），每段提取 类型 + 名字 + 默认值。</summary>
    private static List<UeParameterInfo> ParseParameters(string parameters)
    {
        var result = new List<UeParameterInfo>();
        foreach (string segment in SplitTopLevel(parameters))
        {
            string trimmed = segment.Trim();
            if (trimmed.Length == 0 || trimmed == "..." || trimmed.StartsWith("/*"))
                continue;

            string? defaultValue = null;
            int eq = IndexOfTopLevel(trimmed, '=');
            if (eq >= 0)
            {
                defaultValue = trimmed[(eq + 1)..].Trim();
                trimmed = trimmed[..eq].Trim();
            }

            // 取最后一个标识符作参数名，其余为类型
            Match nameMatch = Regex.Match(trimmed, @"(?<name>[A-Za-z_]\w*)\s*$");
            if (!nameMatch.Success)
                continue;

            string name = nameMatch.Groups["name"].Value;
            string type = trimmed[..nameMatch.Index].Trim();
            if (type.Length == 0 && defaultValue is null)
                continue;

            result.Add(new UeParameterInfo
            {
                Name = name,
                Type = type.Length > 0 ? type : null,
                DefaultValue = defaultValue,
            });
        }

        return result;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var segments = new List<string>();
        int depthAngle = 0, depthParen = 0, depthBracket = 0, start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<': depthAngle++; break;
                case '>': if (depthAngle > 0) depthAngle--; break;
                case '(': depthParen++; break;
                case ')': if (depthParen > 0) depthParen--; break;
                case '[': depthBracket++; break;
                case ']': if (depthBracket > 0) depthBracket--; break;
                case ',' when depthAngle == 0 && depthParen == 0 && depthBracket == 0:
                    segments.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }

        segments.Add(text[start..]);
        return segments;
    }

    private static int IndexOfTopLevel(string text, char c)
    {
        int depthAngle = 0, depthParen = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '<') { depthAngle++; }
            else if (ch == '>') { if (depthAngle > 0) depthAngle--; }
            else if (ch == '(') { depthParen++; }
            else if (ch == ')') { if (depthParen > 0) depthParen--; }
            else if (ch == c && depthAngle == 0 && depthParen == 0) { return i; }
        }

        return -1;
    }

    /// <summary>把宏括号内容拆成 Specifier 列表与 Metadata 键值对（meta=(Key="Value")）。</summary>
    private static void FillSpecifiers(string content, List<string> specifiers, Dictionary<string, string> metadata)
    {
        foreach (string segment in SplitTopLevel(content))
        {
            string item = segment.Trim();
            if (item.Length == 0)
                continue;

            int eq = IndexOfTopLevel(item, '=');
            if (eq > 0)
            {
                string key = item[..eq].Trim();
                metadata[key] = item[(eq + 1)..].Trim();
            }
            else
            {
                specifiers.Add(item);
            }
        }
    }

    /// <summary>从继承子句提取基类名（扫出什么记什么，不做推导）。</summary>
    private static void FillBaseClasses(string baseClause, List<string> output)
    {
        string trimmed = baseClause.Trim();
        if (!trimmed.StartsWith(":"))
            return;

        trimmed = trimmed[1..].Trim();
        foreach (string segment in trimmed.Split(','))
        {
            string item = segment.Trim();
            // 去 access 修饰符与 virtual
            item = Regex.Replace(item, @"^(public|protected|private)\s+", "");
            item = Regex.Replace(item, @"^virtual\s+", "");
            item = item.Trim();
            if (item.Length > 0)
                output.Add(item);
        }
    }

    /// <summary>
    /// 把一段文本的非换行字符全部替换为空格（换行原样保留，以维持行号）。
    /// 用于块注释的"清空但占位"——此前对每个注释都跑一次 Regex.Replace，
    /// 引擎头文件里块注释动辄上百个，全是白花的开销。
    /// </summary>
    private static string BlankOutKeepNewlines(string value)
    {
        bool anyNonNewline = false;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\n')
            {
                anyNonNewline = true;
                break;
            }
        }

        if (!anyNonNewline)
            return value;

        char[] buffer = new char[value.Length];
        for (int i = 0; i < value.Length; i++)
            buffer[i] = value[i] == '\n' ? '\n' : ' ';

        return new string(buffer);
    }
}
