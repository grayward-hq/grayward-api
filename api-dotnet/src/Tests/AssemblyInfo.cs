using Xunit;

// Integration tests must not run in parallel. Each VulnWatchWebAppFactory starts its own PostgreSQL
// container and publishes its connection string through an environment variable, which is
// process-global — two collections starting at once overwrite each other's, and whichever loses
// points its host at a container it does not own. That produced 17 failures in a full run while the
// same tests passed when their class ran alone.
//
// Serialising the assembly is the honest fix while the connection string travels via the
// environment. It has to: Program.cs reads configuration as it executes, before the factory's
// ConfigureAppConfiguration is applied, so there is no per-host channel to use instead.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
