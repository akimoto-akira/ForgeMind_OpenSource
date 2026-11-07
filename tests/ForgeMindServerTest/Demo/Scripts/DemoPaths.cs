// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Demo.Scripts;

/// <summary>
/// Asset locations shared by the episode scripts.
/// <para>Every script writes under one virtual folder so the recorded segments stay together and
/// a re-record never touches the UE5.0 test assets. The on-disk form is what the offline
/// <c>asset</c> tool needs: it takes an absolute <c>.uasset</c> path, while <c>dsl</c> and the
/// editor companion take the <c>/Game/...</c> virtual path.</para>
/// </summary>
internal static class DemoPaths
{
    /// <summary>The virtual folder every episode script writes into.</summary>
    internal const string VirtualRoot = "/Game/Tests/Video/Ep05";

    /// <summary>Virtual (content-relative) path of an asset name.</summary>
    internal static string Virtual(string assetName) => $"{VirtualRoot}/{assetName}";

    /// <summary>Absolute on-disk path of the <c>.uasset</c> the offline asset tool reads.</summary>
    internal static string OnDisk(string assetName) =>
        Path.Combine(BlueprintTestHelper.ContentRoot, "Tests", "Video", "Ep05", assetName + ".uasset");
}
