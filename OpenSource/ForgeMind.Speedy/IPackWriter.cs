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
/// Write abstraction for .spk data packs (refactor plan §14).
/// <para>
/// Symmetric to <see cref="IPackReader"/> on the read side. A scanner provider (refactor
/// plan §15) writes scan results through this interface — the backing store (file, stream,
/// future targets) is decided by the concrete implementation, not by the caller.</para>
/// <para>
/// <see cref="SpeedyPack"/> implements both <see cref="IPackReader"/> (via its read methods)
/// and <see cref="IPackWriter"/>; the split lets a scanner depend on only the side it uses.</para>
/// </summary>
public interface IPackWriter : IDisposable
{
    /// <summary>Writes raw byte data. Cache is updated synchronously, disk persistence is asynchronous.</summary>
    void Write(string path, byte[] data);

    /// <summary>Writes bytes with explicit contentType ("raw" / "json" / "text").</summary>
    void Write(string path, byte[] data, string contentType);

    /// <summary>Serializes to JSON and then writes.</summary>
    void Write<T>(string path, T value);

    /// <summary>Deletes the specified path; cache is invalidated synchronously, disk persistence is asynchronous.</summary>
    void Delete(string path);

    /// <summary>Checks if the path exists.</summary>
    bool Exists(string path);

    /// <summary>Waits for all enqueued writes to be persisted.</summary>
    Task FlushAsync();

    /// <summary>Begins a transaction. Operations are buffered until Commit persists them all at once.</summary>
    IPackTransaction BeginTransaction();
}