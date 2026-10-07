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

using MessagePack;

namespace ForgeMind.Speedy.Internal;

/// <summary>
/// The single MessagePack option set used for every .spk structure (Directory, WAL payloads).
/// <para>
/// These options must not live behind <c>SpeedyPack</c>'s static constructor. A reader that
/// never touches <c>SpeedyPack</c> — for example a plugin loading an embedded pack straight
/// through <c>PackFileReader</c> — would otherwise fall back to
/// <see cref="MessagePackSerializer.DefaultOptions"/> without LZ4, and fail on a compressed
/// Directory with "Unexpected msgpack code 200 (ext 16)". Writer and reader must agree on the
/// options unconditionally.</para>
/// <para>
/// LZ4 is detected by the extension type code on read, so these options also deserialize
/// payloads that were written uncompressed (small directories, legacy v1 data).</para>
/// </summary>
internal static class PackOptions
{
    public static MessagePackSerializerOptions Serializer { get; } =
        MessagePackSerializerOptions.Standard
            .WithResolver(MessagePack.Resolvers.CompositeResolver.Create(
                MessagePack.Resolvers.BuiltinResolver.Instance,
                MessagePack.Resolvers.ContractlessStandardResolver.Instance))
            .WithCompression(MessagePackCompression.Lz4Block);
}
