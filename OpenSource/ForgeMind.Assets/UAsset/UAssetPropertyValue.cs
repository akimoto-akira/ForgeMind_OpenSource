// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.Assets;

/// <summary>
/// Kind of a tagged property value. Maps 1:1 to UE property classes that this
/// module knows how to serialize generically; unsupported property types are
/// rejected with <see cref="NotSupportedException"/>.
/// </summary>
public enum UPropertyValueKind
{
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
    Bool,
    Byte,
    Enum,
    Name,
    Str,
    Object,
    Struct,
    Array,
    /// <summary>FSoftObjectPath / FSoftClassPath (SoftObject/SoftClass/AssetObject/AssetClass Property).</summary>
    SoftObject,
    /// <summary>FScriptDelegate: (object index, function FName) pair.</summary>
    Delegate,
    /// <summary>MapProperty value body: per-entry key/value tagged properties.</summary>
    Map,
    /// <summary>SetProperty value body: per-entry tagged properties.</summary>
    Set,
}

/// <summary>One key/value pair of a MapProperty value body (each side is a full tagged property).</summary>
public sealed class UMapEntry
{
    public UPropertyTag KeyTag { get; set; } = new();

    public UPropertyTag ValueTag { get; set; } = new();
}
