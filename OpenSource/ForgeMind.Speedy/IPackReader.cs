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

namespace ForgeMind.Speedy;

/// <summary>
/// Directory entry metadata for a single stored path (refactor plan §15).
/// Public read-side DTO — decoupled from the internal <c>DirectoryEntry</c> that
/// carries MessagePack serialization concerns. Consumers of <see cref="IPackReader"/>
/// program against this type, never against the internal layout.
/// </summary>
public record SpkEntry(long Offset, int Length, string ContentType, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>
/// Read abstraction for .spk data packs (refactor plan §15).
/// <para>
/// Two implementations: <see cref="FilePackReader"/> (from a file path) and
/// <see cref="StreamPackReader"/> (from any seekable <see cref="Stream"/> — enables
/// embedded-resource reading, refactor plan §16). The runtime reads through this
/// interface, so the backing store can change without touching consumers.</para>
/// </summary>
public interface IPackReader : IDisposable
{
    /// <summary>
    /// Reads the active directory — every stored path and its metadata. The dictionary
    /// uses ordinal key comparison.
    /// </summary>
    IReadOnlyDictionary<string, SpkEntry> ReadDirectory();

    /// <summary>Reads raw data at the specified offset and length.</summary>
    byte[] ReadAt(long offset, int length);

    /// <summary>
    /// Reads an entry's data by path (directory lookup + <see cref="ReadAt"/>).
    /// Returns null when the path is not in the directory.
    /// </summary>
    byte[]? Read(string path);
}