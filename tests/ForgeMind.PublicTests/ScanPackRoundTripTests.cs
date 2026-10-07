// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;
using ForgeMind.Speedy;

namespace ForgeMind.PublicTests;

/// <summary>
/// Round-trip tests: ScanResultPacker writes scan data to SPK, ScanPackReader reads it back.
/// Verifies the write/read symmetry that the runtime relies on (refactor plan §15/§16).
/// </summary>
internal static class ScanPackRoundTripTests
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestRunner.Run(nameof(RoundTrip_PreservesRoots), RoundTrip_PreservesRoots);
        failures += TestRunner.Run(nameof(RoundTrip_PreservesClasses), RoundTrip_PreservesClasses);
        failures += TestRunner.Run(nameof(RoundTrip_PreservesFunctions), RoundTrip_PreservesFunctions);
        return failures;
    }

    private static void RoundTrip_PreservesRoots()
    {
        using var spk = new TempSpk();
        var (result, data) = BuildSampleScanResult();
        WriteResult(spk.Path, result);

        using var provider = ScanPackLoader.FromFile(spk.Path);
        IReadOnlyList<ScanRootView> roots = provider.Roots;

        TestRunner.Equal(1, roots.Count);
        TestRunner.Equal(data.RootList[0].RootPath, roots[0].RootPath);
        TestRunner.Equal(data.RootList[0].Version, roots[0].Version);
    }

    private static void RoundTrip_PreservesClasses()
    {
        using var spk = new TempSpk();
        var (result, _) = BuildSampleScanResult();
        WriteResult(spk.Path, result);

        using var provider = ScanPackLoader.FromFile(spk.Path);
        IReadOnlyList<ScanClassView> classes = provider.FindClassesFlexible("AActor");

        TestRunner.Equal(1, classes.Count);
        TestRunner.Equal("AActor", classes[0].Name);
        TestRunner.Equal("UObject", classes[0].SuperClass);
    }

    private static void RoundTrip_PreservesFunctions()
    {
        using var spk = new TempSpk();
        var (result, _) = BuildSampleScanResult();
        WriteResult(spk.Path, result);

        using var provider = ScanPackLoader.FromFile(spk.Path);
        IReadOnlyList<ScanFunctionView> funcs = provider.CollectFunctions("AActor");

        TestRunner.Equal(1, funcs.Count);
        TestRunner.Equal("K2_DestroyActor", funcs[0].Name);
        TestRunner.True(funcs[0].IsStatic);
    }

    // ===== helpers =====

    internal static (IScanResult, ScanData) BuildSampleScanResult()
    {
        var data = new ScanData();
        data.RootList.Add(new ScanRootView("/Engine", "5.0", ["Core"], []));
        data.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data.FunctionMap["AActor"] =
        [
            new ScanFunctionView(
                "K2_DestroyActor", true, false,
                [new ScanParameterView("Object;AActor*", "AActor*")],
                "/Engine/Source/Runtime/GameFramework/Actor.h", 42, "void"),
        ];

        var manifest = new ScanManifest
        {
            ScannerId = "TestScanner",
            ScannerVersion = "1.0",
            TargetType = "TestTarget",
            TargetVersions = ["5.0"],
            DataTypes = [ScanDataTypes.Code],
        };

        var index = new ScanIndex();
        index.Add(new ScanEntry { Id = "code/classes/AActor", Version = "5.0", DataType = "code" });

        return (new SimpleScanResult(manifest, index, data), data);
    }

    internal static void WriteResult(string path, IScanResult result)
    {
        using var pack = SpeedyPack.Open(path);
        var packer = new ScanResultPacker(pack);
        packer.Pack(result);
    }

    internal sealed class SimpleScanResult(ScanManifest m, ScanIndex i, IScanData d) : IScanResult
    {
        public ScanManifest Manifest { get; } = m;
        public ScanIndex Index { get; } = i;
        public IScanData Data { get; } = d;
    }

    internal sealed class TempSpk : IDisposable
    {
        public string Path { get; }
        public TempSpk() => Path = System.IO.Path.GetTempFileName() + ".spk";
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}