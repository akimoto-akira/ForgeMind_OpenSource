// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Speedy;

namespace ForgeMind.Scan.Unreal;

/// <summary>
/// 把扫描算法产出的原始 <see cref="ScanRoot"/> 树写进 SPK，布局与历史 ScanCache 目录一致：
/// <code>
/// {version}/engine.json     — 引擎侧代码扫描结果
/// {version}/assets.json     — 引擎侧资产扫描结果
/// </code>
/// <para>
/// 没有 manifest、没有 index/、没有 data/ —— ScanCache 里没有的东西这里也不造。
/// 每个版本一对文件，版本之间互不干扰。</para>
/// </summary>
public sealed class ScanCachePackWriter
{
    private readonly IPackWriter _writer;

    public ScanCachePackWriter(IPackWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <summary>
    /// 写入一个版本的扫描结果。同名条目整块覆盖（同一版本重扫即刷新该版本）。
    /// </summary>
    public void Write(string version, IEnumerable<ScanRoot> codeRoots, IEnumerable<ScanRoot> assetRoots)
    {
        string prefix = SanitizeVersion(version);

        _writer.Write($"{prefix}/engine.json", ScanCachePersistence.SerializeEngineRoots(codeRoots));
        _writer.Write($"{prefix}/assets.json", ScanCachePersistence.SerializeAssetRoots(assetRoots));
    }

    /// <summary>
    /// 版本串用作路径段：把路径分隔符与通配符换成下划线，避免写出非法条目路径。
    /// </summary>
    private static string SanitizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "_";

        var sb = new System.Text.StringBuilder(version.Length);
        foreach (char c in version)
            sb.Append(c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        return sb.ToString();
    }
}
