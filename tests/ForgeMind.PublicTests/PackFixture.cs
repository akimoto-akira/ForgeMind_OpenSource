// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeMind.Scan;
using ForgeMind.Speedy;

namespace ForgeMind.PublicTests;

/// <summary>
/// Writes an SPK in the runtime pack layout ({version}/engine.json) so read-side tests can
/// exercise ScanPackReader / ScanPackLoader without depending on the engine scanner.
/// <para>
/// The historical in-boundary writer (ScanResultPacker) was removed with the ScanCache-layout
/// refactor: writing now lives in the engine scanner, which is outside this project's
/// dependency boundary (open-source components only, no UE scanner). The pack format itself is
/// still part of the open contract — ScanPackReader exposes its JSON node types publicly — so
/// tests can write that shape directly and still verify the read path end to end.
/// </para>
/// </summary>
internal static class PackFixture
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes one version's engine tree entry ({version}/engine.json).</summary>
    public static void Write(string path, string version, params ScanPackReader.DtoRoot[] roots)
    {
        using var pack = SpeedyPack.Open(path);
        pack.Write($"{version}/engine.json", JsonSerializer.SerializeToUtf8Bytes(roots.ToList(), s_json));
    }

    public static ScanPackReader.DtoRoot Root(string rootPath, string version,
        params ScanPackReader.DtoClass[] classes) => new()
        {
            RootPath = rootPath,
            Version = version,
            Modules = [new ScanPackReader.DtoModule { Name = "Core", Classes = [.. classes] }],
        };

    public static ScanPackReader.DtoClass Class(string name, string? superClass,
        params ScanPackReader.DtoFunction[] functions) => new()
        {
            Name = name,
            FilePath = "Test.h",
            LineNumber = 1,
            BaseClasses = superClass is null ? [] : [superClass],
            Functions = [.. functions],
        };

    public static ScanPackReader.DtoFunction Function(string name, bool isStatic,
        params ScanPackReader.DtoParameter[] parameters) => new()
        {
            Name = name,
            IsStatic = isStatic,
            ReturnType = "void",
            Parameters = [.. parameters],
        };

    /// <summary>
    /// One parameter node. <paramref name="isOut"/> marks a non-const C++ reference
    /// ("T&amp;") — an out parameter, i.e. an output pin in Blueprint.
    /// </summary>
    public static ScanPackReader.DtoParameter Parameter(string name, string? type, bool isOut = false) => new()
    {
        Name = name,
        Type = type,
        IsOut = isOut,
    };
}
