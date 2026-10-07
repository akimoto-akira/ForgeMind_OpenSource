// Copyright (c) 2026 Hoshino Kennji
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using ForgeMind.Speedy.Internal;

namespace ForgeMind.Speedy;

/// <summary>
/// <see cref="IPackReader"/> backed by any seekable <see cref="Stream"/> (refactor plan §15).
/// <para>
/// Enables reading an .spk pack from a <see cref="MemoryStream"/>, a
/// <c>ManifestResourceStream</c> (refactor plan §16), or any other seekable stream —
/// without a file on disk. The stream must be readable and seekable; ownership is
/// transferred to the reader (disposed on <see cref="Dispose"/>).</para>
/// </summary>
public sealed class StreamPackReader : IPackReader
{
    private readonly PackFileReader _reader;
    private readonly Dictionary<string, SpkEntry> _directory;

    /// <summary>
    /// Opens the .spk pack from the given stream and reads the active header + directory.
    /// The stream must support seeking (Position / Length). Ownership transfers to the reader.
    /// </summary>
    public StreamPackReader(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable to read an .spk pack.", nameof(stream));
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        _reader = PackFileReader.Open(stream);
        var active = _reader.TryReadActiveHeader()
            ?? throw new InvalidDataException("Both header slots are corrupted.");
        var rawDirectory = _reader.LoadDirectory(active.Header);
        _directory = new Dictionary<string, SpkEntry>(rawDirectory.Count, StringComparer.Ordinal);
        foreach (var (path, entry) in rawDirectory)
            _directory[path] = ToEntry(entry);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, SpkEntry> ReadDirectory() => _directory;

    /// <inheritdoc />
    public byte[] ReadAt(long offset, int length) => _reader.ReadAt(offset, length);

    /// <inheritdoc />
    public byte[]? Read(string path)
    {
        if (!_directory.TryGetValue(path, out var entry))
            return null;
        return _reader.ReadAt(entry.Offset, entry.Length);
    }

    /// <inheritdoc />
    public void Dispose() => _reader.Dispose();

    private static SpkEntry ToEntry(DirectoryEntry e) =>
        new(e.Offset, e.Length, e.ContentType, e.CreatedAt, e.UpdatedAt);
}