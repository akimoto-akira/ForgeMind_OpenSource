// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// A class as seen by contract consumers (function C and any other plugin).
/// <para>A view type rather than the scanner's own model class: the scan model belongs to
/// the scanning implementation, and a public contract must not expose it.</para>
/// </summary>
/// <param name="Name">Type name including its prefix, e.g. <c>AActor</c>.</param>
/// <param name="SuperClass">Declared base type name; null or empty when none is declared.</param>
/// <param name="BaseClasses">Every declared base type, in declaration order. Needed because a
/// type may list several (interfaces plus a superclass): derivation checks walk the whole list,
/// so a single base name would silently truncate the chain.</param>
public sealed record ScanClassView(
    string Name,
    string? SuperClass,
    IReadOnlyList<string>? BaseClasses = null,
    int ScopeRank = 0)
{
    /// <summary>
    /// Ordinal rank of this type's proximity to the engine itself — higher wins when several
    /// candidates match a call. Exposed as a plain number so the contract stays engine-agnostic:
    /// the provider decides what the ordering means, consumers only compare.
    /// </summary>
    public int Scope => ScopeRank;

    /// <summary>
    /// Every declared base type, never null. Falls back to <see cref="SuperClass"/> when the
    /// provider reported only the first one.
    /// </summary>
    public IReadOnlyList<string> AllBaseClasses =>
        BaseClasses is { Count: > 0 } bases ? bases
        : string.IsNullOrWhiteSpace(SuperClass) ? [] : [SuperClass];
}

/// <summary>
/// A UPROPERTY declared on a scanned class. Only scan facts (name + declared type text); no
/// inference. Consumed by the DSL member-assignment lowering, which needs the declared component
/// type of an inherited engine member (e.g. <c>CharacterMovement</c> → UCharacterMovementComponent)
/// so a blueprint can write a property of a component it already inherits without re-declaring it.
/// </summary>
/// <param name="Name">Property name as declared (pin / member-access name).</param>
/// <param name="Type">Declared type text; may be empty when the scan did not record it.</param>
public sealed record ScanPropertyView(string Name, string? Type);

/// <summary>A single parameter (pin) of a function.</summary>
/// <param name="Name">Parameter name, which is also the pin name.</param>
/// <param name="Type">Declared type; null when the index does not carry it.</param>
/// <summary>One parameter (pin) as stored in the scan index.</summary>
/// <param name="Name">Parameter name, which is also the pin name.</param>
/// <param name="Type">Declared type; null when unknown.</param>
/// <param name="IsOut">Whether it is an out parameter — a non-const C++ reference ("T&amp;"), which becomes an output pin in Blueprint.</param>
public sealed record ScanParameterView(string Name, string? Type, bool IsOut = false);

/// <summary>
/// Shared UE "does a value of type <c>from</c> fit a pin of type <c>to</c>?" predicate — the
/// <b>single convergence point</b> every <see cref="IScanDataProvider"/> arbitrates through (spec 8).
/// <para>The judgment used to be written twice with different strength: the live-scan provider
/// knew the engine's BlueprintAutocast conversions and walked the base-class chain, while the
/// contract copy used for the frozen index only compared bare names and applied prefix
/// stripping <b>before</b> removing <c>const</c> / <c>&amp;</c> / <c>*</c> — so a declared
/// parameter "const FString&amp;" never compared equal to a return type "FString", and a
/// derived argument (AActor) never fit a base parameter (UObject).</para>
/// <para>Engine facts encoded here (verified against UE 5.0.3):</para>
/// <list type="bullet">
/// <item><c>const T&amp;</c> / <c>T*</c> accepts a <c>T</c> value — markers are erased before
/// comparison.</item>
/// <item><c>TSubclassOf&lt;X&gt;</c> pins take an X object reference and vice versa (the editor
/// auto-inserts GetClass), so both sides unwrap to X.</item>
/// <item>Numeric family members inter-convert, and numeric / X → FString is a
/// <c>BlueprintAutocast</c>-tagged <c>Conv_XToString</c> the compiler inserts on the wire.</item>
/// <item>FString ↔ FName auto-cast both ways (<c>Conv_StringToName</c> / <c>Conv_NameToString</c>).</item>
/// <item>Unrecognized types pass through as wildcards — unknown cannot be judged, no guessing,
/// no blocking.</item>
/// </list>
/// </summary>
public static class ScanTypeCompatibility
{
    /// <summary>DSL / blueprint display type name → engine type name (positional arg types come
    /// from DSL declaration text, e.g. Vector / string).</summary>
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

    /// <summary>Numeric family (engine auto-conversion interop: byte / int / int64 / float / double).</summary>
    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64",
        "float", "double",
    };

    /// <summary>
    /// Known non-UObject value-type families — apart from the autocast arms below there is no
    /// implicit conversion between blueprint pins, so their bare names must match.
    /// </summary>
    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "FString", "FText", "FName", "FVector", "FVector2D", "FVector4", "FIntPoint",
        "FRotator", "FQuat", "FTransform", "FLinearColor", "FColor", "FGuid", "FDateTime",
        "bool",
    };

    /// <summary>
    /// Structs / value types whose X → FString conversion is a BlueprintAutocast-tagged
    /// UFUNCTION on UKismetStringLibrary (see KismetStringLibrary.h L20-77).
    /// </summary>
    private static readonly HashSet<string> AutocastToStringTypes = new(StringComparer.Ordinal)
    {
        "FVector", "FVector2D", "FIntPoint", "FIntVector", "FRotator", "FTransform",
        "FLinearColor", "FColor", "FName", "FText", "bool", "FGuid",
    };

    /// <summary>
    /// Reduces a declared or inferred type text to a comparable bare name:
    /// <c>UPARAM(...)</c> decorators removed, <c>TSubclassOf&lt;X&gt;</c> unwrapped, <c>const</c> /
    /// <c>class</c> / <c>struct</c> keywords and trailing <c>*</c> / <c>&amp;</c> markers removed,
    /// DSL aliases canonicalised, then the C++ reflection class prefix (U / A) stripped.
    /// <para>Order matters: the markers must be removed <b>before</b> the prefix, otherwise
    /// "const FString&amp;" keeps its F and can never equal "FString".</para>
    /// </summary>
    /// <returns>The bare name, or "" for null / whitespace input (callers treat "" as a wildcard).</returns>
    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "";

        string t = StripUParamDecorator(type.Trim());
        t = UnwrapTSubclassOf(t);

        if (t.StartsWith("class ", StringComparison.OrdinalIgnoreCase))
            t = t["class ".Length..].Trim();
        else if (t.StartsWith("struct ", StringComparison.OrdinalIgnoreCase))
            t = t["struct ".Length..].Trim();

        while (t.StartsWith("const ", StringComparison.OrdinalIgnoreCase))
            t = t["const ".Length..].Trim();

        t = t.TrimEnd('*', '&', ' ').Trim();
        if (t.Length == 0)
            return "";

        if (TypeAliases.TryGetValue(t, out string? canonical))
            t = canonical;

        return StripEngineClassPrefix(t);
    }

    /// <summary>
    /// Canonical compatibility test used by provider arbitration (spec 8). Either side being
    /// unknown (null / empty) passes; otherwise the bare names must match, or be related by a
    /// documented engine auto-conversion, or by inheritance.
    /// </summary>
    /// <param name="from">Inferred / produced type at the call site.</param>
    /// <param name="to">Declared type of the target pin.</param>
    /// <param name="findClasses">
    /// Prefix-tolerant class lookup used to walk base-class chains. When null the inheritance
    /// arm degrades to false, i.e. bare-name matching only.
    /// </param>
    public static bool IsCompatible(string? from, string? to,
        Func<string, IReadOnlyList<ScanClassView>>? findClasses = null)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return true;

        string a = Normalize(from);
        string b = Normalize(to);
        if (a.Length == 0 || b.Length == 0)
            return true;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        // Engine BlueprintAutocast family: numeric -> numeric / numeric -> FString.
        if (NumericTypes.Contains(a)
            && (NumericTypes.Contains(b) || b.Equals("FString", StringComparison.Ordinal)))
            return true;

        // FString -> FName (Conv_StringToName) and FName -> FString (Conv_NameToString).
        if (a.Equals("FString", StringComparison.Ordinal) && b.Equals("FName", StringComparison.Ordinal))
            return true;
        if (a.Equals("FName", StringComparison.Ordinal) && b.Equals("FString", StringComparison.Ordinal))
            return true;

        // X -> FString through the matching Conv_XToString autocast.
        if (b.Equals("FString", StringComparison.Ordinal) && AutocastToStringTypes.Contains(a))
            return true;

        // Template / container types: no derived inference, names must match.
        if (from.Contains('<') || to.Contains('<'))
            return false;

        // Unrecognized on either side: cannot be judged -> pass through.
        if (!IsKnownType(a, findClasses) || !IsKnownType(b, findClasses))
            return true;

        return IsClassDerivedFrom(a, b, findClasses);
    }

    /// <summary>
    /// Whether a declaration can be called from a runtime (non editor-utility) blueprint.
    /// <para>UE rejects a call to a UFUNCTION declared in an <c>Editor</c> module — the
/// compiler reports "Cannot use editor function ... in this runtime blueprint. Only valid in
/// editor utility blueprints and Blutility." The frozen index carries no explicit editor flag,
/// so the declaring file path is the authoritative record: <c>Engine/Source/Editor/</c>
/// (and the plugin / project equivalents) is editor-only, <c>Source/Runtime/</c> is not.</para>
    /// <para>Null / empty paths are treated as runtime: an unjudgeable path must never
    /// block a declaration.</para>
    /// </summary>
    public static bool IsEditorOnlyPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        string normalized = filePath.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "Source", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[i + 1], "Editor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the base name is recognised: numeric family / known value-type family
    /// / a class or struct present in the index.</summary>
    private static bool IsKnownType(string baseName, Func<string, IReadOnlyList<ScanClassView>>? findClasses)
        => NumericTypes.Contains(baseName)
           || ValueTypes.Contains(baseName)
           || (findClasses is not null && findClasses(baseName).Count > 0);

    /// <summary>
    /// Walks the declared base-class chain from <paramref name="derived"/> looking for
    /// <paramref name="baseClass"/>. The visited set deduplicates by entry identity — the index
    /// may hold several entries under one name, and name-based dedup would let a baseless
    /// pseudo-entry swallow the real chain.
    /// </summary>
    private static bool IsClassDerivedFrom(string derived, string baseClass,
        Func<string, IReadOnlyList<ScanClassView>>? findClasses)
    {
        if (findClasses is null)
            return false;

        var visited = new HashSet<ScanClassView>();
        var queue = new Queue<ScanClassView>(findClasses(derived));
        while (queue.Count > 0)
        {
            ScanClassView cls = queue.Dequeue();
            if (!visited.Add(cls))
                continue;

            foreach (string baseDecl in cls.AllBaseClasses)
            {
                string baseName = baseDecl.Trim();
                int lt = baseName.IndexOf('<');
                if (lt >= 0)
                    baseName = baseName[..lt].Trim();

                if (Normalize(baseName).Equals(baseClass, StringComparison.OrdinalIgnoreCase))
                    return true;

                foreach (ScanClassView next in findClasses(baseName))
                    queue.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>Strip the C++ reflection class prefix (U / A) — F is a struct prefix and is kept.</summary>
    private static string StripEngineClassPrefix(string name)
        => name.Length > 1 && (name[0] == 'U' || name[0] == 'A') && char.IsUpper(name[1])
            ? name[1..]
            : name;

    /// <summary>
    /// Remove <c>UPARAM(...)</c> parameter decorators from a declared type text.
    /// <para>The scanner keeps the declaration verbatim, and engine headers glue the decorator
    /// in front of the type — <c>AActor::AddComponentByClass</c> declares its first parameter as
    /// <c>UPARAM(meta = (AllowAbstract = "false")) TSubclassOf&lt;UActorComponent&gt; Class</c>
    /// (@Actor.h:1556). Left in place the "bare name" of that parameter is the whole decorator
    /// text, so no argument can ever fit it: host-class arbitration reports "the scan index has
    /// same-named declarations but none matches the call site" (FM1002) or the caller degrades
    /// to positional <c>__argN</c> pins instead of the engine pin name "Class" (run 20261022_1641,
    /// AddCompBy_B). Removal is paren-balanced because the decorator carries a nested list.</para>
    /// </summary>
    private static string StripUParamDecorator(string type)
    {
        const string Marker = "UPARAM";
        for (int start = type.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
             start >= 0;
             start = type.IndexOf(Marker, StringComparison.OrdinalIgnoreCase))
        {
            int open = start + Marker.Length;
            if (open >= type.Length || type[open] != '(')
                return type;

            int depth = 0;
            int close = -1;
            for (int i = open; i < type.Length; i++)
            {
                if (type[i] == '(')
                    depth++;
                else if (type[i] == ')' && --depth == 0)
                {
                    close = i;
                    break;
                }
            }
            if (close < 0)
                return type;

            type = (type[..start] + type[(close + 1)..]).Trim();
        }

        return type;
    }

    /// <summary>
    /// Unwrap a <c>TSubclassOf&lt;X&gt;</c> type string to its inner class name (dropping any
    /// <c>class</c> / <c>const</c> keyword and trailing pointer / reference markers inside).
    /// Returns the input unchanged when it is not a TSubclassOf&lt;...&gt; form.
    /// </summary>
    private static string UnwrapTSubclassOf(string type)
    {
        const string Prefix = "TSubclassOf<";
        if (type.Length <= Prefix.Length + 1)
            return type;
        if (!type.StartsWith(Prefix, StringComparison.Ordinal) || !type.EndsWith(">", StringComparison.Ordinal))
            return type;

        string inner = type[Prefix.Length..^1].Trim();
        if (inner.StartsWith("class ", StringComparison.OrdinalIgnoreCase))
            inner = inner["class ".Length..].Trim();
        if (inner.StartsWith("struct ", StringComparison.OrdinalIgnoreCase))
            inner = inner["struct ".Length..].Trim();
        if (inner.StartsWith("const ", StringComparison.OrdinalIgnoreCase))
            inner = inner["const ".Length..].Trim();

        inner = inner.TrimEnd('*', '&').Trim();
        return inner.Length == 0 ? type : inner;
    }
}

/// <summary>
/// Shared overload arbitration for same-named UFUNCTION declarations (spec 8 single
/// convergence point): both scan providers must rank candidates identically, otherwise the
/// frozen-index path and the live-scan path disagree about which function a bare call binds to.
/// </summary>
public static class ScanOwnerArbiter
{
    /// <summary>
    /// Picks the reachable declaring class from candidates that all match a call site.
    /// Tiers, highest first:
    /// <list type="number">
    /// <item>module kind — runtime-module declarations outrank editor-module ones, because
    /// UE rejects a call into an editor module from a runtime blueprint;</item>
    /// <item>class scope descending — engine-direct modules beat plugin-level matches;</item>
    /// <item>static descending — a bare-name call has no receiver, so a static library entry is
    /// the reachable form;</item>
    /// <item>stable enumeration order of <paramref name="candidates"/>.</item>
    /// </list>
    /// Sorting is applied tier by tier and <c>OrderBy</c> / <c>ThenBy</c> are documented as
    /// stable, so earlier tiers win over later ones.
    /// </summary>
    /// <returns>The winning class name, or "" when <paramref name="candidates"/> is empty.</returns>
    public static string PickBest(
        IReadOnlyList<(string ClassName, int Scope, ScanFunctionView Fn)> candidates)
    {
        if (candidates.Count == 0)
            return "";

        return candidates
            .OrderBy(c => ScanTypeCompatibility.IsEditorOnlyPath(c.Fn.FilePath) ? 1 : 0)
            .ThenByDescending(c => c.Scope)
            .ThenByDescending(c => c.Fn.IsStatic)
            .First().ClassName;
    }
}

/// <summary>A function as seen by contract consumers.</summary>
/// <param name="Name">Function name.</param>
/// <param name="IsStatic">Whether it is a static function.</param>
/// <param name="IsPure">Whether it is a pure function (no exec pin).</param>
/// <param name="Parameters">Parameters in pin order.</param>
/// <param name="FilePath">Declaring source file; consumers need it to point at the declaration.</param>
/// <param name="LineNumber">Declaring line number.</param>
/// <param name="ReturnType">Declared return type; null when the index does not carry it, which
/// consumers must treat as a wildcard during type matching rather than as "void".</param>
/// <param name="Metadata">UFUNCTION metadata (keys from <c>meta=(Key="Value")</c> expanded to
/// individual entries, plus any top-level <c>Key="Value"</c> specifiers like <c>Category</c>).
/// Providers must decompose a raw <c>meta</c> string into its inner key-value pairs before handing
/// it here — consumers treat this dictionary as already flattened. The name-resolution query
/// channel reads exactly one key from this dictionary: <c>DisplayName</c> (per user rule "query
/// only two criteria: DisplayName and direct function name"). Null or empty when the index does not carry
/// metadata; consumers must fall back to <see cref="Name"/>-only matching.</param>
public sealed record ScanFunctionView(
    string Name,
    bool IsStatic,
    bool IsPure,
    IReadOnlyList<ScanParameterView> Parameters,
    string FilePath,
    int LineNumber,
    string? ReturnType = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>Friendly alias UE exposes on a UFunction. Per user rule "query uses only two criteria:
    /// DisplayName and direct function name", the query channel only recognises these two bases --
    /// <see cref="Name"/> (the FName / direct function name) or <c>DisplayName</c> (the
    /// Blueprint-visible display name). ScriptName / ScriptMethod are engine-internal
    /// script bindings, not part of the queryable pair, so they are deliberately excluded.
    /// Kept on the contract so every provider answers identically (spec 8 single
    /// convergence point).</summary>
    public static readonly string[] FriendlyNameKeys = ["DisplayName"];

    /// <summary>True when this function's <see cref="Metadata"/> carries <paramref name="name"/>
    /// under <see cref="FriendlyNameKeys"/> (i.e. as its <c>DisplayName</c>). Ordinal
    /// (case-sensitive) comparison matches UE's own GetMetaData behaviour. Empty
    /// <paramref name="name"/> never matches.</summary>
    public bool MatchesFriendlyName(string name)
    {
        if (string.IsNullOrEmpty(name) || Metadata is null || Metadata.Count == 0)
            return false;
        foreach (string key in FriendlyNameKeys)
        {
            if (Metadata.TryGetValue(key, out var val)
                && string.Equals(val, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the caller's positional argument types align with this function's declared
    /// parameters by bare-name compatibility (both sides stripped of the leading F/U/A/I/E
    /// prefix, then Ordinal-ignore-case compared). Either side null / empty acts as a wildcard
    /// (unknown DSL type / unindexed struct): the candidate is kept rather than rejected, so
    /// the query never silently mis-narrows on an unknown token. A leading
    /// <c>WorldContextObject</c> parameter is skipped (engine auto-wires it, not counted as a
    /// positional arg — same convention as <c>ScanSignatureCatalog.TryFindFunctionOwner</c> and
    /// the translator's <c>ExtractLiteralProperties</c>).
    /// <para>Purpose: when a friendly alias like <c>Dot</c> / <c>Cross</c> has several
    /// <c>ScriptMethod</c> overloads on the same library (Vector / Vector2D / Vector4),
    /// argument-type fit is what picks the right one — count-only filtering would let the
    /// first dictionary-order match win, which resolves <c>Vector.Dot(a,b)</c> to
    /// <c>DotProduct2D</c>. Contract-level so every provider and the translator share one
    /// predicate (spec 8 single convergence point).</para>
    /// </summary>
    /// <param name="argumentTypes">Positional argument types; null entries act as wildcards.</param>
    /// <param name="findClasses">Class lookup for the inheritance arm; null degrades it to
    /// bare-name matching only.</param>
    public bool ArgsFitParameters(IReadOnlyList<string?>? argumentTypes,
        Func<string, IReadOnlyList<ScanClassView>>? findClasses = null)
    {
        if (argumentTypes is null || argumentTypes.Count == 0)
            return true;
        int offset = Parameters.Count > 0
        && Parameters[0].Name.Equals("WorldContextObject", StringComparison.OrdinalIgnoreCase)
        ? 1
        : 0;
        if (argumentTypes.Count + offset > Parameters.Count)
            return false;
        for (int i = 0; i < argumentTypes.Count; i++)
        {
            if (!ScanTypeCompatibility.IsCompatible(argumentTypes[i], Parameters[i + offset].Type, findClasses))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when the function's declared <see cref="ReturnType"/> is compatible with the call
    /// site's expected return type (<paramref name="expectedReturnType"/>, e.g. "FString" when
    /// the value flows into a PrintString <c>InString</c> pin). This mirrors what
    /// <see cref="ScanFunctionView.ArgsFitParameters"/> does on the argument side, and matches
    /// the return check <c>ScanSignatureCatalog.TryFindFunctionOwner</c> already applies
    /// (<c>IsTypeCompatible(d.Function.ReturnType, expectedReturnType)</c>) so the frozen-index
    /// provider answers identically (spec 8 single convergence point). Without it a same-name
    /// collision whose argument type the translator cannot infer (e.g. <c>GetDisplayName(h.HitActor)</c>
    /// where <c>h.HitActor</c> is a struct-member read that infers as unknown) cannot be separated
    /// by argument-fit, and arbitration picks the wrong overload -- the runtime
    /// <c>UKismetSystemLibrary::GetDisplayName</c> (returns FString) versus the editor-only
    /// <c>UContentBrowserDataSource::GetDisplayName</c> (returns FText) (run 20261019_1854
    /// Trace_Hit_B: bridge bound ContentBrowserItemLibrary and UE rejected compiling an editor
    /// function into a runtime blueprint). null / empty on either side is a wildcard (no
    /// expectation, or return not indexed) and passes.
    /// </summary>
    /// <param name="expectedReturnType">Expected return type at the call site; null is a wildcard.</param>
    /// <param name="findClasses">Class lookup for the inheritance arm; null degrades it to
    /// bare-name matching only.</param>
    public bool ReturnsFit(string? expectedReturnType,
        Func<string, IReadOnlyList<ScanClassView>>? findClasses = null)
    {
        if (string.IsNullOrWhiteSpace(expectedReturnType))
            return true;
        return ScanTypeCompatibility.IsCompatible(ReturnType, expectedReturnType, findClasses);
    }

    /// <summary>
    /// Full call-site fit test used by provider arbitration — the <b>single convergence
    /// point</b> every <see cref="IScanDataProvider"/> shares (spec 8): positional argument
    /// types and the expected return type are both checked with the shared predicate,
    /// including engine auto-conversions and base-class derivation.
    /// </summary>
    /// <param name="argumentTypes">Positional argument types; null entries act as wildcards.</param>
    /// <param name="expectedReturnType">Expected return type at the call site; null is a wildcard.</param>
    /// <param name="findClasses">Class lookup for the inheritance arm; null degrades it to
    /// bare-name matching only.</param>
    public bool FitsSignature(IReadOnlyList<string?>? argumentTypes, string? expectedReturnType,
        Func<string, IReadOnlyList<ScanClassView>>? findClasses = null)
        => ArgsFitParameters(argumentTypes, findClasses)
           && ReturnsFit(expectedReturnType, findClasses);

    /// <summary>
    /// Unwrap a <c>TSubclassOf&lt;X&gt;</c> type text to its inner class name before
    /// comparison. The Blueprint wire accepts an X object reference into a
    /// TSubclassOf&lt;X&gt; pin (engine auto-inserts GetClass) and vice versa, so
    /// static-fit checks compare on X's bare name. Kept at the contract level so
    /// every provider / validator applies the same rule (spec 8 single convergence;
    /// matches <c>ScanSignatureCatalog.UnwrapTSubclassOf</c>).
    /// </summary>
    private static string UnwrapTSubclassOf(string type)
    {
        const string Prefix = "TSubclassOf<";
        if (type.Length <= Prefix.Length + 1) return type;
        if (!type.StartsWith(Prefix, StringComparison.Ordinal) || !type.EndsWith(">", StringComparison.Ordinal))
            return type;
        string inner = type[Prefix.Length..^1].Trim();
        if (inner.StartsWith("class ", StringComparison.OrdinalIgnoreCase)) inner = inner[6..].Trim();
        if (inner.StartsWith("struct ", StringComparison.OrdinalIgnoreCase)) inner = inner[7..].Trim();
        if (inner.StartsWith("const ", StringComparison.OrdinalIgnoreCase)) inner = inner[6..].Trim();
        inner = inner.TrimEnd('*', '&').Trim();
        return inner.Length == 0 ? type : inner;
    }

    private static string StripTypePrefix(string name) =>
        name.Length > 1 && name[0] is 'F' or 'U' or 'A' && char.IsUpper(name[1])
            ? name[1..]
            : name;
}

/// <summary>One declaration site of a function name.</summary>
/// <param name="Class">Class that declares it.</param>
/// <param name="Function">The declared function.</param>
public sealed record ScanFunctionDeclarationView(ScanClassView Class, ScanFunctionView Function);

/// <summary>A scanned root, reduced to what consumers can observe.</summary>
/// <param name="RootPath">Root path — one root is one scan scope.</param>
/// <param name="Version">Engine version string; a data attribute only, never an identity.</param>
/// <param name="ModuleNames">Module names directly under this root.</param>
/// <param name="PluginNames">Plugin names under this root.</param>
public sealed record ScanRootView(
    string RootPath,
    string Version,
    IReadOnlyList<string> ModuleNames,
    IReadOnlyList<string> PluginNames);