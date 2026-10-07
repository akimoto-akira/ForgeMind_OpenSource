// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Globalization;
using System.Text;

namespace ForgeMind.Assets;

/// <summary>
/// Augmented GUID stored as four uint32 components. Unreal Engine serializes
/// FGuid without any byte reordering: A, B, C, D are written back to back in
/// little-endian order (16 bytes total).
/// </summary>
public readonly struct UAssetGuid : IEquatable<UAssetGuid>
{
    public uint A { get; }
    public uint B { get; }
    public uint C { get; }
    public uint D { get; }

    public UAssetGuid(uint a, uint b, uint c, uint d)
    {
        A = a;
        B = b;
        C = c;
        D = d;
    }

    /// <summary>Parses the canonical "A-B-C-D" hexadecimal form.</summary>
    public static UAssetGuid Parse(string text)
    {
        string[] parts = text.Split('-');
        return new UAssetGuid(
            uint.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public bool Equals(UAssetGuid other) => A == other.A && B == other.B && C == other.C && D == other.D;

    public override bool Equals(object? obj) => obj is UAssetGuid other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(A, B, C, D);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{A:x8}-{B:x8}-{C:x8}-{D:x8}");
}

/// <summary>
/// Read side of a uasset byte stream; little-endian, mirroring FLinkerLoad.
/// </summary>
public sealed class UAssetReader
{
    private readonly BinaryReader _reader;

    public UAssetReader(Stream stream)
    {
        _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
    }

    public long Position => _reader.BaseStream.Position;

    public long Length => _reader.BaseStream.Length;

    public void Seek(long position) => _reader.BaseStream.Position = position;

    public int ReadInt32() => _reader.ReadInt32();

    public uint ReadUInt32() => _reader.ReadUInt32();

    public short ReadInt16() => _reader.ReadInt16();

    public ushort ReadUInt16() => _reader.ReadUInt16();

    public long ReadInt64() => _reader.ReadInt64();

    public ulong ReadUInt64() => _reader.ReadUInt64();

    public float ReadFloat() => _reader.ReadSingle();

    public double ReadDouble() => _reader.ReadDouble();

    public byte ReadByte() => _reader.ReadByte();

    /// <summary>Reads a 32-bit legacy bool (0/1).</summary>
    public bool ReadBool() => _reader.ReadUInt32() != 0;

    public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

    /// <summary>Reads an FGuid (16 bytes, four uint32).</summary>
    public UAssetGuid ReadGuid() => new(_reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt32());

    /// <summary>Reads an FName reference: (ComparisonIndex, Number).</summary>
    public (int Index, int Number) ReadFName() => (_reader.ReadInt32(), _reader.ReadInt32());

    /// <summary>
    /// Reads an <c>FString</c>. A negative length means UTF-16LE; otherwise the
    /// bytes are the raw ANSI characters. Both forms include the trailing NUL.
    /// </summary>
    public string ReadFString()
    {
        int length = _reader.ReadInt32();
        if (length == 0)
        {
            return string.Empty;
        }

        if (length > 0)
        {
            byte[] raw = _reader.ReadBytes(length);
            string text = Encoding.Latin1.GetString(raw);
            return text.EndsWith('\0') ? text[..^1] : text;
        }

        int charCount = -length;
        byte[] wide = _reader.ReadBytes(charCount * 2);
        string decoded = Encoding.Unicode.GetString(wide);
        return decoded.EndsWith('\0') ? decoded[..^1] : decoded;
    }

    /// <summary>
    /// Reads a name table entry: an FString-style literal plus, for versions
    /// >= VER_UE4_NAME_HASHES_SERIALIZED (504), two uint16 hashes.
    /// </summary>
    public string ReadNameEntry(bool nameHashes)
    {
        string name = ReadFString();
        if (nameHashes)
        {
            _reader.ReadUInt16();
            _reader.ReadUInt16();
        }
        return name;
    }
}

/// <summary>
/// Write side of a uasset byte stream; the mirror of <see cref="UAssetReader"/>
/// (following UAssetAPI's AssetBinaryWriter principle): every helper writes
/// exactly the encoding the reader consumes.
/// </summary>
public sealed class UAssetWriter
{
    private readonly BinaryWriter _writer;

    public UAssetWriter(Stream stream)
    {
        _writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
    }

    public long Position => _writer.BaseStream.Position;

    public void Seek(long position) => _writer.BaseStream.Position = position;

    public void WriteInt32(int value) => _writer.Write(value);

    /// <summary>Overwrites an int32 at an absolute position, then restores the current position (placeholder backfill).</summary>
    public void WriteInt32At(long position, int value)
    {
        long saved = Position;
        Seek(position);
        WriteInt32(value);
        Seek(saved);
    }

    /// <summary>Overwrites an int64 at an absolute position, then restores the current position (placeholder backfill).</summary>
    public void WriteInt64At(long position, long value)
    {
        long saved = Position;
        Seek(position);
        WriteInt64(value);
        Seek(saved);
    }

    public void WriteUInt32(uint value) => _writer.Write(value);

    public void WriteInt16(short value) => _writer.Write(value);

    public void WriteUInt16(ushort value) => _writer.Write(value);

    public void WriteInt64(long value) => _writer.Write(value);

    public void WriteUInt64(ulong value) => _writer.Write(value);

    public void WriteFloat(float value) => _writer.Write(value);

    public void WriteDouble(double value) => _writer.Write(value);

    public void WriteByte(byte value) => _writer.Write(value);

    public void WriteBytes(byte[] value) => _writer.Write(value);

    /// <summary>Writes a 32-bit legacy bool (0/1).</summary>
    public void WriteBool(bool value) => _writer.Write(value ? 1u : 0u);

    /// <summary>Writes an FGuid (16 bytes, four uint32).</summary>
    public void WriteGuid(UAssetGuid guid)
    {
        WriteUInt32(guid.A);
        WriteUInt32(guid.B);
        WriteUInt32(guid.C);
        WriteUInt32(guid.D);
    }

    /// <summary>Writes an FName reference: (ComparisonIndex, Number).</summary>
    public void WriteFName(int index, int number = 0)
    {
        WriteInt32(index);
        WriteInt32(number);
    }

    /// <summary>
    /// Writes an <c>FString</c>: a positive length means ASCII/UTF-8 bytes
    /// (including the trailing NUL), a negative length means UTF-16LE char
    /// count (including the trailing NUL), zero means the empty string.
    /// </summary>
    public void WriteFString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteInt32(0);
            return;
        }

        bool pureAscii = true;
        foreach (char c in value)
        {
            if (c > 0x7e)
            {
                pureAscii = false;
                break;
            }
        }

        if (pureAscii)
        {
            WriteInt32(value.Length + 1);
            WriteBytes(Encoding.Latin1.GetBytes(value));
            WriteByte(0);
        }
        else
        {
            WriteInt32(-(value.Length + 1));
            WriteBytes(Encoding.Unicode.GetBytes(value));
            WriteBytes(new byte[] { 0, 0 });
        }
    }

    /// <summary>Writes a name table entry: FString literal plus two uint16 hashes (when serialized).</summary>
    public void WriteNameEntry(string name, bool nameHashes)
    {
        WriteFString(name);
        if (nameHashes)
        {
            WriteUInt16(0);
            WriteUInt16(0);
        }
    }
}
