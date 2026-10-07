// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// UE 文档引擎抽象层：持有扫描输入（路径全部<b>注入</b>，无硬编码），
/// 并以模板方法固定 UE 的三段扫描流程——模块 → 头文件 → 资产。
/// <para>
/// 四路独立扫描：<b>引擎代码</b> / <b>引擎资产</b> / <b>项目代码</b> / <b>项目资产</b>
/// 各自独立产出 <see cref="ScanRoot"/>，先到先入索引——翻译器不用等资产扫完就能查签名。
/// </para>
/// <para>
/// 版本子类（Ue5_x）规则（测试驱动，不靠版本号推测）：
/// 扫描器 N 测 N 版源码通过且测 N+1 版也通过 → 共用；
/// 测 N+1 版报错 → 为 N+1 单开扫描器子类。
/// <see cref="EngineVersion"/> 仅作数据属性，绝不用作身份标识。
/// </para>
/// </summary>
public abstract class UeDocumentEngine : DocumentEngine
{
    /// <summary>UE 源代码路径（GitHub 源码或本地源码）——由 engine 工具定位结果注入。</summary>
    public DirectoryInfo SourceCodeDir { get; }

    /// <summary>UE 安装版路径（Engine 安装目录）——由 engine 工具定位结果注入。</summary>
    public DirectoryInfo InstallDir { get; }

    /// <summary>引擎版本描述（如 "5.0.3"）——仅作数据属性，绝不用作身份标识。</summary>
    public string EngineVersion { get; }

    /// <summary>项目路径（可选）——由编辑器启动事件确定；为空时只扫引擎侧。</summary>
    public DirectoryInfo? ProjectDir { get; init; }

    /// <summary>扫描诊断（规则编译 / 资产解析等失败信息）——不中断扫描，供测试驱动补齐存根。</summary>
    public List<string> ScanDiagnostics { get; } = [];

    protected UeDocumentEngine(DirectoryInfo sourceCodeDir, DirectoryInfo installDir, string engineVersion)
    {
        SourceCodeDir = sourceCodeDir;
        InstallDir = installDir;
        EngineVersion = engineVersion;
    }

    /// <summary>
    /// 模板方法：引擎侧与项目侧同构（同一套模型与扫描逻辑，只是根不同）。
    /// 项目路径未注入时只产引擎侧根。
    /// </summary>
    public sealed override IReadOnlyList<ScanRoot> Scan()
    {
        var roots = new List<ScanRoot>();
        roots.AddRange(ScanEngineCode());
        roots.AddRange(ScanEngineAssets());
        roots.AddRange(ScanProjectCode());
        roots.AddRange(ScanProjectAssets());
        return roots;
    }

    // ── 四路独立扫描（各自产出 ScanRoot，先到先入索引）──

    /// <summary>
    /// 引擎代码扫描：插件发现 + 模块规则编译 + 头文件扫描。
    /// 不含资产——代码扫完即可投递，翻译器立即能查父类链签名。
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanEngineCode();

    /// <summary>
    /// 引擎资产扫描：遍历引擎安装目录下的 .uasset。
    /// 独立于代码扫描，可后行投递；耗时较长但不阻塞签名查询。
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanEngineAssets();

    /// <summary>
    /// 项目代码扫描：项目直属模块 + 项目插件模块的规则编译 + 头文件扫描。
    /// 独立于引擎侧，可并行投递。
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanProjectCode();

    /// <summary>
    /// 项目资产扫描：遍历项目目录下的 .uasset。
    /// 独立于代码扫描，最后投递。
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> ScanProjectAssets();
}
