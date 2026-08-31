using Compressarr.Core.Conversion;
using Compressarr.Core.Reporting;

namespace Compressarr.Core.Tests.Reporting;

public class HtmlReportGeneratorTests
{
    private static ConversionResult Result(string fileName, bool success = true, double beginGb = 1, double endGb = 0.5, string? arrStatus = null, string? failureReason = null) => new()
    {
        LaneId = "lane1",
        FileName = fileName,
        FullName = $@"C:\videos\{fileName}",
        ContentType = "Movie",
        PresetName = "Compressarr SD-HD",
        BeginSizeGb = beginGb,
        EndSizeGb = endGb,
        Success = success,
        ArrStatus = arrStatus,
        FailureReason = failureReason,
        StartTime = DateTime.Now,
        EndTime = DateTime.Now
    };

    private static ReportModel BaseModel(IReadOnlyList<LaneReportSection> lanes, int runNumber = 0) => new()
    {
        GeneratedAt = new DateTime(2026, 3, 15, 10, 30, 0),
        RunTime = TimeSpan.FromMinutes(2),
        RunNumber = runNumber,
        Lanes = lanes
    };

    [Fact]
    public void Generate_EmbedsLogoAndFavicon_AsBase64()
    {
        var model = BaseModel(new List<LaneReportSection>());

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("class=\"logo\"", html);
        Assert.Contains("data:image/x-icon;base64,", html);
    }

    [Theory]
    [InlineData(0, "Run:")]
    [InlineData(7, "Run #7:")]
    public void Generate_RunLabel_MatchesRunNumber(int runNumber, string expectedLabel)
    {
        var model = BaseModel(new List<LaneReportSection>(), runNumber);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains(expectedLabel, html);
    }

    [Fact]
    public void Generate_LaneWithNoResults_ShowsNoFilesProcessedPlaceholder()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "UHD", Results = Array.Empty<ConversionResult>() }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("UHD", html);
        Assert.Contains("No files processed.", html);
    }

    [Fact]
    public void Generate_NoErrors_ShowsOkBanner()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv") } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("Run completed with no errors.", html);
        Assert.DoesNotContain("error(s) occurred", html);
    }

    [Fact]
    public void Generate_WithErrors_ShowsErrorBannerWithCount()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", success: false), Result("b.mkv") } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("1 error(s) occurred", html);
    }

    [Fact]
    public void Generate_FailureReason_ShownInPlaceOfGenericError()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", success: false, failureReason: "Output drive full, monitoring stopped") } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("Output drive full, monitoring stopped", html);
        Assert.DoesNotContain("<td>ERROR</td>", html);
    }

    [Fact]
    public void Generate_FailureWithNoKnownReason_StillShowsGenericError()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", success: false) } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("ERROR", html);
    }

    [Fact]
    public void Generate_FailedFileWithDetailLog_IncludesFullDetailsLink()
    {
        var detailLogPath = Path.Combine(Path.GetTempPath(), $"compressarr-report-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(detailLogPath, "detail log contents");
        try
        {
            var lanes = new List<LaneReportSection>
            {
                new()
                {
                    LaneDisplayName = "HD/SD",
                    Results = new[]
                    {
                        new ConversionResult
                        {
                            LaneId = "lane1", FileName = "a.mkv", FullName = @"C:\videos\a.mkv",
                            ContentType = "Movie", PresetName = "Compressarr SD-HD",
                            BeginSizeGb = 1, EndSizeGb = 0.5, Success = false,
                            FailureReason = "Output drive full, monitoring stopped",
                            DetailLogFile = detailLogPath,
                            StartTime = DateTime.Now, EndTime = DateTime.Now
                        }
                    }
                }
            };
            var model = BaseModel(lanes);

            var html = new HtmlReportGenerator().Generate(model);

            Assert.Contains("Full Details", html);
            Assert.Contains(new Uri(detailLogPath).AbsoluteUri, html);
        }
        finally
        {
            File.Delete(detailLogPath);
        }
    }

    [Fact]
    public void Generate_FileWithNoArrStatus_RendersEmDash()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", arrStatus: null) } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("<td>—</td>", html);
    }

    [Fact]
    public void Generate_HistorySection_IncludesSavingsPercentColumn()
    {
        var model = new ReportModel
        {
            GeneratedAt = new DateTime(2026, 3, 15, 10, 30, 0),
            RunTime = TimeSpan.FromMinutes(2),
            RunNumber = 0,
            Lanes = new List<LaneReportSection>(),
            Today = new HistoryRollup(FileCount: 2, BeforeGb: 10, AfterGb: 4)
        };

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("<th>Savings</th>", html);
        Assert.Contains("60%", html); // (10-4)/10 = 60% saved
    }
}
