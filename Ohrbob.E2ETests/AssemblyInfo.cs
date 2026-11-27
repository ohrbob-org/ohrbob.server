using NUnit.Framework;

// Fixtures own their web host and their fake identity now, so independent
// classes can run at the same time. Classes that drive the shared
// podcast-testcasts server remain [NonParallelizable] and run on NUnit's
// non-parallel queue, isolated from everything below.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(6)]
