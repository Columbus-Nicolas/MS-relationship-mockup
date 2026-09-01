using Xunit;

// DevAuthTests switches dev mode on through a *process* environment variable, because that is the
// only configuration source that exists early enough for Program.cs to read it — see DevModeGuard
// for the full explanation. A process variable is global to the test run, so it would otherwise be
// visible to any test executing concurrently in another collection, and several of those build
// their own hosts. Serialising the assembly removes that race outright. The suite is a few seconds
// long and mostly shares one Postgres container already, so there is very little to lose.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
