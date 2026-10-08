// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeMindServer.DSL;

/// <summary>
/// Parse + block-split factory (AI Collaboration Spec.md §2.3).
/// Parses the DSL string with a single Roslyn ParseText call and splits at top-level declaration boundaries.
/// Performs syntax analysis only — SemanticModel / MetadataReference / Compilation are disabled (Core Syntax.md §11.2).
/// </summary>
public static class ParserFactory
{
    /// <summary>
    /// Domain Attribute → assetType (snake_case) mapping table.
    /// Order convention: specialized attributes first, general attributes last.
    /// </summary>
    private static readonly (string Attribute, string AssetType)[] DomainMap =
    [
        ("WidgetBlueprint",     "widget_blueprint"),
        ("AnimBlueprint",       "anim_blueprint"),
        ("MaterialInstance",    "material_instance_constant"),
        ("MaterialFunction",    "material_function"),
        ("BehaviorTree",        "behavior_tree"),
        ("Blackboard",          "blackboard"),
        ("InputAction",         "input_action"),
        ("InputMappingContext", "input_mapping_context"),
        ("NiagaraEmitter",      "niagara_emitter"),
        ("NiagaraSystem",       "niagara_system"),
        ("MetaSoundSource",     "meta_sound_source"),
        ("ChaosSolver",         "chaos_solver_actor"),
        ("GroomBinding",        "groom_binding_asset"),
        ("Landscape",           "landscape"),
        ("FoliageType",         "foliage_type"),
        ("PhysicsAsset",        "physics_asset"),
        ("PaperFlipbook",       "paper_flipbook"),
        ("PaperTileMap",        "paper_tile_map"),
        ("PaperSprite",         "paper_sprite"),
        ("LevelSequence",       "level_sequence"),
        ("Enum",                "user_defined_enum"),
        ("Struct",              "user_defined_struct"),
        ("Material",            "material"),
        ("LevelBlueprint",      "level_blueprint"),
        ("BlueprintFunctionLibrary", "blueprint_function_library"),
        ("MacroLibrary",        "blueprint_macro_library"),
        ("BlueprintInterface",  "blueprint_interface"),
        ("Blueprint",           "blueprint"),
    ];

    /// <summary>Exact-lookup dictionary for DomainMap (Ordinal, token-level exact match).</summary>
    private static readonly Dictionary<string, string> DomainLookup =
        DomainMap.ToDictionary(d => d.Attribute, d => d.AssetType, StringComparer.Ordinal);

    /// <summary>Token-level regex for block attribute names (matches only the "[Identifier" shape; no substring matching via Contains).</summary>
    private static readonly Regex AttributeNameRegex =
        new(@"\[([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>Using-alias extraction regex (Core Syntax.md §11.5): using Name = "Target";</summary>
    private static readonly Regex UsingAliasRegex =
        new(@"^\s*using\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*""([^""]+)""\s*;",
            RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Pre-parse normalization regex: domains like Material use <c>public function Name</c> pseudo-declarations
    /// (Core Syntax.md §4.1 example) — not valid C# top-level declarations. Normalizes them to
    /// <c>public class Name</c> so SplitBlocks splits at top-level declaration boundaries.
    /// Only replaces the function keyword at the declaration start line, leaving member references untouched.
    /// </summary>
    private static readonly Regex FunctionPseudoDeclRegex =
        new(@"^(\s*(?:public\s+|internal\s+|private\s+|protected\s+)?)function(\s+)",
            RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Parses the DSL string into a Roslyn SyntaxTree (the single ParseText call).</summary>
    public static SyntaxTree Parse(string code) =>
        CSharpSyntaxTree.ParseText(
            FunctionPseudoDeclRegex.Replace(code, "$1class$2"),
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithDocumentationMode(DocumentationMode.None),
            path: "DSL");

    /// <summary>
    /// Splits at top-level declaration (class/struct/enum/interface/record) boundaries,
    /// injecting using aliases and regex-detecting the assetType.
    /// Unrecognized domain → throws <see cref="UnrecognizedDomainException"/>; never silently falls back.
    /// </summary>
    public static IReadOnlyList<BlockContext> SplitBlocks(SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var root = tree.GetRoot();
        var sourceText = tree.ToString();

        // File-level using aliases (apply to all blocks)
        var fileAliases = ExtractUsingAliases(sourceText);

        var blocks = new List<BlockContext>();
        foreach (var node in root.ChildNodes())
        {
            if (node is not BaseTypeDeclarationSyntax)
                continue;

            var blockText = node.ToFullString();
            var header = ExtractHeaderText(blockText);
            var assetType = DetectAssetType(header);

            // Block-level using aliases (only effective for this block, Core Syntax.md §6.2)
            var usings = new List<UsingAlias>(fileAliases);
            usings.AddRange(ExtractUsingAliases(blockText));

            blocks.Add(new BlockContext
            {
                Root = node,
                BlockText = blockText,
                AssetType = assetType,
                Usings = usings,
                Diagnostics = new DiagnosticReporter()
            });
        }

        return InjectCrossBlockTypeAliases(blocks);
    }

    /// <summary>
    /// Cross-block type-name → Path aliases (Core Syntax.md §11.5 implicit alias):
    /// a top-level declaration <c>[Domain(Path="/Game/X")] public class/interface X</c>
    /// establishes <c>"X" → "/Game/X"</c> so later blocks can reference it by bare name —
    /// e.g. <c>[Implements(typeof(IDamageable))]</c> resolves to the interface asset path
    /// without requiring an explicit <c>using IDamageable = "/Game/...";</c> alias.
    /// Type aliases are placed before file/block-level usings so explicit aliases
    /// (registered later) take precedence (DomainTranslatorBase.RegisterAlias: last wins).
    /// </summary>
    private static IReadOnlyList<BlockContext> InjectCrossBlockTypeAliases(List<BlockContext> blocks)
    {
        var typeAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            if (block.Root is BaseTypeDeclarationSyntax typeDecl
                && ExtractBlockPath(typeDecl) is { } p
                && !string.IsNullOrEmpty(p)
                && !string.IsNullOrEmpty(typeDecl.Identifier.ValueText))
            {
                typeAliases[typeDecl.Identifier.ValueText] = p;
            }
        }

        if (typeAliases.Count == 0)
            return blocks;

        var enhanced = new List<BlockContext>(blocks.Count);
        foreach (var block in blocks)
        {
            // Type aliases first → explicit file/block usings override (last-registered wins).
            var usings = new List<UsingAlias>();
            foreach (var kv in typeAliases)
                usings.Add(new UsingAlias(kv.Key, kv.Value));
            usings.AddRange(block.Usings);

            enhanced.Add(new BlockContext
            {
                Root = block.Root,
                BlockText = block.BlockText,
                AssetType = block.AssetType,
                Usings = usings,
                Diagnostics = block.Diagnostics
            });
        }
        return enhanced;
    }

    /// <summary>
    /// Extracts the asset Path from a block's domain attribute (named <c>Path = "..."</c>
    /// or positional string literal). Returns null when the block carries no path.
    /// </summary>
    private static string? ExtractBlockPath(BaseTypeDeclarationSyntax typeDecl)
    {
        foreach (var attrList in typeDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                if (attr.ArgumentList is null)
                    continue;

                // Named parameter: Path = "..."
                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    if (arg.NameEquals is { Name: { } name }
                        && name.ToString() == "Path"
                        && arg.Expression is LiteralExpressionSyntax pathLit
                        && pathLit.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        return (string)pathLit.Token.Value!;
                    }
                }

                // Positional: [Domain("/Game/...")]
                var positional = attr.ArgumentList.Arguments
                    .FirstOrDefault(a => a.NameEquals is null && a.NameColon is null);
                if (positional?.Expression is LiteralExpressionSyntax lit
                    && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return (string)lit.Token.Value!;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Regex token-level exact match Attribute → assetType (snake_case).
    /// No substring matching; unrecognized domains → throw <see cref="UnrecognizedDomainException"/>.
    /// </summary>
    public static string DetectAssetType(string declarationText)
    {
        ArgumentNullException.ThrowIfNull(declarationText);

        var found = new List<string>();
        foreach (Match m in AttributeNameRegex.Matches(declarationText))
        {
            var name = m.Groups[1].Value;
            found.Add(name);
            if (DomainLookup.TryGetValue(name, out var assetType))
                return assetType;
        }

        var detail = found.Count > 0
            ? $"Unrecognized domain attribute: [{string.Join(", ", found)}]"
            : "Declaration block is missing a domain attribute";
        throw new UnrecognizedDomainException(detail, found);
    }

    /// <summary>Extracts all using aliases from text (Core Syntax.md §11.5).</summary>
    public static IReadOnlyList<UsingAlias> ExtractUsingAliases(string text)
    {
        var result = new List<UsingAlias>();
        foreach (Match m in UsingAliasRegex.Matches(text))
        {
            result.Add(new UsingAlias(m.Groups[1].Value, m.Groups[2].Value));
        }
        return result;
    }

    /// <summary>
    /// Extracts the declaration header text (leading attributes + modifiers + name), truncated at the
    /// first unquoted '{' or ';' — ensures member-level attributes do not participate in domain detection.
    /// </summary>
    private static string ExtractHeaderText(string blockText)
    {
        var inString = false;
        for (var i = 0; i < blockText.Length; i++)
        {
            var c = blockText[i];
            if (inString)
            {
                if (c == '\\') i++;          // Skip escaped character
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c is '{' or ';') return blockText[..i];
        }
        return blockText;
    }
}
