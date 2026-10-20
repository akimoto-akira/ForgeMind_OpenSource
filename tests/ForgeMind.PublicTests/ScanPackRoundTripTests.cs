// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.PublicTests;

/// <summary>
/// Round-trip tests: a pack written in the runtime layout ({version}/engine.json) is read back
/// through ScanPackReader / ScanPackLoader.
/// <para>
/// The pack writer itself (ScanResultPacker) was removed with the ScanCache-layout refactor —
/// writing now happens in the engine scanner, which is outside this project's dependency
/// boundary. These tests therefore write the pack through the pack-format contract that
/// ScanPackReader exposes (see <see cref="PackFixture"/>), so the read path — the one the
/// runtime actually uses — stays covered end to end.
/// </para>
/// </summary>
internal static class ScanPackRoundTripTests
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestRunner.Run(nameof(RoundTrip_PreservesRoots), RoundTrip_PreservesRoots);
        failures += TestRunner.Run(nameof(RoundTrip_PreservesClasses), RoundTrip_PreservesClasses);
        failures += TestRunner.Run(nameof(RoundTrip_PreservesFunctions), RoundTrip_PreservesFunctions);
        failures += TestRunner.Run(nameof(RoundTrip_PreservesOutParameter), RoundTrip_PreservesOutParameter);
        return failures;
    }

    private static void RoundTrip_PreservesRoots()
    {
        using var spk = new TempSpk();
        PackFixture.Write(spk.Path, "5.0", PackFixture.Root("/Engine", "5.0",
            PackFixture.Class("AActor", "UObject",
                PackFixture.Function("K2_DestroyActor", true,
                    PackFixture.Parameter("Object;AActor*", "AActor*")))));

        using var provider = ScanPackLoader.FromFile(spk.Path);
        IReadOnlyList<ScanRootView> roots = provider.Roots;

        TestRunner.Equal(1, roots.Count);
        TestRunner.Equal("/Engine", roots[0].RootPath);
        TestRunner.Equal("5.0", roots[0].Version);
    }

    private static void RoundTrip_PreservesClasses()
    {
        using var spk = new TempSpk();
        PackFixture.Write(spk.Path, "5.0", PackFixture.Root("/Engine", "5.0",
            PackFixture.Class("AActor", "UObject")));

        using var provider = ScanPackLoader.FromFile(spk.Path);
        IReadOnlyList<ScanClassView> classes = provider.FindClassesFlexible("AActor");

        TestRunner.Equal(1, classes.Count);
        TestRunner.Equal("AActor", classes[0].Name);
        TestRunner.Equal("UObject", classes[0].SuperClass);
    }

    private static void RoundTrip_PreservesFunctions()
    {
        using var spk = new TempSpk();
        PackFixture.Write(spk.Path, "5.0", PackFixture.Root("/Engine", "5.0",
            PackFixture.Class("AActor", "UObject",
                PackFixture.Function("K2_DestroyActor", true))));

        using var provider = ScanPackLoader.FromFile(spk.Path);
        IReadOnlyList<ScanFunctionView> funcs = provider.CollectFunctions("AActor");

        TestRunner.Equal(1, funcs.Count);
        TestRunner.Equal("K2_DestroyActor", funcs[0].Name);
        TestRunner.True(funcs[0].IsStatic);
    }

    /// <summary>
    /// The out-parameter flag survives the pack round trip: a non-const C++ reference
    /// ("FHitResult&amp;") is an out parameter (output pin), a const reference
    /// ("const TArray&lt;AActor*&gt;&amp;") is not.
    /// </summary>
    private static void RoundTrip_PreservesOutParameter()
    {
        using var spk = new TempSpk();
        PackFixture.Write(spk.Path, "5.0", PackFixture.Root("/Engine", "5.0",
            PackFixture.Class("UKismetSystemLibrary", null,
                PackFixture.Function("LineTraceSingle", true,
                    PackFixture.Parameter("Start", "const FVector"),
                    PackFixture.Parameter("OutHit", "FHitResult&", isOut: true),
                    PackFixture.Parameter("ActorsToIgnore", "const TArray<AActor*>&")))));

        using var provider = ScanPackLoader.FromFile(spk.Path);
        IReadOnlyList<ScanFunctionView> funcs = provider.CollectFunctions("UKismetSystemLibrary");

        TestRunner.Equal(1, funcs.Count);
        TestRunner.Equal(3, funcs[0].Parameters.Count);
        TestRunner.False(funcs[0].Parameters[0].IsOut);
        TestRunner.True(funcs[0].Parameters[1].IsOut);
        TestRunner.False(funcs[0].Parameters[2].IsOut);
    }

    internal sealed class TempSpk : IDisposable
    {
        public string Path { get; }
        public TempSpk() => Path = System.IO.Path.GetTempFileName() + ".spk";
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
