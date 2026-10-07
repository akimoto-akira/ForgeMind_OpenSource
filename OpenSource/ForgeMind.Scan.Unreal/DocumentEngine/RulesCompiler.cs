// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeMind.Scan.Unreal;

/// <summary>规则编译结果：全部 Build.cs 一次编译；错误整体回报（测试驱动补存根）。</summary>
public sealed class RulesCompileResult
{
    public bool Success => Errors.Count == 0;

    /// <summary>编译 / 实例化错误（含 Build.cs 相对路径），测试驱动补齐 stub 的依据。</summary>
    public List<string> Errors { get; } = [];

    /// <summary>编译成功并实例化出的模块：模块名 → 模块信息。</summary>
    public Dictionary<string, ModuleInfo> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 规则编译器：把收集到的 *.Build.cs 与本程序集（含 UnrealBuildTool 存根）一起
/// 用 Roslyn 编译为内存程序集，反射实例化 ModuleRules 派生类，产出 ModuleInfo。
/// 全程内存内完成，不落盘。
/// </summary>
public static class RulesCompiler
{
    /// <summary>编译一组 Build.cs 并产出模块信息。</summary>
    /// <param name="rulesFiles">.build.cs 完整路径列表（相对路径 → 模块归属由调用方在 ModuleInfo 上补）。</param>
    /// <param name="defaultScope">产出的 ModuleInfo 的默认层级（直属模块；插件模块由调用方改写）。</param>
    /// <param name="engineDir">引擎目录（填入目标规则的 EngineDir / RelativeEnginePath，供 Build.cs 读取）。</param>
    public static RulesCompileResult Compile(IEnumerable<string> rulesFiles, ScopeLevel defaultScope, string? engineDir = null)
    {
        var result = new RulesCompileResult();
        List<string> files = [.. rulesFiles];
        if (files.Count == 0)
            return result;

        // 1. 解析为语法树，并把 TargetRules / ReadOnlyTargetRules 类型引用改写为 dynamic
        //    （数据收集优先：Target.* 的未知成员走运行时动态兜底，不再编译失败）
        var parseOptions = new CSharpParseOptions(languageVersion: LanguageVersion.Latest);
        List<SyntaxTree> trees = [];
        foreach (string file in files)
        {
            try
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, path: file);
                trees.Add(TargetRulesToDynamicRewriter.Rewrite(tree));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{file}: 读取失败 - {ex.Message}");
            }
        }

        if (trees.Count == 0)
            return result;

        // 2. 引用：当前运行时全部程序集（含本程序集，即 UnrealBuildTool 存根所在）
        var references = new List<MetadataReference>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                continue;
            try
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
            catch
            {
                // 个别程序集不可引用（如内存程序集）——跳过
            }
        }

        // Build.cs 中的 dynamic 运算需要 Microsoft.CSharp（CS0656），显式补上
        string csharpBinderPath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "Microsoft.CSharp.dll");
        if (File.Exists(csharpBinderPath))
            references.Add(MetadataReference.CreateFromFile(csharpBinderPath));

        // 注意：ForgeMindServer 根命名空间存在同名的 UnrealBuildTool 工具类（MCP build 工具），
        // 此处必须用 global:: 限定到存根命名空间
        Type moduleRulesType = typeof(global::UnrealBuildTool.ModuleRules);
        var target = new global::UnrealBuildTool.ReadOnlyTargetRules
        {
            EngineDir = engineDir ?? "",
            RelativeEnginePath = engineDir ?? "",
        };

        // 3a. 整体编译（快路径）：全部 Build.cs 一个程序集
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);
        var overall = CSharpCompilation.Create(
            "ForgeMindRules_" + Guid.NewGuid().ToString("N"), trees, references, options);
        using var ms = new MemoryStream();
        if (overall.Emit(ms).Success)
        {
            ms.Seek(0, SeekOrigin.Begin);
            Assembly? assembly = TryLoadRulesAssembly(ms.ToArray(), result);
            if (assembly is not null)
                InstantiateModules(assembly, moduleRulesType, target, files, defaultScope, result);
            return result;
        }

        // 3b. 逐文件编译（隔离失败面）：单个文件失败只丢该文件，尽可能多地收集模块数据
        foreach (SyntaxTree tree in trees)
        {
            var single = CSharpCompilation.Create(
                "ForgeMindRules_" + Guid.NewGuid().ToString("N"), [tree], references, options);
            using var singleMs = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult singleEmit = single.Emit(singleMs);
            if (!singleEmit.Success)
            {
                foreach (Diagnostic diagnostic in singleEmit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    result.Errors.Add(diagnostic.ToString());
                continue;
            }

            singleMs.Seek(0, SeekOrigin.Begin);
            Assembly? singleAssembly = TryLoadRulesAssembly(singleMs.ToArray(), result);
            if (singleAssembly is not null)
                InstantiateModules(singleAssembly, moduleRulesType, target, files, defaultScope, result);
        }

        return result;
    }

    /// <summary>加载规则程序集；失败时记录错误并返回 null。</summary>
    private static Assembly? TryLoadRulesAssembly(byte[] image, RulesCompileResult result)
    {
        try
        {
            return Assembly.Load(image);
        }
        catch (Exception ex)
        {
            result.Errors.Add("规则程序集加载失败 - " + ex.Message);
            return null;
        }
    }

    /// <summary>反射实例化程序集内的 ModuleRules 派生类，产出 ModuleInfo。</summary>
    private static void InstantiateModules(
        Assembly assembly,
        Type moduleRulesType,
        global::UnrealBuildTool.ReadOnlyTargetRules target,
        List<string> files,
        ScopeLevel defaultScope,
        RulesCompileResult result)
    {
        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || !moduleRulesType.IsAssignableFrom(type))
                continue;

            try
            {
                // 显式数组：避免 CreateInstance(Type, bool nonPublic) 候选——
                // DynamicStubObject 有 implicit operator bool，普通形式候选优先于
                // params 展开形式，会把 target 静默转成 nonPublic=false 导致无参构造失败
                object? instance = Activator.CreateInstance(type, new object?[] { target });
                if (instance is null)
                {
                    result.Errors.Add($"{type.Name}: 实例化返回 null");
                    continue;
                }

                // 规则类名即模块名（UBT 惯例：Foo.Build.cs → class Foo → 模块 Foo）
                string moduleName = type.Name;
                string rulesFile = FindRuleFileFor(files, type.Name) ?? "";

                if (instance is global::UnrealBuildTool.ModuleRules rules)
                    rules.ModuleDirectory = Path.GetDirectoryName(rulesFile) ?? "";

                var info = new ModuleInfo
                {
                    Name = moduleName,
                    Directory = Path.GetDirectoryName(rulesFile) ?? "",
                    RulesFile = rulesFile,
                    Scope = defaultScope,
                };
                result.Modules[moduleName] = info;
            }
            catch (TargetInvocationException invocationEx) when (invocationExceptionInner(invocationEx) is not null)
            {
                Exception inner = invocationExceptionInner(invocationEx)!;
                result.Errors.Add(
                    $"{type.Name}: 实例化失败 - {inner.GetType().FullName}: {inner.Message}\n{StackSummary(inner)}");
            }
            catch (Exception ex)
            {
                result.Errors.Add(
                    $"{type.Name}: 实例化失败 - {ex.GetType().FullName}: {ex.Message}\n{StackSummary(ex)}");
            }

            static Exception? invocationExceptionInner(TargetInvocationException ex) => ex.InnerException;

            static string StackSummary(Exception exception) =>
                string.Join("\n", exception.StackTrace?.Split('\n').Take(4) ?? ["(no stack)"]);
        }
    }

    /// <summary>
    /// 把 Build.cs 语法树中对 TargetRules / ReadOnlyTargetRules 的类型引用改写为 dynamic：
    /// Build.cs 对目标规则对象的一切成员访问走运行时动态绑定（未知成员由
    /// DynamicStubObject 兜底），编译期不再因目标规则缺成员而失败。
    /// </summary>
    private sealed class TargetRulesToDynamicRewriter : CSharpSyntaxRewriter
    {
        public static SyntaxTree Rewrite(SyntaxTree tree)
        {
            SyntaxNode? rewritten = ((CSharpSyntaxNode)tree.GetRoot()).Accept(new TargetRulesToDynamicRewriter());
            return rewritten is null ? tree : tree.WithRootAndOptions(rewritten, tree.Options);
        }

        private static bool IsTargetRulesType(string name) =>
            name is "TargetRules" or "ReadOnlyTargetRules";

        /// <summary>当前构造函数中声明为 dynamic 的参数名（base 初始化器实参需要静态化）。</summary>
        private readonly HashSet<string> _dynamicParameterNames = new(StringComparer.Ordinal);

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var previous = new HashSet<string>(_dynamicParameterNames);

            SyntaxNode? result = base.VisitConstructorDeclaration(node);

            _dynamicParameterNames.Clear();
            _dynamicParameterNames.UnionWith(previous);
            return result;
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            // 扩展方法的 this 参数不能是 dynamic（CS1103），保持原样不改写
            if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.ThisKeyword)))
                return node;

            // 先让子节点完成改写（ReadOnlyTargetRules → dynamic），再识别 dynamic 参数
            SyntaxNode? result = base.VisitParameter(node);

            ParameterSyntax visited = result as ParameterSyntax ?? node;
            if (visited.Type?.ToString().Trim() == "dynamic")
                _dynamicParameterNames.Add(visited.Identifier.ValueText);

            return result;
        }

        public override SyntaxNode? VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            // base(Target) 中的 dynamic 实参在构造初始化器里禁止动态派发（CS1975）——
            // 包一层 (object) 强转后即可静态绑定到 ModuleRules(object) 构造器
            if (node.IsKind(SyntaxKind.BaseConstructorInitializer) && _dynamicParameterNames.Count > 0)
            {
                bool changed = false;
                var arguments = node.ArgumentList.Arguments.Select(argument =>
                {
                    if (argument.Expression is IdentifierNameSyntax identifier &&
                        _dynamicParameterNames.Contains(identifier.Identifier.ValueText))
                    {
                        changed = true;
                        return argument.WithExpression(
                            SyntaxFactory.CastExpression(
                                SyntaxFactory.ParseTypeName("object"), identifier).WithTriviaFrom(argument));
                    }

                    return argument;
                }).ToList();

                if (changed)
                    node = node.WithArgumentList(node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(arguments)));
            }

            return base.VisitConstructorInitializer(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // 只改写「类型位置」的引用：表达式位（如 TargetRules.TargetType.Server 的
            // 静态成员访问）保留原名，否则 dynamic 作为表达式标识符会 CS0103
            return IsTypeContext(node) && IsTargetRulesType(node.Identifier.ValueText)
                ? SyntaxFactory.ParseTypeName("dynamic").WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            if (node.Right is IdentifierNameSyntax right &&
                IsTargetRulesType(right.Identifier.ValueText) &&
                IsTypeContext(node))
            {
                return SyntaxFactory.ParseTypeName("dynamic").WithTriviaFrom(node);
            }

            return base.VisitQualifiedName(node);
        }

        /// <summary>判断节点是否处于「类型位置」（参数 / 变量 / 强转 / 泛型参数 / 数组元素 / 约束等）。</summary>
        private static bool IsTypeContext(SyntaxNode node) => node.Parent switch
        {
            ParameterSyntax parameter => parameter.Type == node,
            VariableDeclarationSyntax variable => variable.Type == node,
            CastExpressionSyntax cast => cast.Type == node,
            MethodDeclarationSyntax method => method.ReturnType == node,
            PropertyDeclarationSyntax property => property.Type == node,
            TypeArgumentListSyntax => true,
            ArrayTypeSyntax array => array.ElementType == node,
            NullableTypeSyntax nullable => nullable.ElementType == node,
            TypeConstraintSyntax => true,
            _ => false,
        };
    }

    /// <summary>按规则类型名（= 模块名）反查 .build.cs 路径。</summary>
    private static string? FindRuleFileFor(List<string> files, string moduleName)
    {
        string suffix = ".build.cs";
        return files.FirstOrDefault(f =>
            Path.GetFileName(f).EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(f)[..^suffix.Length].Equals(moduleName, StringComparison.OrdinalIgnoreCase));
    }
}
