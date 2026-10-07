// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.PublicTests;

/// <summary>
/// SPK multi-version isolation tests (refactor plan §6).
/// Verifies that a single SPK containing multiple UE version scopes (e.g. 5.0 and 5.8)
/// can be queried per-version without cross-version contamination — querying "5.0" must
/// never return "5.8" data, and same-named classes stay within their own version scope.
/// </summary>
internal static class SpkMultiVersionTests
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestRunner.Run(nameof(PackMany_TwoVersions_BothPresent), PackMany_TwoVersions_BothPresent);
        failures += TestRunner.Run(nameof(CreateVersionScope_50_OnlyReturns50Data), CreateVersionScope_50_OnlyReturns50Data);
        failures += TestRunner.Run(nameof(CreateVersionScope_58_OnlyReturns58Data), CreateVersionScope_58_OnlyReturns58Data);
        failures += TestRunner.Run(nameof(CreateVersionScope_SameNameClass_Isolated), CreateVersionScope_SameNameClass_Isolated);
        failures += TestRunner.Run(nameof(ListVersions_ReturnsBoth), ListVersions_ReturnsBoth);
        failures += TestRunner.Run(nameof(FromFileVersionScoped_QueriesCorrectVersion), FromFileVersionScoped_QueriesCorrectVersion);
        failures += TestRunner.Run(nameof(DefaultConstructor_LoadsFirstVersion), DefaultConstructor_LoadsFirstVersion);
        return failures;
    }

    // ===== Test: PackMany writes both versions and the merged manifest lists both =====

    private static void PackMany_TwoVersions_BothPresent()
    {
        using var spk = new TempSpk();

        var data50 = new ScanData();
        data50.RootList.Add(new ScanRootView("/Engine/5.0", "5.0", ["Core"], []));
        data50.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data50.FunctionMap["AActor"] = [new ScanFunctionView("K2_GetActorLocation", false, true, [], "Actor.h", 100, "FVector")];

        var data58 = new ScanData();
        data58.RootList.Add(new ScanRootView("/Engine/5.8", "5.8", ["Core"], []));
        data58.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data58.FunctionMap["AActor"] = [new ScanFunctionView("GetActorLocation", false, true, [], "Actor.h", 200, "FVector")];

        var manifest50 = new ScanManifest
        {
            ScannerId = "TestScanner", ScannerVersion = "1.0",
            TargetType = "UnrealEngine", TargetVersions = ["5.0"],
            DataTypes = [ScanDataTypes.Code],
        };
        var manifest58 = new ScanManifest
        {
            ScannerId = "TestScanner", ScannerVersion = "1.0",
            TargetType = "UnrealEngine", TargetVersions = ["5.8"],
            DataTypes = [ScanDataTypes.Code],
        };

        using (var pack = SpeedyPack.Open(spk.Path))
        {
            var packer = new ScanResultPacker(pack);
            packer.PackMany([new SimpleScanResult(manifest50, new ScanIndex(), data50), new SimpleScanResult(manifest58, new ScanIndex(), data58)]);
        }

        // Verify both versions exist in the pack
        IReadOnlyList<string> versions = ScanPackLoader.ListVersions(spk.Path);
        TestRunner.Equal(2, versions.Count);
        TestRunner.True(versions.Contains("5.0"));
        TestRunner.True(versions.Contains("5.8"));
    }

    // ===== Test: CreateVersionScope("5.0") only returns 5.0 data =====

    private static void CreateVersionScope_50_OnlyReturns50Data()
    {
        using var spk = new TempSpk();
        WriteTwoVersionPack(spk.Path);

        using var provider = ScanPackLoader.FromFileVersionScoped(spk.Path, "5.0");

        // 5.0 AActor has K2_GetActorLocation
        bool found = provider.TryFindFunction("AActor", "K2_GetActorLocation", 0, null, out _);
        TestRunner.True(found);

        // 5.0 should NOT have 5.8's GetActorLocation
        bool found58 = provider.TryFindFunction("AActor", "GetActorLocation", 0, null, out _);
        TestRunner.False(found58);
    }

    // ===== Test: CreateVersionScope("5.8") only returns 5.8 data =====

    private static void CreateVersionScope_58_OnlyReturns58Data()
    {
        using var spk = new TempSpk();
        WriteTwoVersionPack(spk.Path);

        using var provider = ScanPackLoader.FromFileVersionScoped(spk.Path, "5.8");

        // 5.8 AActor has GetActorLocation
        bool found = provider.TryFindFunction("AActor", "GetActorLocation", 0, null, out _);
        TestRunner.True(found);

        // 5.8 should NOT have 5.0's K2_GetActorLocation
        bool found50 = provider.TryFindFunction("AActor", "K2_GetActorLocation", 0, null, out _);
        TestRunner.False(found50);
    }

    // ===== Test: Same-named class AActor exists in both versions but stays isolated =====

    private static void CreateVersionScope_SameNameClass_Isolated()
    {
        using var spk = new TempSpk();

        // 5.0: AActor with 1 function
        var data50 = new ScanData();
        data50.RootList.Add(new ScanRootView("/Engine/5.0", "5.0", ["Core"], []));
        data50.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data50.FunctionMap["AActor"] = [new ScanFunctionView("Func50", false, false, [], "A.h", 1, "void")];

        // 5.8: AActor with 2 functions
        var data58 = new ScanData();
        data58.RootList.Add(new ScanRootView("/Engine/5.8", "5.8", ["Core"], []));
        data58.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data58.FunctionMap["AActor"] =
        [
            new ScanFunctionView("Func50", false, false, [], "A.h", 1, "void"),
            new ScanFunctionView("Func58", false, false, [], "A.h", 2, "void"),
        ];

        var manifest50 = new ScanManifest { ScannerId = "T", ScannerVersion = "1", TargetType = "UE", TargetVersions = ["5.0"], DataTypes = [ScanDataTypes.Code] };
        var manifest58 = new ScanManifest { ScannerId = "T", ScannerVersion = "1", TargetType = "UE", TargetVersions = ["5.8"], DataTypes = [ScanDataTypes.Code] };

        using (var pack = SpeedyPack.Open(spk.Path))
        {
            new ScanResultPacker(pack).PackMany([new SimpleScanResult(manifest50, new ScanIndex(), data50), new SimpleScanResult(manifest58, new ScanIndex(), data58)]);
        }

        // 5.0 scope: only 1 function
        using (var p50 = ScanPackLoader.FromFileVersionScoped(spk.Path, "5.0"))
        {
            var funcs = p50.CollectFunctions("AActor");
            TestRunner.Equal(1, funcs.Count);
            TestRunner.Equal("Func50", funcs[0].Name);
        }

        // 5.8 scope: 2 functions
        using (var p58 = ScanPackLoader.FromFileVersionScoped(spk.Path, "5.8"))
        {
            var funcs = p58.CollectFunctions("AActor");
            TestRunner.Equal(2, funcs.Count);
        }
    }

    // ===== Test: ListVersions discovers both version namespaces =====

    private static void ListVersions_ReturnsBoth()
    {
        using var spk = new TempSpk();
        WriteTwoVersionPack(spk.Path);

        var versions = ScanPackLoader.ListVersions(spk.Path);
        TestRunner.Equal(2, versions.Count);
        TestRunner.Contains(versions, v => v == "5.0");
        TestRunner.Contains(versions, v => v == "5.8");
    }

    // ===== Test: FromFileVersionScoped uses version-scoped API =====

    private static void FromFileVersionScoped_QueriesCorrectVersion()
    {
        using var spk = new TempSpk();
        WriteTwoVersionPack(spk.Path);

        using var provider = ScanPackLoader.FromFileVersionScoped(spk.Path, "5.8");
        var cls = provider.FindClassesFlexible("AActor");
        TestRunner.Equal(1, cls.Count);
        TestRunner.Equal("AActor", cls[0].Name);
    }

    // ===== Test: Default constructor loads only the first version (not LoadAll) =====

    private static void DefaultConstructor_LoadsFirstVersion()
    {
        using var spk = new TempSpk();
        WriteTwoVersionPack(spk.Path);

        // Default constructor loads Versions[0] (which is "5.0" since versions are sorted)
        using var provider = ScanPackLoader.FromFile(spk.Path);

        // Should find 5.0's function, not 5.8's
        bool found50 = provider.TryFindFunction("AActor", "K2_GetActorLocation", 0, null, out _);
        bool found58 = provider.TryFindFunction("AActor", "GetActorLocation", 0, null, out _);
        TestRunner.True(found50);
        TestRunner.False(found58);
    }

    // ===== helpers =====

    private static void WriteTwoVersionPack(string path)
    {
        var data50 = new ScanData();
        data50.RootList.Add(new ScanRootView("/Engine/5.0", "5.0", ["Core"], []));
        data50.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data50.FunctionMap["AActor"] = [new ScanFunctionView("K2_GetActorLocation", false, true, [], "Actor.h", 100, "FVector")];

        var data58 = new ScanData();
        data58.RootList.Add(new ScanRootView("/Engine/5.8", "5.8", ["Core"], []));
        data58.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data58.FunctionMap["AActor"] = [new ScanFunctionView("GetActorLocation", false, true, [], "Actor.h", 200, "FVector")];

        var manifest50 = new ScanManifest { ScannerId = "T", ScannerVersion = "1", TargetType = "UE", TargetVersions = ["5.0"], DataTypes = [ScanDataTypes.Code] };
        var manifest58 = new ScanManifest { ScannerId = "T", ScannerVersion = "1", TargetType = "UE", TargetVersions = ["5.8"], DataTypes = [ScanDataTypes.Code] };

        using var pack = SpeedyPack.Open(path);
        new ScanResultPacker(pack).PackMany([new SimpleScanResult(manifest50, new ScanIndex(), data50), new SimpleScanResult(manifest58, new ScanIndex(), data58)]);
    }

    private sealed class SimpleScanResult(ScanManifest m, ScanIndex i, IScanData d) : IScanResult
    {
        public ScanManifest Manifest { get; } = m;
        public ScanIndex Index { get; } = i;
        public IScanData Data { get; } = d;
    }

    private sealed class TempSpk : IDisposable
    {
        public string Path { get; }
        public TempSpk() => Path = System.IO.Path.GetTempFileName() + ".spk";
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
