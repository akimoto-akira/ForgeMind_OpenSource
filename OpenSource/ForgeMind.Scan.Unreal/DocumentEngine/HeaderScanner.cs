// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.RegularExpressions;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// C++ header scanner: extracts UE reflection types and members (regex-based; can be upgraded to full lexical analysis later).
/// <para>
/// Extracts: UCLASS / USTRUCT / UENUM / UINTERFACE / UDELEGATE (with Specifiers and Metadata),
/// base classes, UFUNCTION (with param name / type / default — param name is the pin name), UPROPERTY.
/// Only extracts scan facts, no inference.
/// </para>
/// </summary>
internal static partial class HeaderScanner
{
    // ---- Regex (source-generated) ----

    /// <summary>
    /// Macro parameter table content (everything inside parens of UCLASS/USTRUCT/UENUM/UINTERFACE/UFUNCTION/UPROPERTY).
    /// Three macro regexes share this constant — previously each inlined its own copy of
    /// <c>(?:[^()]|\([^()]*\))*</c>; "adding a new form" required changing all three places to not miss (Rule #8 converges to a single entry point).
    /// <para>
    /// Key: must <b>consume string literals as a whole</b>. Pure paren-pairing approaches fail on
    /// <c>DisplayName = "Load Stream Level (by Name)"</c> style metas with <b>parens inside quotes</b> —
    /// at <c>(by Name)</c> pairing fails and the parameter table never exits, so the entire UFUNCTION
    /// macro never matches and the function is entirely missed. Verified: 5.0 index therefore lacks LoadStreamLevel /
    /// UnloadStreamLevel / OpenLevel (all three DisplayNames carry "(by Name)"),
    /// while same-file GetStreamingLevel / SetGamePaused are normally indexed — DSL catalog then reports
    /// FM1002 "no matching engine function name in scan index".
    /// String branch is first, priority over single-char branch; paren branch also allows strings inside,
    /// covering the <c>meta=(...)</c> nesting level (actual depth in engine headers).
    /// Single-char branch must <b>exclude quotes</b> (<c>[^()"]</c>): otherwise the same quote can be either fully
    /// consumed by the string branch or char-by-char by the single-char branch, two paths coexisting; once post-macro matching fails
    /// (typical: <c>UENUM(meta=(ScriptName="..."))</c> is followed by <c>namespace</c> not
    /// class/struct/enum), backtracking goes exponential — verified it hangs TypeDeclRegex on EditorTutorial.h.
    /// Excluding quotes makes the two branches mutually exclusive, backtracking disappears, behavior unchanged.
    /// </para>
    /// </summary>
    private const string MacroSpecPattern = @"""[^""]*""|[^()""]|\((?:""[^""]*""|[^()""])*\)";

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineCommentRegex();

    /// <summary>UCLASS / USTRUCT / UENUM / UINTERFACE macro + immediately following type declaration.</summary>
    [GeneratedRegex(
        @"\b(?<macro>UCLASS|USTRUCT|UENUM|UINTERFACE)\s*\((?<spec>(?>(?:" + MacroSpecPattern + @")*))\)" +
        @"\s*(?:class|struct|enum)\s+(?:[A-Z][A-Z_0-9]*\s+)*(?:(?:class|struct)\s+)?(?<name>\w+)(?<base>[^;{]*)[;{]",
        RegexOptions.Singleline)]
    private static partial Regex TypeDeclRegex();

    /// <summary>
    /// Delegate declaration macro DECLARE_*DELEGATE* (non-_RetVal variant): delegate name is the <b>first</b> macro parameter.
    /// <para>
    /// Previously always used the "second macro parameter" for naming: parameterized variants
    /// (<c>DECLARE_DELEGATE_TwoParams(FExtensionHandlerDelegateInternal, AActor*, FName)</c>)
    /// have the second macro parameter as the <b>parameter type</b>, so the index got fake delegate entries
    /// named AActor / FName (no base class, no members). Verified: 5.0 index has "AAActor" 43 times, first 42 are such fake entries,
    /// the real one (baseClasses=["UObject"]) at #39 — same-name lookup is shadowed by fake entries, derivation chain judgment blocked.
    /// _RetVal variant has return type as first macro parameter; delegate name is still the second, handled by
    /// <see cref="DelegateRetValDeclRegex"/> (excluded from this regex via negative lookahead).
    /// </para>
    /// </summary>
    [GeneratedRegex(@"\bDECLARE_(?!\w*_RetVal)\w*DELEGATE\w*\s*\(\s*(?<name>\w+)")]
    private static partial Regex DelegateDeclRegex();

    /// <summary>DECLARE_*DELEGATE*_RetVal* variant: first macro parameter is return type, delegate name is second macro parameter.</summary>
    [GeneratedRegex(@"\bDECLARE_\w*DELEGATE\w*_RetVal\w*\s*\(\s*[^,()<>]+\s*,\s*(?<name>\w+)")]
    private static partial Regex DelegateRetValDeclRegex();

    /// <summary>
    /// UFUNCTION macro + immediately following function declaration (signature up to semicolon **or** opening brace of function body).
    /// <para>
    /// The closing character set must be <c>[;{]</c>: previously only <c>;</c> was recognized; the body char branch
    /// <c>[^;{}]</c> excluded <c>{</c> — UFUNCTIONs with **inline definitions in headers**
    /// (<c>UFUNCTION(...) void Foo(...){ ... }</c>) never matched, the entire function was missed.
    /// Verified: UE 5.0 SceneComponent.h:897-898 has
    /// <c>UFUNCTION(BlueprintCallable, Category="Rendering") void SetVisibility(
    /// bool bNewVisibility, bool bPropagateToChildren=false){ ... }</c>
    /// which is this form: the index had only four SetVisibility entries from Datasmith / MovieScene / UMG /
    /// VREditor; the SceneComponent one was 0 (while the file had 91 other entries).
    /// The DSL catalog then reported FM1102 "function SetVisibility does not exist".
    /// Changing to <c>[;{]</c> covers both forms; FunctionSignatureRegex then takes only
    /// the signature part; the trailing <c>{</c> does not participate in the match.
    /// </para>
    /// </summary>
    [GeneratedRegex(
        @"\bUFUNCTION\s*\((?<spec>(?:" + MacroSpecPattern + @")*)\)" +
        @"(?<body>(?:[^;{}]|\([^()]*\))*?[;{])",
        RegexOptions.Singleline)]
    private static partial Regex FunctionDeclRegex();

    /// <summary>UPROPERTY macro + immediately following property declaration (up to semicolon).</summary>
    [GeneratedRegex(
        @"\bUPROPERTY\s*\((?<spec>(?:" + MacroSpecPattern + @")*)\)" +
        @"(?<body>(?:[^;{}]|\([^()]*\))*?;)",
        RegexOptions.Singleline)]
    private static partial Regex PropertyDeclRegex();

    /// <summary>
    /// Function signature: modifiers + return type + name + parameter list.
    /// <para>
    /// Parameter list must be taken via <b>paren pairing</b> up to the closing right paren — previously used <c>[^)]*</c> which stopped
    /// at the first right paren: as long as a default value contains parens (<c>FString(TEXT("Hello"))</c>,
    /// <c>FLinearColor(0.0, 0.66, 1.0)</c>), the parameter list was truncated there, all subsequent params lost
    /// (PrintString once only had WorldContextObject / InString).
    /// Using a "non-paren char | paired parens" alternation (supports 3-level nesting, covering actual engine header depth),
    /// the two branch initial character sets are mutually exclusive, no backtracking explosion risk.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"(?<static>\bstatic\s+)?(?:virtual\s+)?(?:[A-Z][A-Z_0-9]*\s+)?(?<ret>[\w:<>&*\s]+?)\s+(?<name>\w+)\s*\((?<params>(?:[^()]|\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))*)\)")]
    private static partial Regex FunctionSignatureRegex();

    /// <summary>Property declaration: type + name (+ optional bitfield / initializer).</summary>
    [GeneratedRegex(@"^(?<type>[\w:<>&*\s]+?)\s+(?<name>\w+)\s*(?::\s*\d+)?(?:=[^;]*)?;?\s*$")]
    private static partial Regex PropertySignatureRegex();

    /// <summary>Scan all .h / .hpp headers in a directory.</summary>
    public static List<UeClassInfo> ScanAllHeaders(string directory, ScopeLevel scope)
    {
        var classes = new List<UeClassInfo>();
        if (!Directory.Exists(directory))
            return classes;

        // Single directory traversal (previously *.h / *.hpp each did their own AllDirectories recursion,
        // doubling filesystem cost). Collect all at once, then parse.
        List<string> files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(static file =>
                file.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase))
        // Stable sort: .h first, .hpp after, within-group preserves enumeration order — identical
        // output order to the old "first *.h then *.hpp" approach (candidate order for same-name classes unchanged).
            .OrderBy(static file => file.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
            return classes;

        // Per-file serial parse. Was changed to AsParallel parallel, but suffered long-tail stalls
        // on certain modules (entire module slow to return). Reverted to serial — bottleneck is
        // single-file parsing cost, not concurrency level.
        foreach (string file in files)
            ScanHeader(file, scope, classes);

        return classes;
    }

    /// <summary>Scan a single header file.</summary>
    public static void ScanHeader(string filePath, ScopeLevel scope, List<UeClassInfo> output)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            return; // Read failed header silently skipped (index does not abort)
        }

        // Preprocessing: strip comments (block comments: content replaced with spaces but newlines preserved to maintain line numbers; line comments: removed entirely)
        text = BlockCommentRegex().Replace(text, static m => BlankOutKeepNewlines(m.Value));
        text = LineCommentRegex().Replace(text, string.Empty);

        // Line number cursor: monotonically advances by position, total cost O(file length).
        // Previously each match counted newlines from the start (O(match count × file length)),
        // quadratic on large headers — this was the main cost of single-version scanning.
        int cursorIndex = 0;
        int cursorLine = 1;

        int LineNumberAt(int index)
        {
            if (index < cursorIndex)
            {
                // Position rewind (switched to a different matching loop): reset to start; at most a constant number of rewinds per file
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

        // Collect all matches (types / delegates / functions / properties), sort by occurrence position, process in order.
        // Member ownership = most recently declared host type (C++ members always appear after host declaration)
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

        // Delegate macros: normal variant (delegate name = first macro param) and _RetVal variant
        // (= second macro param) matched separately, two regexes mutually exclusive
        // (normal variant uses negative lookahead to exclude _RetVal).
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

        // Process in position order: type becomes current host (added to output); members attach to current host
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

    // ---- Internal helpers ----

    private readonly record struct SignatureMatch(string Name, bool IsStatic, string? ReturnType, string Parameters);

    private static SignatureMatch? MatchFunctionSignature(string body)
    {
        // body starts with newline / indentation after UFUNCTION macro head: if not trimmed, the regex matching from
        // string start can't find the optional static group at its empty position, so "static" gets swallowed by the
        // return type group (which includes \s) — IsStatic always false, and ret carries the "static" prefix.
        // After trimming, static is at string start, static / return type each land in their own group.
        Match match = FunctionSignatureRegex().Match(body.TrimStart());
        if (!match.Success)
            return null;

        string name = match.Groups["name"].Value;
        if (name.Length == 0 || name is "operator" or "if" or "for" or "while" or "switch" or "return")
            return null;

        return new SignatureMatch(name, match.Groups["static"].Success,
            match.Groups["ret"].Value.Trim(), match.Groups["params"].Value);
    }

    /// <summary>Parse parameter list: split by top-level commas (considering angle / round / square bracket nesting), extract type + name + default value per segment.</summary>
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

            // Take last identifier as parameter name, rest as type
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

    /// <summary>Split macro bracket contents into Specifier list and Metadata key-value pairs (meta=(Key="Value")).</summary>
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

    /// <summary>Extract base class names from inheritance clause (record whatever is found, no inference).</summary>
    private static void FillBaseClasses(string baseClause, List<string> output)
    {
        string trimmed = baseClause.Trim();
        if (!trimmed.StartsWith(":"))
            return;

        trimmed = trimmed[1..].Trim();
        foreach (string segment in trimmed.Split(','))
        {
            string item = segment.Trim();
            // Strip access modifier and virtual prefix
            item = Regex.Replace(item, @"^(public|protected|private)\s+", "");
            item = Regex.Replace(item, @"^virtual\s+", "");
            item = item.Trim();
            if (item.Length > 0)
                output.Add(item);
        }
    }

    /// <summary>
    /// Replace all non-newline characters in a text segment with spaces (newlines preserved to maintain line numbers).
    /// Used for block comment "clear but occupy position" — previously ran Regex.Replace for each comment,
    /// engine headers have hundreds of block comments, all wasted cost.
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
