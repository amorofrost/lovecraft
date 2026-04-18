using System;
using System.Runtime.CompilerServices;
using Xunit;

// MockDataStore is a shared static singleton mutated by many test classes.
// Running collections in parallel races on non-concurrent dictionaries/lists.
// Disabling cross-collection parallelization keeps the existing suite correct
// while still executing in well under a minute end-to-end.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lovecraft.UnitTests;

// Program.cs fails fast when JWT_SECRET_KEY is unset. Tests that bootstrap the
// full app via WebApplicationFactory<Program> (AclTests, RateLimitingTests, ...)
// would otherwise blow up at startup. Seed a deterministic test secret once,
// before any test code runs, and only if the developer hasn't supplied their
// own value (so CI can still override via env).
internal static class TestAssemblyInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JWT_SECRET_KEY")))
        {
            Environment.SetEnvironmentVariable(
                "JWT_SECRET_KEY",
                "test-jwt-secret-key-minimum-32-characters-long-for-hs256-tests");
        }
    }
}
