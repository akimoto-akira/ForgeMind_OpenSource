// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.PublicTests;

/// <summary>
/// Public test entry point (refactor plan §20). Runs open-source component tests
/// — Scan/Speedy round-trip and query tests. No UE, no ForgeMindServer, no commercial
/// dependencies. Exits 0 when all pass, 1 on any failure.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("ForgeMind Public Tests");
        Console.WriteLine("======================");

        int failures = 0;
        Console.WriteLine("\n[ScanPack Round-Trip]");
        failures += ScanPackRoundTripTests.RunAll();

        Console.WriteLine("\n[SpkScanDataProvider Queries]");
        failures += SpkScanDataProviderTests.RunAll();

        Console.WriteLine("\n[SPK Multi-Version Isolation]");
        failures += SpkMultiVersionTests.RunAll();

        Console.WriteLine("\n[Embedded SPK Resource]");
        failures += EmbeddedSpkTests.RunAll();

        return TestRunner.Summary();
    }
}