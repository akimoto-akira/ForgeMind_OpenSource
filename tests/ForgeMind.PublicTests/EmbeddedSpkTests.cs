// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using System.Reflection;
using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;

namespace ForgeMind.PublicTests;

/// <summary>
/// Stream-backed SPK loading tests (refactor plan §6) — the deployment path for shipped
/// packages, where the bundle is read from a non-file source such as an embedded resource
/// (ManifestResourceStream) instead of from disk.
/// <para>
/// Historically these tests used an embedded fixture named "test-bundle.spk". That file was
/// never committed, and the pack writer (ScanResultPacker) was removed with the
/// ScanCache-layout refactor — writing now lives in the engine scanner, outside this
/// project's dependency boundary. The bundle is therefore produced in-test (see
/// <see cref="PackFixture"/>) and loaded through <see cref="ScanPackLoader.FromStream"/>,
/// which is the same reader chain <c>FromEmbeddedResource</c> uses. Resource-name resolution
/// itself is covered by the negative test below (missing resource → null).
/// </para>
/// </summary>
internal static class EmbeddedSpkTests
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestRunner.Run(nameof(FromStream_LoadsProvider), FromStream_LoadsProvider);
        failures += TestRunner.Run(nameof(FromStream_MatchesBundleData), FromStream_MatchesBundleData);
        failures += TestRunner.Run(nameof(FromStream_QueriesWork), FromStream_QueriesWork);
        failures += TestRunner.Run(nameof(TryFromEmbeddedResource_MissingReturnsNull), TryFromEmbeddedResource_MissingReturnsNull);
        failures += TestRunner.Run(nameof(FromStream_RootsReadable), FromStream_RootsReadable);
        failures += TestRunner.Run(nameof(FromStream_SingleVersion), FromStream_SingleVersion);
        return failures;
    }

    // ===== Test: a stream-backed pack yields a working provider =====

    private static void FromStream_LoadsProvider()
    {
        using var spk = new TempSpk();
        using var provider = OpenBundle(spk.Path);
        TestRunner.True(provider.HasIndexData, "provider should have index data");
    }

    // ===== Test: the bundle yields its known data =====

    private static void FromStream_MatchesBundleData()
    {
        // The bundle contains: AActor (UObject), APawn (AActor), and AActor::K2_DestroyActor,
        // AActor::GetActorLocation, APawn::GetController.
        using var spk = new TempSpk();
        using var provider = OpenBundle(spk.Path);

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

    // ===== Test: classes and functions are queryable from the stream-backed pack =====

    private static void FromStream_QueriesWork()
    {
        using var spk = new TempSpk();
        using var provider = OpenBundle(spk.Path);

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

    // ===== Test: roots are readable from the stream-backed pack =====

    private static void FromStream_RootsReadable()
    {
        using var spk = new TempSpk();
        using var provider = OpenBundle(spk.Path);

        IReadOnlyList<ScanRootView> roots = provider.Roots;
        TestRunner.True(roots.Count > 0, "should have roots");
        TestRunner.Equal("/Engine", roots[0].RootPath);
    }

    // ===== Test: the bundle carries a single version ("5.0") =====

    private static void FromStream_SingleVersion()
    {
        using var spk = new TempSpk();
        using var provider = OpenBundle(spk.Path);

        var classes = provider.FindClassesFlexible("AActor");
        TestRunner.Equal(1, classes.Count);
        TestRunner.Equal("AActor", classes[0].Name);
        TestRunner.Equal("UObject", classes[0].SuperClass);
    }

    // ===== helpers =====

    /// <summary>
    /// Writes the bundle fixture to a temp file and opens it through the stream-backed reader
    /// (ManifestResourceStream-equivalent path).
    /// </summary>
    private static SpkScanDataProvider OpenBundle(string path)
    {
        PackFixture.Write(path, "5.0", PackFixture.Root("/Engine", "5.0",
            PackFixture.Class("AActor", "UObject",
                PackFixture.Function("K2_DestroyActor", true),
                PackFixture.Function("GetActorLocation", false)),
            PackFixture.Class("APawn", "AActor",
                PackFixture.Function("GetController", false))));

        return ScanPackLoader.FromStream(File.OpenRead(path));
    }

    private sealed class TempSpk : IDisposable
    {
        public string Path { get; }
        public TempSpk() => Path = System.IO.Path.GetTempFileName() + ".spk";
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
