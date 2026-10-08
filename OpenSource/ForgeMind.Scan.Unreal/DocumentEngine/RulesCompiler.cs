// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeMind.Scan.Unreal;

/// <summary>Rule compilation result: all Build.cs compiled at once; errors reported holistically (test-driven stub completion).</summary>
public sealed class RulesCompileResult
{
    public bool Success => Errors.Count == 0;

    /// <summary>Compilation / instantiation errors (incl. Build.cs relative path); basis for test-driven stub completion.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Successfully compiled and instantiated modules: module name → module info.</summary>
    public Dictionary<string, ModuleInfo> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Rule compiler: collects *.Build.cs together with this assembly (including UnrealBuildTool stubs) and
/// compiles them into an in-memory assembly via Roslyn, reflects-instantiates ModuleRules derived classes, producing ModuleInfo.
/// Entirely in-memory, no disk writes.
/// </summary>
public static class RulesCompiler
{
    /// <summary>Compile a set of Build.cs and produce module info.</summary>
    /// <param name="rulesFiles">.build.cs full path list (relative path → module ownership filled by caller on ModuleInfo).</param>
    /// <param name="defaultScope">Default scope for produced ModuleInfo (direct modules; plugin modules rewritten by caller).</param>
    /// <param name="engineDir">Engine directory (populated into target rule's EngineDir / RelativeEnginePath for Build.cs to read).</param>
    public static RulesCompileResult Compile(IEnumerable<string> rulesFiles, ScopeLevel defaultScope, string? engineDir = null)
    {
        var result = new RulesCompileResult();
        List<string> files = [.. rulesFiles];
        if (files.Count == 0)
            return result;

        // 1. Parse into syntax trees, and rewrite TargetRules / ReadOnlyTargetRules type references to dynamic
        //    (data collection priority: unknown members of Target.* fall back to runtime dynamic dispatch, no longer failing compilation)
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
                result.Errors.Add($"{file}: read failed - {ex.Message}");
            }
        }

        if (trees.Count == 0)
            return result;

        // 2. References: all assemblies in current runtime (incl. this assembly, where UnrealBuildTool stubs reside)
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
                // Some assemblies cannot be referenced (e.g. in-memory assemblies) — skip
            }
        }

        // dynamic operations in Build.cs require Microsoft.CSharp (CS0656); explicitly add it
        string csharpBinderPath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "Microsoft.CSharp.dll");
        if (File.Exists(csharpBinderPath))
            references.Add(MetadataReference.CreateFromFile(csharpBinderPath));

        // Note: ForgeMindServer root namespace has a same-named UnrealBuildTool tool class (MCP build tool);
        // must use global:: qualifier to target the stub namespace here
        Type moduleRulesType = typeof(global::UnrealBuildTool.ModuleRules);
        var target = new global::UnrealBuildTool.ReadOnlyTargetRules
        {
            EngineDir = engineDir ?? "",
            RelativeEnginePath = engineDir ?? "",
        };

        // 3a. Overall compilation (fast path): all Build.cs in one assembly
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

        // 3b. Per-file compilation (isolate failure scope): single file failure loses only that file, collect module data as much as possible
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

    /// <summary>Load rules assembly; on failure record error and return null.</summary>
    private static Assembly? TryLoadRulesAssembly(byte[] image, RulesCompileResult result)
    {
        try
        {
            // Load through the dedicated rules context, not Assembly.Load(byte[]): the default
            // context cannot resolve the stub assembly (ForgeMind.Scan.Unreal) when it runs as a
            // plugin (the dll sits in a plugin directory, not beside the host binary), and every
            // ModuleRules-derived type then fails to load (ReflectionTypeLoadException).
            return RulesAssemblyContext.Current.LoadFromStream(new MemoryStream(image));
        }
        catch (Exception ex)
        {
            result.Errors.Add("rules assembly load failed - " + ex.Message);
            return null;
        }
    }

    /// <summary>Reflect-instantiate ModuleRules derived types in assembly, producing ModuleInfo.</summary>
    private static void InstantiateModules(
        Assembly assembly,
        Type moduleRulesType,
        global::UnrealBuildTool.ReadOnlyTargetRules target,
        List<string> files,
        ScopeLevel defaultScope,
        RulesCompileResult result)
    {
        foreach (Type type in EnumerateRuleTypes(assembly, result))
        {
            if (type.IsAbstract || !moduleRulesType.IsAssignableFrom(type))
                continue;

            try
            {
                // Explicit array: avoid CreateInstance(Type, bool nonPublic) candidate —
                // DynamicStubObject has implicit operator bool; the normal form candidate takes priority
                // over params expansion, silently converting target to nonPublic=false causing parameterless ctor failure
                object? instance = Activator.CreateInstance(type, new object?[] { target });
                if (instance is null)
                {
                    result.Errors.Add($"{type.Name}: instantiation returned null");
                    continue;
                }

                // Rule class name = module name (UBT convention: Foo.Build.cs → class Foo → module Foo)
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
                    $"{type.Name}: instantiation failed - {inner.GetType().FullName}: {inner.Message}\n{StackSummary(inner)}");
            }
            catch (Exception ex)
            {
                result.Errors.Add(
                    $"{type.Name}: instantiation failed - {ex.GetType().FullName}: {ex.Message}\n{StackSummary(ex)}");
            }

            static Exception? invocationExceptionInner(TargetInvocationException ex) => ex.InnerException;

            static string StackSummary(Exception exception) =>
                string.Join("\n", exception.StackTrace?.Split('\n').Take(4) ?? ["(no stack)"]);
        }
    }

    /// <summary>
    /// Rewrite TargetRules / ReadOnlyTargetRules type references in Build.cs syntax tree to dynamic:
    /// all member accesses on the target rules object go through runtime dynamic binding
    /// (unknown members fall back to DynamicStubObject), no longer failing compilation due to missing target rule members.
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

        /// <summary>Parameter names declared as dynamic in the current constructor (base initializer arguments need static binding).</summary>
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
            // Extension method 'this' parameter cannot be dynamic (CS1103); keep it unchanged
            if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.ThisKeyword)))
                return node;

            // Let child nodes finish rewriting first (ReadOnlyTargetRules → dynamic), then identify dynamic params
            SyntaxNode? result = base.VisitParameter(node);

            ParameterSyntax visited = result as ParameterSyntax ?? node;
            if (visited.Type?.ToString().Trim() == "dynamic")
                _dynamicParameterNames.Add(visited.Identifier.ValueText);

            return result;
        }

        public override SyntaxNode? VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            // dynamic arguments in base(Target) are not allowed for dynamic dispatch in constructor initializer (CS1975) —
            // wrapping with (object) cast enables static binding to ModuleRules(object) ctor
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
            // Only rewrite references in "type position": expression position (e.g. static member access of
            // TargetRules.TargetType.Server) keeps the original name, otherwise dynamic as expression identifier causes CS0103
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

        /// <summary>Determine whether a node is in "type position" (parameter / variable / cast / type argument / array element / constraint etc.).</summary>
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

    /// <summary>
    /// Isolated load context for the Roslyn-compiled rules assemblies. It resolves every
    /// dependency from the runtime's already-loaded assemblies: the compile-time references are
    /// exactly those assemblies (AppDomain.CurrentDomain.GetAssemblies()), so each reference
    /// resolves back to the very instance the metadata was taken from, keeping type identity
    /// (the ModuleRules base class and DynamicStubObject binder targets) unified across the
    /// assembly boundary.
    /// </summary>
    private sealed class RulesAssemblyContext : AssemblyLoadContext
    {
        public static RulesAssemblyContext Current { get; } = new();

        private RulesAssemblyContext() : base(name: "ForgeMindRules")
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (name == null)
                return null;

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    return loaded;
            }

            return null; // Fall back to default resolution
        }
    }

    /// <summary>
    /// Enumerates the ModuleRules-derived types within a rules assembly. A partial type load
    /// failure (some dependency not resolvable) is degraded to the successfully loaded types —
    /// aligned with the plugin loader's discovery mode — instead of propagating the exception
    /// and killing the whole scan.
    /// </summary>
    private static IEnumerable<Type> EnumerateRuleTypes(Assembly assembly, RulesCompileResult result)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            result.Errors.Add(
                "rules assembly partial type load failed - " + (ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message));
            types = ex.Types;
        }

        foreach (Type? type in types)
        {
            if (type is not null)
                yield return type;
        }
    }

    /// <summary>Reverse lookup .build.cs path by rule type name (= module name).</summary>
    private static string? FindRuleFileFor(List<string> files, string moduleName)
    {
        string suffix = ".build.cs";
        return files.FirstOrDefault(f =>
            Path.GetFileName(f).EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(f)[..^suffix.Length].Equals(moduleName, StringComparison.OrdinalIgnoreCase));
    }
}
