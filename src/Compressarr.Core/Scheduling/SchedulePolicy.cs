using System.Globalization;
using Compressarr.Core.Config;

namespace Compressarr.Core.Scheduling;

/// <summary>Where a moment falls relative to the daytime window, and when that next changes.
/// NextChangeLocal is null when the answer never changes (the schedule is off, or there is no
/// daytime window at all).</summary>
public readonly record struct ScheduleWindowState(bool IsDaytime, DateTime? NextChangeLocal);

/// <summary>
/// The day/night window rules as pure functions of the settings and a local wall-clock time - no
/// clock, no config store, no processes - so every edge (a window crossing midnight, a separate
/// weekend window, "no window", the wrap from Friday night into Saturday) is unit-testable.
/// Wall-clock arithmetic on purpose: the schedule is "8 AM to 10 PM on this PC's clock", so a
/// daylight-saving change just moves the real-time length of one night.
/// </summary>
public static class SchedulePolicy
{
    // How many days ahead intervals are generated when looking for the next change. Any window
    // that exists at all appears at least once a week, so 10 days always finds the next boundary.
    private const int LookAheadDays = 10;

    /// <summary>Parses "HH:mm" (also accepts "H:mm"). Anything else is rejected.</summary>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!TimeOnly.TryParseExact(text.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        time = parsed.ToTimeSpan();
        return true;
    }

    /// <summary>Is <paramref name="localNow"/> inside the daytime window, and when does that next
    /// flip? A disabled schedule is never daytime.</summary>
    public static ScheduleWindowState Evaluate(ScheduleSettings settings, DateTime localNow)
    {
        if (!settings.Enabled) return new ScheduleWindowState(false, null);

        var merged = MergedWindows(settings, localNow.Date.AddDays(-1), LookAheadDays + 1);
        foreach (var (start, end) in merged)
        {
            if (localNow >= start && localNow < end) return new ScheduleWindowState(true, end);
        }

        var next = merged.Where(w => w.Start > localNow).Select(w => (DateTime?)w.Start).FirstOrDefault();
        return new ScheduleWindowState(false, next);
    }

    /// <summary>When everything still to encode would finish, counting the time the queue is held
    /// for the daytime window. Only differs from "now + minutes" when the schedule is enabled with
    /// off-hours-only on. currentMinutes is the file encoding right now: with FinishCurrentFile it
    /// runs straight through even into the day; with SuspendEncode it only advances during
    /// off-hours like everything else. overrideUntilLocal is an active "run anyway" - encoding is
    /// allowed until then even inside the window. Null if no completion can be found (a window that
    /// leaves no off-hours at all).</summary>
    public static DateTime? ProjectCompletion(ScheduleSettings settings, DateTime localNow, double currentMinutes, double queuedMinutes, DateTime? overrideUntilLocal = null)
    {
        if (!settings.Enabled || !settings.OnlyEncodeOffHours)
        {
            return localNow.AddMinutes(currentMinutes + queuedMinutes);
        }

        var t = localNow;
        var remaining = queuedMinutes;
        if (settings.WhenDayStarts == DayHoldBehavior.FinishCurrentFile)
        {
            t = t.AddMinutes(currentMinutes);
        }
        else
        {
            remaining += currentMinutes;
        }

        for (var guard = 0; guard < 2000; guard++)
        {
            if (remaining <= 0) return t;

            DateTime? segmentEnd;
            if (overrideUntilLocal is { } until && t < until)
            {
                segmentEnd = until;
            }
            else
            {
                var state = Evaluate(settings, t);
                if (state.IsDaytime)
                {
                    t = state.NextChangeLocal!.Value;
                    continue;
                }
                segmentEnd = state.NextChangeLocal;
            }

            if (segmentEnd is null) return t.AddMinutes(remaining);

            var available = (segmentEnd.Value - t).TotalMinutes;
            if (remaining <= available) return t.AddMinutes(remaining);
            remaining -= available;
            t = segmentEnd.Value;
        }

        return null;
    }

    /// <summary>The daytime windows starting on or after <paramref name="firstDate"/>, as local
    /// [Start, End) pairs, with overlapping/touching ones merged (Friday's late window running into
    /// Saturday's early one is one window). A window belongs to the day it starts on, which is also
    /// the day whose weekday/weekend setting applies to it.</summary>
    private static List<(DateTime Start, DateTime End)> MergedWindows(ScheduleSettings settings, DateTime firstDate, int days)
    {
        var raw = new List<(DateTime Start, DateTime End)>();
        for (var i = 0; i < days; i++)
        {
            var date = firstDate.AddDays(i);
            var weekend = settings.WeekendDifferent && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var startText = weekend ? settings.WeekendDayStart : settings.DayStart;
            var endText = weekend ? settings.WeekendDayEnd : settings.DayEnd;
            if (!TryParseTime(startText, out var start) || !TryParseTime(endText, out var end)) continue;
            if (start == end) continue; // "no daytime window" - always night

            var windowStart = date + start;
            var windowEnd = end > start ? date + end : date.AddDays(1) + end;
            raw.Add((windowStart, windowEnd));
        }

        raw.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var window in raw)
        {
            if (merged.Count > 0 && window.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, window.End > merged[^1].End ? window.End : merged[^1].End);
            }
            else
            {
                merged.Add(window);
            }
        }
        return merged;
    }
}
