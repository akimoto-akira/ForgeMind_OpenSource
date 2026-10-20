// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Abstractions;

/// <summary>
/// Decomposes the raw <c>meta=(Key="Value", ...)</c> capture written by the scanner into individual
/// metadata entries so consumers can query by friendly alias (DisplayName / ScriptName / ScriptMethod).
/// <para>
/// <see cref="ScanFunctionView.Metadata"/> is documented as "already flattened". Every provider
/// (SPK reader path and live-scanner path) funnels its raw metadata through
/// <see cref="Normalize"/> before handing it to the view. Consumers therefore never see the packed
/// form and never need to unpack themselves — single convergence point (spec 8).</para>
/// <para>
/// Both the packed and the already-flattened forms are accepted, so this helper works whether the
/// scanner wrote <c>metadata = { "meta": "(DisplayName=\"X\", ScriptName=\"Y\")" }</c> (pre-fix packs)
/// or <c>metadata = { "DisplayName": "X", "ScriptName": "Y" }</c> (post-fix packs). Rebuilding
/// <c>engine.spk</c> is not required for correctness, only for tidiness.</para>
/// </summary>
public static class FunctionMetadataNormalizer
{
    /// <summary>
    /// Returns a dictionary where any top-level <c>"meta"</c> entry holding a parenthesized string
    /// is expanded into its inner <c>Key="Value"</c> pairs. Non-<c>meta</c> entries pass through
    /// unchanged. Values are unquoted. Null input yields null output; empty input yields an empty
    /// read-only dictionary. Existing inner keys do not overwrite pre-existing top-level keys.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Normalize(IReadOnlyDictionary<string, string>? raw)
    {
        if (raw is null)
            return null;
        if (raw.Count == 0)
            return raw;
        // Fast path: nothing to unpack.
        if (!raw.TryGetValue("meta", out var packed) || string.IsNullOrWhiteSpace(packed))
            return raw;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            if (!string.Equals(key, "meta", StringComparison.Ordinal))
                result[key] = value;
        }

        foreach (var (innerKey, innerValue) in ParseMetaList(packed))
        {
            if (!result.ContainsKey(innerKey))
                result[innerKey] = innerValue;
        }

        return result;
    }

    /// <summary>
    /// Parses a UE macro meta list. Accepts both the parenthesized form
    /// <c>"(K1=\"V1\", K2=V2)"</c> and the bare form <c>"K1=\"V1\", K2=V2"</c>. Splits on
    /// commas at depth 0 while respecting quoted strings so commas inside
    /// <c>"Load Stream Level (by Name)"</c> never split prematurely. A segment with <c>=</c> at
    /// depth 0 becomes a key-value pair; a segment without one becomes a flag with empty value
    /// (rare in meta lists but the shape stays for caller convenience).
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> ParseMetaList(string text)
    {
        string body = text.Trim();
        if (body.Length >= 2 && body[0] == '(' && body[^1] == ')')
            body = body[1..^1];

        int start = 0;
        for (int i = 0; i <= body.Length; i++)
        {
            bool atEnd = i == body.Length;
            char c = atEnd ? '\0' : body[i];
            if (atEnd || c == ',')
            {
                string segment = body.Substring(start, i - start).Trim();
                start = i + 1;
                if (segment.Length == 0)
                    continue;
                int eq = IndexOfEqualsTopLevel(segment);
                if (eq > 0)
                {
                    string key = segment[..eq].Trim();
                    string val = Unquote(segment[(eq + 1)..].Trim());
                    if (key.Length > 0)
                        yield return new(key, val);
                }
                else
                {
                    yield return new(Unquote(segment), "");
                }
                continue;
            }

            // Skip to end of a quoted run so its commas never split.
            if (c == '"')
            {
                i++;
                while (i < body.Length && body[i] != '"')
                {
                    if (body[i] == '\\' && i + 1 < body.Length)
                        i++;
                    i++;
                }
                continue;
            }
        }
    }

    private static int IndexOfEqualsTopLevel(string text)
    {
        int depthParen = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '"')
            {
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                        i++;
                    i++;
                }
                continue;
            }
            if (ch == '(') { depthParen++; continue; }
            if (ch == ')') { if (depthParen > 0) depthParen--; continue; }
            if (ch == '=' && depthParen == 0)
                return i;
        }
        return -1;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            string inner = value[1..^1];
            return inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        return value;
    }
}
