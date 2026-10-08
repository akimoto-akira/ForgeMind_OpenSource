// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// ScanCache-format serialization layer: writes the algorithm-produced <see cref="ScanRoot"/> tree to JSON verbatim.
/// <para>
/// No view / flattening / trimming here — the algorithm has passed 25+ use cases so it is correct; the SPK is just
/// the container for those files. Therefore the JSON shape, field names, nesting, and serializer options are
/// <b>character-identical</b> to the bytes historically written to <c>ScanCache/{version}/engine.json</c>:
/// same DTO shape, same <see cref="JsonSerializerOptions"/> (no indent, no null, camelCase, default encoder).
/// Changing any field would break byte compatibility with historical caches.</para>
/// </summary>
public static class ScanCachePersistence
{
    // ── Intermediate POCOs (identical to history; field order is JSON field order) ──

    private sealed class DtoRoot
    {
        public string RootPath { get; set; } = "";
        public int Scope { get; set; }
        public string Version { get; set; } = "";
        public List<DtoPlugin> Plugins { get; set; } = [];
        public List<DtoModule> Modules { get; set; } = [];
        public List<DtoAsset> Assets { get; set; } = [];
    }

    private sealed class DtoPlugin
    {
        public string Name { get; set; } = "";
        public string Directory { get; set; } = "";
        public string? DescriptorFile { get; set; }
        public int Scope { get; set; }
        public List<DtoModule> Modules { get; set; } = [];
    }

    private sealed class DtoModule
    {
        public string Name { get; set; } = "";
        public string Directory { get; set; } = "";
        public string? RulesFile { get; set; }
        public int Scope { get; set; }
        public List<DtoClass> Classes { get; set; } = [];
    }

    private sealed class DtoClass
    {
        public string Name { get; set; } = "";
        public int Kind { get; set; }
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public List<string> BaseClasses { get; set; } = [];
        public List<string> Specifiers { get; set; } = [];
        public Dictionary<string, string> Metadata { get; set; } = [];
        public List<DtoFunction> Functions { get; set; } = [];
        public List<DtoProperty> Properties { get; set; } = [];
        public int Scope { get; set; }
    }

    private sealed class DtoFunction
    {
        public string Name { get; set; } = "";
        public bool IsStatic { get; set; }
        public string? ReturnType { get; set; }
        public List<string> Specifiers { get; set; } = [];
        public Dictionary<string, string> Metadata { get; set; } = [];
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public List<DtoParameter> Parameters { get; set; } = [];
    }

    private sealed class DtoProperty
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public List<string> Specifiers { get; set; } = [];
        public Dictionary<string, string> Metadata { get; set; } = [];
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
    }

    private sealed class DtoParameter
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public string? DefaultValue { get; set; }
    }

    private sealed class DtoAsset
    {
        public string FilePath { get; set; } = "";
        public string Name { get; set; } = "";
        public int Scope { get; set; }
        public int FileVersionUe4 { get; set; }
        public int FileVersionUe5 { get; set; }
        public int ExportCount { get; set; }
        public List<DtoAssetExport> Exports { get; set; } = [];
        public int FailedExports { get; set; }
    }

    private sealed class DtoAssetExport
    {
        public string ObjectName { get; set; } = "";
        public string? ClassName { get; set; }
    }

    // ── Serializer options (identical to history, item by item) ──

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Serializes the engine-side scan root to <c>engine.json</c> content (character-identical to historical file).
    /// </summary>
    public static byte[] SerializeEngineRoots(IEnumerable<ScanRoot> roots) =>
        JsonSerializer.SerializeToUtf8Bytes(roots.Select(ToDto).ToList(), s_jsonOpts);

    /// <summary>
    /// Serializes the engine-asset scan root to <c>assets.json</c> content (character-identical to historical file).
    /// </summary>
    public static byte[] SerializeAssetRoots(IEnumerable<ScanRoot> roots) =>
        JsonSerializer.SerializeToUtf8Bytes(roots.Select(ToDto).ToList(), s_jsonOpts);

    // ── Model → DTO ──

    private static DtoRoot ToDto(ScanRoot root) => new()
    {
        RootPath = root.RootPath,
        Scope = (int)root.Scope,
        Version = root.Version,
        Plugins = root.Plugins.Select(ToDto).ToList(),
        Modules = root.Modules.Select(ToDto).ToList(),
        Assets = root.Assets.Select(ToDto).ToList(),
    };

    private static DtoPlugin ToDto(PluginInfo plugin) => new()
    {
        Name = plugin.Name,
        Directory = plugin.Directory,
        DescriptorFile = plugin.DescriptorFile,
        Scope = (int)plugin.Scope,
        Modules = plugin.Modules.Select(ToDto).ToList(),
    };

    private static DtoModule ToDto(ModuleInfo module) => new()
    {
        Name = module.Name,
        Directory = module.Directory,
        RulesFile = module.RulesFile,
        Scope = (int)module.Scope,
        Classes = module.Classes.Select(ToDto).ToList(),
    };

    private static DtoClass ToDto(UeClassInfo cls) => new()
    {
        Name = cls.Name,
        Kind = (int)cls.Kind,
        FilePath = cls.FilePath,
        LineNumber = cls.LineNumber,
        BaseClasses = [.. cls.BaseClasses],
        Specifiers = [.. cls.Specifiers],
        Metadata = new Dictionary<string, string>(cls.Metadata),
        Functions = cls.Functions.Select(ToDto).ToList(),
        Properties = cls.Properties.Select(ToDto).ToList(),
        Scope = (int)cls.Scope,
    };

    private static DtoFunction ToDto(UeFunctionInfo fn) => new()
    {
        Name = fn.Name,
        IsStatic = fn.IsStatic,
        ReturnType = fn.ReturnType,
        Specifiers = [.. fn.Specifiers],
        Metadata = new Dictionary<string, string>(fn.Metadata),
        FilePath = fn.FilePath,
        LineNumber = fn.LineNumber,
        Parameters = fn.Parameters.Select(ToDto).ToList(),
    };

    private static DtoParameter ToDto(UeParameterInfo p) => new()
    {
        Name = p.Name,
        Type = p.Type,
        DefaultValue = p.DefaultValue,
    };

    private static DtoProperty ToDto(UePropertyInfo prop) => new()
    {
        Name = prop.Name,
        Type = prop.Type,
        Specifiers = [.. prop.Specifiers],
        Metadata = new Dictionary<string, string>(prop.Metadata),
        FilePath = prop.FilePath,
        LineNumber = prop.LineNumber,
    };

    private static DtoAsset ToDto(UeAssetEntry asset)
    {
        var dto = new DtoAsset
        {
            FilePath = asset.FilePath,
            Name = asset.Name,
            Scope = (int)asset.Scope,
            FileVersionUe4 = asset.FileVersionUe4,
            FileVersionUe5 = asset.FileVersionUe5,
            ExportCount = asset.ExportCount,
            FailedExports = asset.FailedExports,
        };
        foreach (var e in asset.Exports)
            dto.Exports.Add(new DtoAssetExport { ObjectName = e.ObjectName, ClassName = e.ClassName });
        return dto;
    }
}
