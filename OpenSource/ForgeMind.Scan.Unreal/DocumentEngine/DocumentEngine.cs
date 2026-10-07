// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// 文档引擎抽象基类（分层顶点）：触发一次扫描，产出按根组织的索引数据。
/// <para>
/// 分层沿用：DocumentEngine → UeDocumentEngine → Ue5_x。
/// 一条根路径就是一个扫描范围；扫描结果由调用方持有到进程结束（不落盘、不缓存）。
/// </para>
/// </summary>
public abstract class DocumentEngine
{
    /// <summary>
    /// 触发一次完整扫描，产出扫描范围内所有根的数据
    /// （引擎侧根 + 项目侧根；无项目时只含引擎侧根）。
    /// </summary>
    public abstract IReadOnlyList<ScanRoot> Scan();
}
