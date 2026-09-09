using System.Net;
using Compressarr.Core.Conversion;
using Compressarr.Core.Reporting;

namespace Compressarr.Core.Tests.Reporting;

public class HtmlReportGeneratorTests
{
    private static ConversionResult Result(string fileName, bool success = true, double beginGb = 1, double endGb = 0.5, string? arrStatus = null, string? failureReason = null, ReportErrorCode? errorCode = null, string? postProcessWarning = null, DateTime? startTime = null, DateTime? endTime = null) => new()
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
        ErrorCode = errorCode,
        PostProcessWarning = postProcessWarning,
        StartTime = startTime ?? DateTime.Now,
        EndTime = endTime ?? DateTime.Now
    };

    private static ReportModel BaseModel(IReadOnlyList<LaneReportSection> lanes, int runNumber = 0, string? summaryLogFilePath = null) => new()
    {
        GeneratedAt = new DateTime(2026, 3, 15, 10, 30, 0),
        RunTime = TimeSpan.FromMinutes(2),
        RunNumber = runNumber,
        Lanes = lanes,
        SummaryLogFilePath = summaryLogFilePath
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
    public void Generate_ErrorCode_ShownAsCompactBadgeWithFullDescriptionInTooltip()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", success: false, failureReason: "Output drive full, monitoring stopped", errorCode: ReportErrorCode.MoveDiskFull) } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        // The report cell itself stays compact ("ERROR 103") - the full description lives only in
        // the title attribute (a native help-bubble), not inline in the table.
        Assert.Contains("ERROR 103", html);
        Assert.Contains(WebUtility.HtmlEncode(ReportErrorCode.MoveDiskFull.Describe()), html);
        Assert.DoesNotContain("<td>ERROR</td>", html);
    }

    [Fact]
    public void Generate_MoveFailure_LinksTheCompressarrLogInsteadOfTheIrrelevantHandBrakeLog()
    {
        // Real bug found live, in two parts: (1) a move failure (the video converted fine, only
        // filing it failed) always linked HandBrake's own detail log, which has nothing to do with
        // why the move failed and was actively misleading; (2) simply removing that link left no
        // way to see the real error at all - it should link this run's own Compressarr summary
        // log instead, which has the actual exception message for this file.
        var detailLogPath = Path.Combine(Path.GetTempPath(), $"compressarr-report-test-hb-{Guid.NewGuid():N}.txt");
        var summaryLogPath = Path.Combine(Path.GetTempPath(), $"compressarr-report-test-summary-{Guid.NewGuid():N}.txt");
        File.WriteAllText(detailLogPath, "detail log contents - irrelevant to a move failure");
        File.WriteAllText(summaryLogPath, "Move skipped: The user name or password is incorrect.");
        try
        {
            var result = new ConversionResult
            {
                LaneId = "lane1", FileName = "a.mkv", FullName = @"C:\videos\a.mkv",
                ContentType = "Movie", PresetName = "Compressarr SD-HD",
                BeginSizeGb = 1, EndSizeGb = 0.5, Success = false,
                ErrorCode = ReportErrorCode.MoveFailedOther,
                DetailLogFile = detailLogPath,
                StartTime = DateTime.Now, EndTime = DateTime.Now
            };
            var lanes = new List<LaneReportSection>
            {
                new() { LaneDisplayName = "HD/SD", Results = new[] { result } }
            };
            var model = BaseModel(lanes, summaryLogFilePath: summaryLogPath);

            var html = new HtmlReportGenerator().Generate(model);

            Assert.Contains("ERROR 104", html);
            Assert.Contains("Full Details", html);
            Assert.Contains(new Uri(summaryLogPath).AbsoluteUri, html);
            Assert.DoesNotContain(new Uri(detailLogPath).AbsoluteUri, html);
        }
        finally
        {
            File.Delete(detailLogPath);
            File.Delete(summaryLogPath);
        }
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
    public void Generate_PostProcessWarning_ShownDistinctlyFromError()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", success: true, postProcessWarning: "Companion files not moved: Access denied") } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("Companion files not moved: Access denied", html);
        Assert.Contains("class=\"warn\"", html);
        // A post-process warning is real, but the conversion itself still succeeded - it must not
        // read as a full failure: no error-row styling, and the row still says "OK".
        Assert.DoesNotContain("class=\"err\"", html);
        Assert.Contains("<td>OK<br>", html);
    }

    [Fact]
    public void Generate_PostProcessWarning_DoesNotCountTowardErrorBanner()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", success: true, postProcessWarning: "Sonarr/Radarr unmonitor failed: timed out") } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("Run completed with no errors.", html);
        Assert.DoesNotContain("error(s) occurred", html);
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
                            ErrorCode = ReportErrorCode.EncodeFailed,
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

    [Fact]
    public void Generate_PerFileTable_IncludesDurationColumn()
    {
        var start = new DateTime(2026, 3, 15, 10, 0, 0);
        var end = start.AddHours(1).AddMinutes(23).AddSeconds(45);
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "HD/SD", Results = new[] { Result("a.mkv", startTime: start, endTime: end) } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("<th>Duration</th>", html);
        Assert.Contains("<td>1h 23m 45s</td>", html);
    }

    [Fact]
    public void Generate_HistorySection_IncludesTimeColumn()
    {
        var model = new ReportModel
        {
            GeneratedAt = new DateTime(2026, 3, 15, 10, 30, 0),
            RunTime = TimeSpan.FromMinutes(2),
            RunNumber = 0,
            Lanes = new List<LaneReportSection>(),
            // 25 hours, to confirm the total isn't wrapped modulo-24 the way TimeSpan.Hours would.
            Today = new HistoryRollup(FileCount: 2, BeforeGb: 10, AfterGb: 4, TotalTimeSeconds: 25 * 3600 + 61)
        };

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("<th>Time</th>", html);
        Assert.Contains("<td>25h 1m 1s</td>", html);
    }

    [Fact]
    public void Generate_LaneFullySkipped_ShowsErrorCodeInsteadOfSilentNoFilesProcessed()
    {
        // Real gap found live: a lane skipped entirely (no preset configured, no Output folder,
        // etc) used to show a bare "No files processed." with zero indication why - invisible on
        // the report even though the plain-text log had the real reason. A user who only checks
        // the report (which can even auto-open) had no way to know.
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "Broken Lane", Results = Array.Empty<ConversionResult>(), LaneProblems = new[] { ReportErrorCode.LaneNoPresetConfigured } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("ERROR 106", html);
        Assert.Contains(WebUtility.HtmlEncode(ReportErrorCode.LaneNoPresetConfigured.Describe()), html);
        Assert.Contains("No files processed.", html); // still shown too - the code explains why
    }

    [Fact]
    public void Generate_LanePartiallyBroken_ShowsErrorCodeAlongsideTheFilesThatDidProcess()
    {
        // A lane can have a real problem (its TV preset missing) while still processing other
        // content fine (movies in the same lane) - the notice must show either way, not just for
        // a fully-empty lane.
        var lanes = new List<LaneReportSection>
        {
            new()
            {
                LaneDisplayName = "Mixed Lane",
                Results = new[] { Result("movie.mkv") },
                LaneProblems = new[] { ReportErrorCode.LaneTvPresetNotFound }
            }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("ERROR 108", html);
        Assert.Contains(WebUtility.HtmlEncode(ReportErrorCode.LaneTvPresetNotFound.Describe()), html);
        Assert.Contains("movie.mkv", html); // the file that DID process is still shown normally
    }

    [Fact]
    public void Generate_LaneProblemOnly_NoFileErrors_StillShowsErrorBannerAtTop()
    {
        // A lane-level problem with zero per-file errors used to leave the top banner saying
        // "Run completed with no errors." - misleading given a real problem is shown just below.
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "Broken Lane", Results = Array.Empty<ConversionResult>(), LaneProblems = new[] { ReportErrorCode.LaneNoOutputConfigured } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("1 lane problem(s)", html);
        Assert.DoesNotContain("Run completed with no errors.", html);
    }

    [Fact]
    public void Generate_NoLaneProblems_BannerAndSectionsUnaffected()
    {
        var lanes = new List<LaneReportSection>
        {
            new() { LaneDisplayName = "Healthy Lane", Results = new[] { Result("a.mkv") } }
        };
        var model = BaseModel(lanes);

        var html = new HtmlReportGenerator().Generate(model);

        Assert.Contains("Run completed with no errors.", html);
        // The .lane-problem CSS *class* is always defined in the stylesheet - what must be absent
        // is an actual element using it.
        Assert.DoesNotContain("<p class=\"lane-problem\">", html);
        Assert.DoesNotContain("lane problem(s)", html);
    }
}
