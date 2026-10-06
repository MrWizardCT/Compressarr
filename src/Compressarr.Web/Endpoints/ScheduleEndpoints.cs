using Compressarr.Core.Config;
using Compressarr.Core.Scheduling;
using Compressarr.Web.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

/// <summary>The Scheduler page's own settings endpoint. Separate from /api/settings on purpose: the
/// two pages each save only their own part of the config, so saving one can never overwrite
/// unsaved edits made on the other. Live schedule state (current mode, held or not) is not here -
/// the Monitor page and the top bar read it from /api/run/status.</summary>
public static class ScheduleEndpoints
{
    private static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    public static void MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/schedule", (IConfigStore configStore) =>
            Results.Json(ToDto(configStore.Load(AppPaths.GetConfigFilePath()).Schedule)));

        app.MapPut("/api/schedule", (ScheduleDto dto, IConfigStore configStore) =>
        {
            var result = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                Apply(config.Schedule, dto);
                return ToDto(config.Schedule);
            });
            return Results.Json(result);
        });
    }

    internal static ScheduleDto ToDto(ScheduleSettings s)
    {
        // Always seven entries, Sunday first, so the page never has to cope with a short list.
        var days = Enumerable.Range(0, 7)
            .Select(i => i < s.Days.Count
                ? new ScheduleDayDto(DayNames[i], s.Days[i].Start, s.Days[i].End)
                : new ScheduleDayDto(DayNames[i], s.DayStart, s.DayEnd))
            .ToList();

        var issues = ScheduleValidator.Validate(s).Select(i => new ValidationIssueDto(i.Field, i.Message)).ToList();
        return new ScheduleDto(
            s.Enabled, s.Mode.ToString(), s.DayStart, s.DayEnd, s.WeekendDayStart, s.WeekendDayEnd, days,
            s.DayPriority.ToString(), s.NightPriority.ToString(), s.OnlyEncodeOffHours, s.WhenDayStarts.ToString(), issues);
    }

    internal static void Apply(ScheduleSettings s, ScheduleDto dto)
    {
        s.Enabled = dto.Enabled;
        s.Mode = Enum.Parse<ScheduleMode>(dto.Mode);
        s.DayStart = dto.DayStart;
        s.DayEnd = dto.DayEnd;
        s.WeekendDayStart = dto.WeekendDayStart;
        s.WeekendDayEnd = dto.WeekendDayEnd;
        s.Days = Enumerable.Range(0, 7)
            .Select(i => i < dto.Days.Count ? new ScheduleDayWindow { Start = dto.Days[i].Start, End = dto.Days[i].End } : new ScheduleDayWindow())
            .ToList();
        s.DayPriority = Enum.Parse<EncodePriority>(dto.DayPriority);
        s.NightPriority = Enum.Parse<EncodePriority>(dto.NightPriority);
        s.OnlyEncodeOffHours = dto.OnlyEncodeOffHours;
        s.WhenDayStarts = Enum.Parse<DayHoldBehavior>(dto.WhenDayStarts);
    }
}
