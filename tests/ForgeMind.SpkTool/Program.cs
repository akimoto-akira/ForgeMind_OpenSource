// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using ForgeMind.Dsl;
using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;

namespace ForgeMind.SpkTool;

/// <summary>
/// Command-line read tool for an <c>.spk</c> scan bundle. Opens the frozen engine index
/// (default: the EngineIndex plugin's embedded <c>engine.spk</c>) and answers scan queries
/// without launching UE or running the full test suite. Purpose: diagnose name-resolution /
/// arbitration issues (e.g. a same-named UFUNCTION like GetDisplayName resolving to the wrong
/// editor overload) straight from the data the DSL translator sees.
///
/// Usage:
///   ForgeMind.SpkTool [command] [args] [--spk &lt;path&gt;] [--version &lt;v&gt;]
///
/// Commands:
///   versions                         list version namespaces in the bundle
///   class   &lt;Name&gt;                   FindClassesFlexible (prefix-tolerant) + base chain
///   funcs   &lt;Class&gt;                  CollectFunctions (incl. inherited) with signatures
///   overloads &lt;Name&gt;                 every declaration site of a function FName (owner/scope/return/params/meta)
///   resolve &lt;Name&gt; [--args t1,t2] [--returns T]   TryFindFunctionOwner arbitration result
///
/// Offline DSL translation (drives the production pipeline; never contacts the bridge):
///   translate --file &lt;dsl.cs&gt; [--engine-version 5.0] [--spk &lt;path&gt;]
///
/// Args for resolve: comma-separated positional arg types; an empty slot is a wildcard
/// (e.g. --args "Vector,Vector,,HitResult"). --returns sets the expected return type.
/// </summary>
internal static class Program
{
    /// <summary>--meta: print each function's full metadata key set (besides DisplayName).</summary>
    private static bool s_showMeta;

    private static int Main(string[] args)
    {
        var opts = ParseOptions(args, out List<string> positional);

        if (positional.Count == 0 || positional[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string command = positional[0];
        string spkPath = opts.GetValueOrDefault("spk") ?? LocateDefaultSpk();
        string version = opts.GetValueOrDefault("version") ?? "5.0";
        s_showMeta = opts.ContainsKey("meta");

        if (string.IsNullOrEmpty(spkPath) || !File.Exists(spkPath))
        {
            Console.Error.WriteLine($"[spktool] bundle not found: '{spkPath}'");
            Console.Error.WriteLine("[spktool] pass one with --spk <path>");
            return 2;
        }

        // `translate` registers its own full-bundle provider (like the EngineIndex plugin) and
        // needs no version-scoped one, so it branches before the scan-query provider opens.
        if (command == "translate")
            return Translate(positional, opts, spkPath);

        using SpkScanDataProvider provider =
            ScanPackLoader.FromFileVersionScoped(spkPath, version);

        Console.WriteLine($"[spktool] bundle = {spkPath}");
        Console.WriteLine($"[spktool] version = {version}  hasIndexData = {provider.HasIndexData}");
        Console.WriteLine();

        switch (command)
        {
            case "versions":
                return Versions(spkPath);
            case "class":
                return Class(provider, Require(positional, 1, "class <Name>"));
            case "funcs":
                return Funcs(provider, Require(positional, 1, "funcs <Class>"));
            case "overloads":
                return Overloads(provider, Require(positional, 1, "overloads <Name>"));
            case "resolve":
                return Resolve(provider, positional, opts);
            case "translate":
                Console.Error.WriteLine("[spktool] internal error: translate handled earlier");
                return 2;
            default:
                Console.Error.WriteLine($"[spktool] unknown command '{command}'");
                PrintUsage();
                return 2;
        }
    }

    private static int Versions(string spkPath)
    {
        IReadOnlyList<string> vs = ScanPackLoader.ListVersions(spkPath);
        Console.WriteLine("versions in bundle:");
        foreach (string v in vs) Console.WriteLine($"  {v}");
        if (vs.Count == 0) Console.WriteLine("  (none)");
        return 0;
    }

    private static int Class(SpkScanDataProvider provider, string name)
    {
        IReadOnlyList<ScanClassView> matches = provider.FindClassesFlexible(name);
        Console.WriteLine($"FindClassesFlexible(\"{name}\") -> {matches.Count} match(es)");
        foreach (ScanClassView c in matches)
        {
            string bases = c.AllBaseClasses.Count > 0 ? string.Join(", ", c.AllBaseClasses) : "(none)";
            Console.WriteLine($"  {c.Name}  scope={c.Scope}  super={c.SuperClass ?? "(null)"}  bases=[{bases}]");
        }
        return matches.Count == 0 ? 1 : 0;
    }

    private static int Funcs(SpkScanDataProvider provider, string className)
    {
        IReadOnlyList<ScanFunctionView> funcs = provider.CollectFunctions(className);
        Console.WriteLine($"CollectFunctions(\"{className}\") -> {funcs.Count} function(s) (incl. inherited)");
        foreach (ScanFunctionView f in funcs)
            Console.WriteLine("  " + Describe(f));
        return funcs.Count == 0 ? 1 : 0;
    }

    private static int Overloads(SpkScanDataProvider provider, string name)
    {
        IReadOnlyList<ScanFunctionDeclarationView> decls = provider.FindFunctionDeclarations(name);
        Console.WriteLine($"FindFunctionDeclarations(\"{name}\") -> {decls.Count} declaration site(s)");
        foreach (ScanFunctionDeclarationView d in decls
                     .OrderByDescending(x => x.Class.Scope)
                     .ThenByDescending(x => x.Function.IsStatic))
        {
            Console.WriteLine($"  [scope={d.Class.Scope}] {d.Class.Name}");
            Console.WriteLine($"      {Describe(d.Function)}");
        }
        if (decls.Count == 0)
        {
            Console.WriteLine("  (none — TryFindFunctionOwner would return NoDeclaration for a bare call)");
            return 1;
        }
        Console.WriteLine();
        Console.WriteLine("Note: the arbitration winner is the first row above (scope desc, then static desc, then stable).");
        return 0;
    }

    private static int Resolve(SpkScanDataProvider provider, List<string> positional, Dictionary<string, string> opts)
    {
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("usage: resolve <Name> [--args t1,t2,...] [--returns TYPE]");
            return 2;
        }
        string method = positional[1];

        List<string?> argTypes = [];
        if (opts.TryGetValue("args", out string? argsCsv))
        {
            foreach (string seg in argsCsv.Split(','))
                argTypes.Add(string.IsNullOrWhiteSpace(seg) ? null : seg.Trim());
        }
        string? expectedReturn = opts.GetValueOrDefault("returns");

        ScanOwnerLookup lookup = provider.TryFindFunctionOwner(method, argTypes, expectedReturn, out string ownerClass);

        Console.WriteLine($"TryFindFunctionOwner(\"{method}\", args=[{string.Join(", ", argTypes.Select(a => a ?? "wildcard"))}], returns={expectedReturn ?? "(none)"})");
        Console.WriteLine($"  -> {lookup}  owner=\"{ownerClass ?? "(null)"}\"");
        Console.WriteLine();
        Console.WriteLine($"Same-name declarations (for reference):");
        return Overloads(provider, method);
    }

    // ---- helpers ----

    /// <summary>
    /// Offline DSL -&gt; IR. Runs the **production** pipeline, not a copy of it:
    /// <list type="bullet">
    /// <item>ScanPackLoader.FromFile + ScanDataProviderRegistry.Register — exactly what
    /// EngineIndexPlugin.OnLoad does, so translators query the same frozen index.</item>
    /// <item>DslRegistrar.RegisterAll — exactly what DslPlugin.OnLoad does, so asset types
    /// resolve through the same translator registry.</item>
    /// <item>BatchIRAssembler.Assemble — the same parse / validate / translate / assemble path
    /// the `dsl` tool runs.</item>
    /// </list>
    /// session / path / logger are all null, so nothing reaches the bridge: the IR is printed
    /// here and never applied. This adds no dry-run to the `dsl` tool — its "IR only goes to the
    /// engine" contract is untouched. Purpose is narrow and structural: spot a translator that
    /// emitted a node whose class is a basic type name (e.g. class="int"), which otherwise only
    /// surfaces after a full UE round-trip. It is not a substitute for the engine as the source
    /// of truth on correctness.
    /// </summary>
    private static int Translate(List<string> positional, Dictionary<string, string> opts, string spkPath)
    {
        string? file = opts.GetValueOrDefault("file") ?? (positional.Count > 1 ? positional[1] : null);
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            Console.Error.WriteLine("usage: translate --file <dsl.cs> [--engine-version 5.0]");
            return 2;
        }

        string dslCode = File.ReadAllText(file);
        string engineVersion = opts.GetValueOrDefault("engine-version") ?? "5.0";

        // Same wiring the plugins do at load time — see the method summary.
        SpkScanDataProvider scanProvider = ScanPackLoader.FromFile(spkPath);
        ScanDataProviderRegistry.Register(scanProvider);
        DslRegistrar.RegisterAll();

        Console.WriteLine($"[spktool] translate '{file}'  engine={engineVersion}");
        Console.WriteLine($"[spktool] scan index registered from {spkPath}");
        Console.WriteLine();

        BatchIRResult result = BatchIRAssembler.Assemble(
            dslCode, engineVersion,
            compile: null, save: null, overwrite: null,
            session: null, path: null, logger: null);

        Console.WriteLine($"diagnostics ({result.Diagnostics.Entries.Count}):  success={result.Success}");
        foreach (var d in result.Diagnostics.Entries)
            Console.WriteLine($"  {d.Severity.ToString().ToUpperInvariant()} {d.Code} @{d.Line}:{d.Column} {d.Message}");
        if (result.Diagnostics.Entries.Count == 0)
            Console.WriteLine("  (none)");

        Console.WriteLine();
        if (result.BatchIRJson is null)
        {
            Console.WriteLine("IR: none produced (batch aborted before translation finished).");
            return 1;
        }

        Console.WriteLine("Batch IR (NOT sent to the engine):");
        Console.WriteLine(result.BatchIRJson);
        return result.Success ? 0 : 1;
    }

    private static string Describe(ScanFunctionView f)
    {
        string kind = f.IsPure ? "Pure" : (f.IsStatic ? "Static" : "Member");
        string ps = f.Parameters.Count == 0
            ? "()"
            : "(" + string.Join(", ", f.Parameters.Select(p =>
                $"{(p.IsOut ? "out " : "")}{p.Type ?? "?"} {p.Name}")) + ")";
        string display = f.Metadata is not null && f.Metadata.TryGetValue("DisplayName", out string? dn) && !string.IsNullOrEmpty(dn)
            ? $"  DisplayName=\"{dn}\""
            : "";
        // --meta: dump every metadata key so autocast / ScriptMethod / WorldContext flags
        // are visible offline (these decide whether a DSL form can be resolved by query).
        string meta = s_showMeta && f.Metadata is { Count: > 0 }
            ? "  meta{" + string.Join(", ", f.Metadata.Select(kv => kv.Value.Length == 0 ? kv.Key : $"{kv.Key}={kv.Value}")) + "}"
            : "";
        return $"{f.Name}{ps} : {f.ReturnType ?? "(unknown?)"}  [{kind}]{display}{meta}  @{ShortPath(f.FilePath)}:{f.LineNumber}";
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "(no file)";
        try { return Path.GetFileName(path); } catch { return path; }
    }

    private static string Require(List<string> positional, int index, string usage)
    {
        if (positional.Count <= index)
        {
            Console.Error.WriteLine($"usage: {usage}");
            Environment.Exit(2);
        }
        return positional[index];
    }

    /// <summary>Splits options (--key value / --flag / --key=value) from positionals.</summary>
    private static Dictionary<string, string> ParseOptions(string[] args, out List<string> positional)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        positional = [];
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--"))
            {
                string key = a[2..];
                int eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    opts[key[..eq]] = key[(eq + 1)..];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    opts[key] = args[++i];
                }
                else
                {
                    opts[key] = "true";
                }
            }
            else
            {
                positional.Add(a);
            }
        }
        return opts;
    }

    /// <summary>Walks up from the executable directory to the repo root (the folder holding ForgeMind.sln),
    /// then returns the EngineIndex plugin's embedded engine.spk path.</summary>
    private static string? LocateDefaultSpk()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ForgeMind.sln")))
            {
                string spk = Path.Combine(dir.FullName,
                    "Commercial", "ForgeMind.EngineIndex", "Resources", "engine.spk");
                if (File.Exists(spk)) return spk;
                break;
            }
            dir = dir.Parent;
        }
        // Fallback: the assembly that embeds the spk, if the path moved.
        return Path.Combine(AppContext.BaseDirectory, "engine.spk");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ForgeMind.SpkTool — read an .spk scan bundle and query it (no UE needed).");
        Console.WriteLine();
        Console.WriteLine("ForgeMind.SpkTool [command] [args] [--spk <path>] [--version <v>]");
        Console.WriteLine();
        Console.WriteLine("  versions                         list version namespaces in the bundle");
        Console.WriteLine("  class   <Name>                   FindClassesFlexible + base chain");
        Console.WriteLine("  funcs   <Class>                  CollectFunctions (incl. inherited)");
        Console.WriteLine("  overloads <Name>                 every FName declaration site (owner/scope/return/params/meta)");
        Console.WriteLine("  resolve <Name> [--args t1,t2] [--returns T]   TryFindFunctionOwner arbitration");
        Console.WriteLine("  translate --file <dsl.cs> [--engine-version 5.0]   offline DSL->IR (never sends to bridge)");
        Console.WriteLine();
        Console.WriteLine("Flags: --spk <path>  --version <v>  --meta (dump every metadata key on funcs/overloads)");
        Console.WriteLine();
        Console.WriteLine("Default bundle: <repo>/Commercial/ForgeMind.EngineIndex/Resources/engine.spk, default version 5.0.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  resolve GetDisplayName --args \"\" --returns FString");
        Console.WriteLine("  overloads GetDisplayName");
        Console.WriteLine("  funcs Vector");
    }
}
