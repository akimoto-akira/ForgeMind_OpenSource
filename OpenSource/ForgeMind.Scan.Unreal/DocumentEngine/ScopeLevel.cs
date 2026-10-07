// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// 扫描层级标号：数字越大越基础。
/// 父类查找规则——以自身标号为起点，只在<b>同级或更大标号</b>的层级中查找；
/// 依赖方向只能从低标号指向高标号（例如项目插件的类绝不可能继承项目直属模块的类）。
/// </summary>
public enum ScopeLevel
{
    /// <summary>项目资产（uasset 扫描来源，标「资产」）。</summary>
    ProjectAsset = -2,

    /// <summary>引擎资产（uasset 扫描来源，标「资产」）。</summary>
    EngineAsset = -1,

    /// <summary>项目直属模块。</summary>
    Project = 1,

    /// <summary>项目插件内的模块。</summary>
    ProjectPlugin = 2,

    /// <summary>引擎插件内的模块。</summary>
    EnginePlugin = 3,

    /// <summary>引擎直属模块（Engine/Source 与 Platforms/*/Source）。</summary>
    Engine = 4,
}

/// <summary>
/// 「类」的种类——UE 反射类型的统称（中间层），
/// 一个 UCLASS / USTRUCT / UENUM / UINTERFACE / UDELEGATE 都算一个「类」。
/// </summary>
public enum UeTypeKind
{
    Class,
    Struct,
    Enum,
    Interface,
    Delegate,
}
