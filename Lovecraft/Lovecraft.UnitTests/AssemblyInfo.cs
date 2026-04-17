using Xunit;

// MockDataStore is a shared static singleton mutated by many test classes.
// Running collections in parallel races on non-concurrent dictionaries/lists.
// Disabling cross-collection parallelization keeps the existing suite correct
// while still executing in well under a minute end-to-end.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
