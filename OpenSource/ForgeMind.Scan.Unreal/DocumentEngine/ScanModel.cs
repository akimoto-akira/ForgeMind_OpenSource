// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// 扫描结果的数据组织（最符合 UE 的形态）：
/// <para>根 → 插件 / 模块 → 类 → 成员 → 参数。</para>
/// 归属唯一：每个模块要么是根直属，要么属于唯一一个插件，不重复统计；
/// 插件模块从根的直属模块集合中剔除。
/// </summary>
public sealed class ScanRoot
{
    /// <summary>根路径（引擎安装路径或项目路径）——一条根路径就是一个扫描范围。</summary>
    public required string RootPath { get; init; }

    /// <summary>所属层级（引擎侧 / 项目侧；资产来源见 <see cref="ScanSource"/>）。</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>引擎版本等描述信息——仅作数据属性，绝不作身份标识。</summary>
    public string Version { get; init; } = "";

    /// <summary>插件集合（引擎侧：Engine/Plugins；项目侧：Project/Plugins）。</summary>
    public List<PluginInfo> Plugins { get; } = [];

    /// <summary>根直属模块集合（<b>不含</b>任何插件内的模块）。</summary>
    public List<ModuleInfo> Modules { get; } = [];

    /// <summary>资产集合（uasset 扫描来源，标「资产」；引擎侧 = EngineAsset，项目侧 = ProjectAsset）。</summary>
    public List<UeAssetEntry> Assets { get; } = [];
}

/// <summary>
/// uasset 资产条目——来自资产反序列化的事实（来源标注「资产」，与头文件扫出的 C++ 类区分）。
/// 消费方通用：包括 DSL（各域）但不限于 DSL，不与任何域绑死。
/// </summary>
public sealed class UeAssetEntry
{
    /// <summary>资产文件完整路径。</summary>
    public required string FilePath { get; init; }

    /// <summary>资产文件名（不含扩展名）。</summary>
    public required string Name { get; init; }

    /// <summary>所属层级：引擎资产（EngineAsset）或项目资产（ProjectAsset）。</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>包摘要的 UE4 / UE5 文件版本（仅作数据属性）。</summary>
    public int FileVersionUe4 { get; init; }

    public int FileVersionUe5 { get; init; }

    /// <summary>导出对象数量。</summary>
    public int ExportCount { get; init; }

    /// <summary>导出对象（对象名 + 类名；类名为 import 时的类名）。</summary>
    public List<UeAssetExport> Exports { get; } = [];

    /// <summary>解析失败的文件不产出条目；此处累计失败数（诊断用）。</summary>
    public int FailedExports { get; init; }
}

/// <summary>uasset 内的一个导出对象。</summary>
public sealed class UeAssetExport
{
    public required string ObjectName { get; init; }

    /// <summary>类名（导出类为 import 时，解析到被引用类名）。</summary>
    public string? ClassName { get; init; }
}

/// <summary>检索命中的对象种类（点查与检索共用同一索引，此处为检索的结果面）。</summary>
public enum ScanSearchHitKind
{
    Class,
    Function,
    Property,
    Asset,
}

/// <summary>检索命中条目。</summary>
public sealed class ScanSearchHit
{
    /// <summary>命中对象种类。</summary>
    public required ScanSearchHitKind Kind { get; init; }

    /// <summary>命中名（类名 / 函数名 / 属性名 / 资产名）。</summary>
    public required string Name { get; init; }

    /// <summary>宿主类名（函数 / 属性命中时有值；其余为空）。</summary>
    public string? Owner { get; init; }

    /// <summary>所在模块名（资产命中时为空）。</summary>
    public string? Module { get; init; }

    /// <summary>位置（类 / 成员 = 文件:行号；资产 = 文件路径）。</summary>
    public string Location { get; init; } = "";

    /// <summary>所属层级（六级标号）。</summary>
    public required ScopeLevel Scope { get; init; }
}

/// <summary>
/// 插件（带 .uplugin 的一等公民）——插件是引擎 / 项目之下的又一层容器，
/// 一个插件包含若干模块。
/// </summary>
public sealed class PluginInfo
{
    /// <summary>插件名（.uplugin 文件名，不含扩展名）。</summary>
    public required string Name { get; init; }

    /// <summary>插件目录。</summary>
    public required string Directory { get; init; }

    /// <summary>.uplugin 描述文件路径（可能缺失——目录存在但描述文件缺失时不阻断扫描）。</summary>
    public string? DescriptorFile { get; init; }

    /// <summary>所属层级：引擎插件（EnginePlugin）或项目插件（ProjectPlugin）。</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>该插件包含的模块。</summary>
    public List<ModuleInfo> Modules { get; } = [];
}

/// <summary>模块（UBT 模块规则 + 头文件集合）——既是组织单位，也是扫描与缓存的物理单位。</summary>
public sealed class ModuleInfo
{
    /// <summary>模块名（.Build.cs 文件名，不含扩展名）。</summary>
    public required string Name { get; init; }

    /// <summary>模块目录（.Build.cs 所在目录）。</summary>
    public required string Directory { get; init; }

    /// <summary>.Build.cs 规则文件路径。</summary>
    public string? RulesFile { get; init; }

    /// <summary>所属层级（继承自容器）。</summary>
    public required ScopeLevel Scope { get; set; }

    /// <summary>该模块扫描出的类（中间层）。</summary>
    public List<UeClassInfo> Classes { get; } = [];
}

/// <summary>
/// 「类」——中间层的原子单位：UCLASS / USTRUCT / UENUM / UINTERFACE / UDELEGATE 统称。
/// 只存扫描事实（自己声明的成员 + 基类名），继承成员的聚合由查询服务在返回前计算。
/// </summary>
public sealed class UeClassInfo
{
    /// <summary>类型名（含 U / A / F / I / E 前缀，如 AActor）。</summary>
    public required string Name { get; init; }

    /// <summary>种类（class / struct / enum / interface / delegate）。</summary>
    public required UeTypeKind Kind { get; init; }

    /// <summary>声明所在源文件路径。</summary>
    public string FilePath { get; init; } = "";

    /// <summary>声明在源文件中的行号。</summary>
    public int LineNumber { get; init; }

    /// <summary>基类名列表（扫出什么记什么；含接口实现）。</summary>
    public List<string> BaseClasses { get; } = [];

    /// <summary>宏括号内的 Specifier 列表。</summary>
    public List<string> Specifiers { get; } = [];

    /// <summary>宏括号内的 Metadata 键值对。</summary>
    public Dictionary<string, string> Metadata { get; } = [];

    /// <summary>自己声明的 UFUNCTION 成员。</summary>
    public List<UeFunctionInfo> Functions { get; } = [];

    /// <summary>自己声明的 UPROPERTY 成员。</summary>
    public List<UePropertyInfo> Properties { get; } = [];

    /// <summary>所属层级（继承自所在模块 / 资产来源）。</summary>
    public required ScopeLevel Scope { get; set; }
}

/// <summary>UFUNCTION 成员——参数名即蓝图引脚名。</summary>
public sealed class UeFunctionInfo
{
    public required string Name { get; init; }

    /// <summary>是否静态函数（静态函数的首参 WorldContextObject 隐含）。</summary>
    public bool IsStatic { get; init; }

    /// <summary>返回类型（如 "FString" / "void"；扫描未覆盖时为 null——类型匹配时作通配）。</summary>
    public string? ReturnType { get; init; }

    public List<string> Specifiers { get; } = [];

    public Dictionary<string, string> Metadata { get; } = [];

    public string FilePath { get; init; } = "";

    public int LineNumber { get; init; }

    /// <summary>参数列表（顺序即引脚顺序）。</summary>
    public List<UeParameterInfo> Parameters { get; } = [];
}

/// <summary>UPROPERTY 成员。</summary>
public sealed class UePropertyInfo
{
    public required string Name { get; init; }

    /// <summary>声明类型（可为空——扫描未覆盖时不强求）。</summary>
    public string? Type { get; init; }

    public List<string> Specifiers { get; } = [];

    public Dictionary<string, string> Metadata { get; } = [];

    public string FilePath { get; init; } = "";

    public int LineNumber { get; init; }
}

/// <summary>UFUNCTION 的参数——名 / 类型 / 默认值，直接对应蓝图引脚。</summary>
public sealed class UeParameterInfo
{
    /// <summary>参数名（即引脚名）。</summary>
    public required string Name { get; init; }

    /// <summary>参数类型（可为空——扫描未覆盖时不强求）。</summary>
    public string? Type { get; init; }

    /// <summary>C++ 声明中的默认值（可能为空——没有默认值不等于默认值是 null）。</summary>
    public string? DefaultValue { get; init; }
}
