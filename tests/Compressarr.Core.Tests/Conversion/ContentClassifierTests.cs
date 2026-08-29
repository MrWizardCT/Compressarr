using Compressarr.Core.Conversion;

namespace Compressarr.Core.Tests.Conversion;

public class ContentClassifierTests
{
    [Theory]
    [InlineData("Show.Name.S01E01.mkv", true, "01", "01")]
    [InlineData("Show.Name.S01E123.mkv", true, "01", "123")]
    [InlineData("Show.Name.S123E01.mkv", true, "123", "01")]
    [InlineData("Show.Name.1x01.mkv", true, "1", "01")]
    public void GetEpisodeInfo_TvFilenames_ExtractsSeasonAndEpisode(string fileName, bool expectedHasBoth, string expectedSeason, string expectedEpisode)
    {
        var info = ContentClassifier.GetEpisodeInfo(fileName);

        Assert.Equal(expectedHasBoth, info.HasSeasonAndEpisode);
        Assert.Equal(expectedSeason, info.Season);
        Assert.Equal(expectedEpisode, info.Episode);
    }

    [Theory]
    [InlineData("Caddyshack (1980).mkv")]
    [InlineData("Some Movie Title.mp4")]
    public void GetEpisodeInfo_MovieFilenames_HasNoSeasonEpisode(string fileName)
    {
        var info = ContentClassifier.GetEpisodeInfo(fileName);

        Assert.False(info.HasSeasonAndEpisode);
    }

    [Fact]
    public void IsTvFile_MatchesHasSeasonAndEpisode()
    {
        Assert.True(ContentClassifier.IsTvFile("Show.S01E01.mkv"));
        Assert.False(ContentClassifier.IsTvFile("Caddyshack (1980).mkv"));
    }

    [Theory]
    [InlineData("Caddyshack (1980) {edition-Director's Cut}.mkv", "Caddyshack (1980)")]
    [InlineData("Caddyshack (1980).mkv", "Caddyshack (1980)")]
    [InlineData("No Year Tag At All.mkv", "No Year Tag At All")]
    public void GetMovieFolderName_ExtractsThroughYearTag(string fileName, string expected)
    {
        var result = ContentClassifier.GetMovieFolderName(fileName);

        Assert.Equal(expected, result);
    }
}
