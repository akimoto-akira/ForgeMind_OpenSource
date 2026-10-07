// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using ForgeMind.Scan;
using ForgeMind.Scan.Abstractions;

namespace ForgeMind.PublicTests;

/// <summary>
/// Query tests for SpkScanDataProvider (refactor plan §16).
/// Exercises FindClassesFlexible, CollectFunctions, TryFindFunction — the core
/// IScanDataProvider contract as served from SPK data.
/// </summary>
internal static class SpkScanDataProviderTests
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestRunner.Run(nameof(HasIndexData_True), HasIndexData_True);
        failures += TestRunner.Run(nameof(FindClassesFlexible_ExactMatch), FindClassesFlexible_ExactMatch);
        failures += TestRunner.Run(nameof(FindClassesFlexible_PrefixTolerance), FindClassesFlexible_PrefixTolerance);
        failures += TestRunner.Run(nameof(FindClassesFlexible_NoMatch), FindClassesFlexible_NoMatch);
        failures += TestRunner.Run(nameof(CollectFunctions_IncludesInherited), CollectFunctions_IncludesInherited);
        failures += TestRunner.Run(nameof(TryFindFunction_Matches), TryFindFunction_Matches);
        failures += TestRunner.Run(nameof(TryFindFunction_NotFound), TryFindFunction_NotFound);
        failures += TestRunner.Run(nameof(TryFindFunctionOwner_Resolves), TryFindFunctionOwner_Resolves);
        failures += TestRunner.Run(nameof(Roots_ReturnsAll), Roots_ReturnsAll);
        return failures;
    }

    private static SpkScanDataProvider CreateProvider()
    {
        var data = new ScanData();
        data.RootList.Add(new ScanRootView("/Engine", "5.0", ["Core"], []));
        data.ClassList.Add(new ScanClassView("AActor", "UObject", ["UObject"], 10));
        data.ClassList.Add(new ScanClassView("APawn", "AActor", ["AActor"], 9));
        data.FunctionMap["AActor"] =
        [
            new ScanFunctionView("K2_DestroyActor", true, false, [], "Actor.h", 42, "void"),
            new ScanFunctionView("GetActorLocation", false, true, [], "Actor.h", 100, "FVector"),
        ];
        data.FunctionMap["APawn"] =
        [
            new ScanFunctionView("GetController", false, false, [], "Pawn.h", 50, "AController*"),
        ];
        return new SpkScanDataProvider(data);
    }

    private static void HasIndexData_True()
    {
        using var provider = CreateProvider();
        TestRunner.True(provider.HasIndexData);
    }

    private static void FindClassesFlexible_ExactMatch()
    {
        using var provider = CreateProvider();
        var result = provider.FindClassesFlexible("AActor");
        TestRunner.Equal(1, result.Count);
        TestRunner.Equal("AActor", result[0].Name);
    }

    private static void FindClassesFlexible_PrefixTolerance()
    {
        using var provider = CreateProvider();
        var result = provider.FindClassesFlexible("Actor");
        TestRunner.Equal(1, result.Count);
        TestRunner.Equal("AActor", result[0].Name);
    }

    private static void FindClassesFlexible_NoMatch()
    {
        using var provider = CreateProvider();
        var result = provider.FindClassesFlexible("NonExistent");
        TestRunner.Equal(0, result.Count);
    }

    private static void CollectFunctions_IncludesInherited()
    {
        using var provider = CreateProvider();
        var funcs = provider.CollectFunctions("APawn");
        TestRunner.Equal(3, funcs.Count);
        TestRunner.Contains(funcs, f => f.Name == "GetController");
        TestRunner.Contains(funcs, f => f.Name == "K2_DestroyActor");
        TestRunner.Contains(funcs, f => f.Name == "GetActorLocation");
    }

    private static void TryFindFunction_Matches()
    {
        using var provider = CreateProvider();
        bool found = provider.TryFindFunction("AActor", "K2_DestroyActor", 0, null, out var sig);
        TestRunner.True(found);
        TestRunner.NotNull(sig);
        TestRunner.Equal("AActor", sig!.Owner);
        TestRunner.Equal("K2_DestroyActor", sig.Name);
        TestRunner.True(sig.IsStatic);
    }

    private static void TryFindFunction_NotFound()
    {
        using var provider = CreateProvider();
        bool found = provider.TryFindFunction("AActor", "NonExistent", 0, null, out _);
        TestRunner.False(found);
    }

    private static void TryFindFunctionOwner_Resolves()
    {
        using var provider = CreateProvider();
        var lookup = provider.TryFindFunctionOwner("K2_DestroyActor", null, null, out string owner);
        TestRunner.Equal(ScanOwnerLookup.Resolved, lookup);
        TestRunner.Equal("AActor", owner);
    }

    private static void Roots_ReturnsAll()
    {
        using var provider = CreateProvider();
        var roots = provider.Roots;
        TestRunner.Equal(1, roots.Count);
        TestRunner.Equal("/Engine", roots[0].RootPath);
        TestRunner.Equal("5.0", roots[0].Version);
    }
}