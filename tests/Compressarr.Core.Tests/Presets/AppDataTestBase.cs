using Compressarr.Core.Config;

namespace Compressarr.Core.Tests.Presets;

/// <summary>Points AppPaths at a throwaway folder for the life of a test, so a test that exercises
/// the profile store (or anything else that resolves its files through AppPaths) can never touch this
/// machine's real Compressarr data. AppPaths.TestOverrideAppDataDirectory is one static, so every
/// class using it is in the "AppDataOverride" xUnit collection - collections never run in
/// parallel with each other.</summary>
[CollectionDefinition("AppDataOverride")]
public sealed class AppDataOverrideCollection;

[Collection("AppDataOverride")]
public abstract class AppDataTestBase : IDisposable
{
    protected string AppData { get; } = Directory.CreateTempSubdirectory("compressarr-appdata-tests-").FullName;

    protected AppDataTestBase() => AppPaths.TestOverrideAppDataDirectory = AppData;

    public void Dispose()
    {
        AppPaths.TestOverrideAppDataDirectory = null;
        try { Directory.Delete(AppData, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }
}
