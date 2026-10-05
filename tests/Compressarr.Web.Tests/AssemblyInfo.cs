// AppPaths.TestOverrideAppDataDirectory is a process-wide static (it is what keeps these tests away
// from the real Compressarr data folder), so tests that use it must never run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
