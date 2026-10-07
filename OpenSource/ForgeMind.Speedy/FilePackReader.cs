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
/// <see cref="IPackReader"/> backed by a file on disk (refactor plan §15).
/// Opens the .spk file in read-only shared mode and loads the active directory.
/// </summary>
public sealed class FilePackReader : IPackReader
{
    private readonly PackFileReader _reader;
    private readonly Dictionary<string, SpkEntry> _directory;

    /// <summary>Opens the .spk file and reads the active header + directory.</summary>
    public FilePackReader(string filePath)
    {
        _reader = PackFileReader.Open(filePath);
        var active = _reader.TryReadActiveHeader()
            ?? throw new InvalidDataException(
                $"Both header slots of '{filePath}' are corrupted.");
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