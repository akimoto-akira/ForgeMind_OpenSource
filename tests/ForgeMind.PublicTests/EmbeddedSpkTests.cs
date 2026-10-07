// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;

namespace ForgeMind.PublicTests;

/// <summary>
/// Embedded SPK resource tests (refactor plan §6).
/// Verifies that scan data bundled as an embedded resource in the assembly can be loaded
/// through <see cref="ScanPackLoader.FromEmbeddedResource"/> and queried normally — this is
/// the deployment path for shipped packages (no external .spk file needed).
/// </summary>
internal static class EmbeddedSpkTests
{
    private const string ResourceName = "ForgeMind.PublicTests.test-bundle.spk";

    public static int RunAll()
    {
        int failures = 0;
        failures += TestRunner.Run(nameof(FromEmbeddedResource_LoadsProvider), FromEmbeddedResource_LoadsProvider);
        failures += TestRunner.Run(nameof(FromEmbeddedResource_MatchesFileData), FromEmbeddedResource_MatchesFileData);
        failures += TestRunner.Run(nameof(FromEmbeddedResource_QueriesWork), FromEmbeddedResource_QueriesWork);
        failures += TestRunner.Run(nameof(TryFromEmbeddedResource_MissingReturnsNull), TryFromEmbeddedResource_MissingReturnsNull);
        failures += TestRunner.Run(nameof(FromEmbeddedResource_ManifestReadable), FromEmbeddedResource_ManifestReadable);
        failures += TestRunner.Run(nameof(VersionsFromEmbeddedResource_OnlyOne), VersionsFromEmbeddedResource_OnlyOne);
        return failures;
    }

    // ===== Test: FromEmbeddedResource returns a working provider =====

    private static void FromEmbeddedResource_LoadsProvider()
    {
        using var provider = ScanPackLoader.FromEmbeddedResource(typeof(EmbeddedSpkTests).Assembly, ResourceName);
        TestRunner.True(provider.HasIndexData, "provider should have index data");
    }

    // ===== Test: Embedded SPK yields expected known data =====

    private static void FromEmbeddedResource_MatchesFileData()
    {
        // The embedded test-bundle.spk contains: AActor (UObject), APawn (AActor),
        // and AActor::K2_DestroyActor, AActor::GetActorLocation, APawn::GetController.
        using var provider = ScanPackLoader.FromEmbeddedResource(typeof(EmbeddedSpkTests).Assembly, ResourceName);

        var actors = provider.FindClassesFlexible("AActor");
        TestRunner.Equal(1, actors.Count);
        TestRunner.Equal("AActor", actors[0].Name);
        TestRunner.Equal("UObject", actors[0].SuperClass);

        var pawns = provider.FindClassesFlexible("APawn");
        TestRunner.Equal(1, pawns.Count);
        TestRunner.Equal("APawn", pawns[0].Name);

        bool found = provider.TryFindFunction("AActor", "K2_DestroyActor", 0, null, out _);
        TestRunner.True(found);
    }

    // ===== Test: Can query classes and functions from embedded SPK =====

    private static void FromEmbeddedResource_QueriesWork()
    {
        using var provider = ScanPackLoader.FromEmbeddedResource(typeof(EmbeddedSpkTests).Assembly, ResourceName);

        // AActor has K2_DestroyActor
        bool found = provider.TryFindFunction("AActor", "K2_DestroyActor", 0, null, out var sig);
        TestRunner.True(found);
        TestRunner.NotNull(sig);
        TestRunner.Equal("AActor", sig!.Owner);
        TestRunner.True(sig.IsStatic);

        // APawn is also present
        var pawns = provider.FindClassesFlexible("APawn");
        TestRunner.Equal(1, pawns.Count);
        TestRunner.Equal("APawn", pawns[0].Name);
        TestRunner.Equal("AActor", pawns[0].SuperClass);
    }

    // ===== Test: TryFromEmbeddedResource returns null for missing resource =====

    private static void TryFromEmbeddedResource_MissingReturnsNull()
    {
        var result = ScanPackLoader.TryFromEmbeddedResource(typeof(EmbeddedSpkTests).Assembly, "ForgeMind.PublicTests.nonexistent.spk");
        TestRunner.True(result is null, "should return null for missing resource");
    }

    // ===== Test: Manifest is readable from embedded SPK =====

    private static void FromEmbeddedResource_ManifestReadable()
    {
        using var provider = ScanPackLoader.FromEmbeddedResource(typeof(EmbeddedSpkTests).Assembly, ResourceName);
        IReadOnlyList<ScanRootView> roots = provider.Roots;
        TestRunner.True(roots.Count > 0, "should have roots");
        TestRunner.Equal("/Engine", roots[0].RootPath);
    }

    // ===== Test: Version detection on embedded resource =====

    private static void VersionsFromEmbeddedResource_OnlyOne()
    {
        using var provider = ScanPackLoader.FromEmbeddedResource(typeof(EmbeddedSpkTests).Assembly, ResourceName);
        // The bundle has a single version "5.0"
        var classes = provider.FindClassesFlexible("AActor");
        TestRunner.Equal(1, classes.Count);
        TestRunner.Equal("AActor", classes[0].Name);
        TestRunner.Equal("UObject", classes[0].SuperClass);
    }
}
