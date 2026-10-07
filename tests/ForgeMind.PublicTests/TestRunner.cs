// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMind.PublicTests;

/// <summary>
/// Minimal test harness — no external NuGet dependencies (refactor plan §20).
/// Same pattern as ForgeMindServerTest: Exe entry + simple Assert.
/// </summary>
internal static class TestRunner
{
    private static int _passed;
    private static int _failed;

    public static int Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"  [PASS] {name}");
            _passed++;
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
            _failed++;
            return 1;
        }
    }

    public static int Summary()
    {
        Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
        return _failed > 0 ? 1 : 0;
    }

    public static void True(bool condition, string message = "expected true")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message = "expected false")
    {
        if (condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T? actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected '{expected}', got '{actual}'");
    }

    public static void NotNull<T>(T? value) where T : class
    {
        if (value is null) throw new InvalidOperationException("expected non-null");
    }

    public static void Contains<T>(IEnumerable<T> source, Func<T, bool> predicate, string message = "item not found")
    {
        True(source.Any(predicate), message);
    }
}