using System.Diagnostics;
using System.Text.Json;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class StatisticsService : IStatisticsService
{
    private readonly SemesterConfiguration _config;
    private readonly IHolidayDataService _holidayService;
    private readonly string _cachePath;
    private readonly object _cacheLock = new();
    private Dictionary<DateTime, DailyStatsRecord> _dailyCache = [];

    public StatisticsService(SemesterConfiguration config, IHolidayDataService holidayService, string cacheDir)
    {
        _config = config;
        _holidayService = holidayService;
        _cachePath = Path.Combine(cacheDir, "stats_cache.json");
        LoadCache();

        _config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SemesterConfiguration.StartDate)
                or nameof(SemesterConfiguration.EndDate)
                or nameof(SemesterConfiguration.ExcludeWeekends)
                or nameof(SemesterConfiguration.CustomHolidays)
                or nameof(SemesterConfiguration.CustomWorkdays)
                or nameof(SemesterConfiguration.ScheduleTemplates)
                or nameof(SemesterConfiguration.ScheduleTemplateRules)
                or nameof(SemesterConfiguration.ManualTimeExclusions))
            {
                InvalidateCache();
            }
        };
    }

    public AggregatedStats CalculateStats()
        => CalculateStats(DateTime.Now);

    public AggregatedStats CalculateStats(DateTime referenceDateTime)
    {
        var referenceDate = referenceDateTime.Date;
        var start = _config.StartDate.Date;
        var end = _config.EndDate.Date;

        var cacheChanged = false;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            lock (_cacheLock)
            {
                if (_dailyCache.ContainsKey(d))
                    continue;
            }

            var isSchool = _holidayService.IsSchoolDay(d, out var reason);
            lock (_cacheLock)
            {
                _dailyCache[d] = new DailyStatsRecord
                {
                    Date = d,
                    IsSchoolDay = isSchool,
                    ExclusionReason = reason
                };
            }
            cacheChanged = true;
        }

        if (cacheChanged)
        {
            SaveCache();
        }

        List<KeyValuePair<DateTime, DailyStatsRecord>> allRecords;
        lock (_cacheLock)
        {
            allRecords = _dailyCache
                .Where(kv => kv.Key >= start && kv.Key <= end)
                .ToList();
        }

        var totalSchoolDays = allRecords.Count(r => r.Value.IsSchoolDay);
        var passedRecords = allRecords
            .Where(kv => kv.Key <= referenceDate)
            .ToList();
        var passedSchoolDays = passedRecords.Count(r => r.Value.IsSchoolDay);
        var passedSchoolHours = allRecords
            .Where(kv => kv.Key < referenceDate && kv.Value.IsSchoolDay)
            .Sum(kv => CalculateDateSchoolHours(kv.Key));
        passedSchoolHours += CalculateReferenceDateSchoolHours(referenceDateTime, allRecords);
        var totalSchoolHours = allRecords
            .Where(kv => kv.Value.IsSchoolDay)
            .Sum(kv => CalculateDateSchoolHours(kv.Key));

        var exclusions = allRecords
            .Where(kv => !kv.Value.IsSchoolDay && !string.IsNullOrEmpty(kv.Value.ExclusionReason))
            .GroupBy(kv => kv.Value.ExclusionReason)
            .Select(g => new ExclusionDetail
            {
                Name = g.Key!,
                StartDate = g.Min(x => x.Key),
                EndDate = g.Max(x => x.Key),
                ExcludedDays = g.Count()
            })
            .OrderByDescending(e => e.ExcludedDays)
            .ToList();

        return new AggregatedStats
        {
            TotalSchoolDays = totalSchoolDays,
            PassedSchoolDays = passedSchoolDays,
            PassedSchoolHours = Math.Round(passedSchoolHours, 4),
            TotalSchoolHours = Math.Round(totalSchoolHours, 4),
            CurrentWeek = CalculateWeeklyStats(referenceDateTime, allRecords),
            AppliedExclusions = exclusions,
            ReferenceDate = referenceDate
        };
    }

    private WeeklyStats CalculateWeeklyStats(
        DateTime referenceDateTime,
        List<KeyValuePair<DateTime, DailyStatsRecord>> allRecords)
    {
        var referenceDate = referenceDateTime.Date;
        var weekStart = referenceDate.AddDays(-GetChineseWeekOffset(referenceDate.DayOfWeek));
        var weekEnd = weekStart.AddDays(6);
        var weekRecords = allRecords
            .Where(kv => kv.Key >= weekStart && kv.Key <= weekEnd)
            .ToList();
        var totalHours = weekRecords
            .Where(kv => kv.Value.IsSchoolDay)
            .Sum(kv => CalculateDateSchoolHours(kv.Key));
        var passedHours = weekRecords
            .Where(kv => kv.Key < referenceDate && kv.Value.IsSchoolDay)
            .Sum(kv => CalculateDateSchoolHours(kv.Key));
        passedHours += CalculateReferenceDateSchoolHours(referenceDateTime, weekRecords);

        return new WeeklyStats
        {
            StartDate = weekStart,
            EndDate = weekEnd,
            TotalSchoolDays = weekRecords.Count(kv => kv.Value.IsSchoolDay),
            PassedSchoolDays = weekRecords.Count(kv => kv.Key <= referenceDate && kv.Value.IsSchoolDay),
            TotalSchoolHours = Math.Round(totalHours, 4),
            PassedSchoolHours = Math.Round(passedHours, 4)
        };
    }

    private double CalculateReferenceDateSchoolHours(
        DateTime referenceDateTime,
        List<KeyValuePair<DateTime, DailyStatsRecord>> allRecords)
    {
        var referenceDate = referenceDateTime.Date;
        if (referenceDate < _config.StartDate.Date || referenceDate > _config.EndDate.Date)
            return 0;

        var todayRecord = allRecords.FirstOrDefault(kv => kv.Key == referenceDate);
        if (todayRecord.Value is not { IsSchoolDay: true })
            return 0;

        return CalculateDateSchoolHours(referenceDate, referenceDateTime.TimeOfDay);
    }

    private double CalculateDateSchoolHours(DateTime date, TimeSpan? cutoffTime = null)
    {
        date = date.Date;
        var intervals = GetSchoolIntervals(_config.GetScheduleTemplate(date));
        var total = intervals.Sum(i => CalculateIntervalHours(date, i.Start, i.End, cutoffTime));
        return Math.Max(0, total);
    }

    private double CalculateIntervalHours(DateTime date, TimeSpan start, TimeSpan end, TimeSpan? cutoffTime)
    {
        if (end <= start)
            return 0;

        var intervalStart = start;
        var intervalEnd = end;
        if (cutoffTime.HasValue)
        {
            if (cutoffTime.Value <= intervalStart)
                return 0;
            if (cutoffTime.Value < intervalEnd)
                intervalEnd = cutoffTime.Value;
        }

        var total = (intervalEnd - intervalStart).TotalHours;
        foreach (var exclusion in _config.ManualTimeExclusions.Where(x => x.Date.Date == date))
        {
            var overlapStart = Max(intervalStart, exclusion.StartTime);
            var overlapEnd = Min(intervalEnd, exclusion.EndTime);
            if (overlapEnd > overlapStart)
                total -= (overlapEnd - overlapStart).TotalHours;
        }

        return Math.Max(0, total);
    }

    private static IReadOnlyList<(TimeSpan Start, TimeSpan End)> GetSchoolIntervals(ScheduleTemplate template)
    {
        var intervals = new List<(TimeSpan Start, TimeSpan End)>();
        if (template.LunchStartTime > template.SchoolStartTime)
            intervals.Add((template.SchoolStartTime, Min(template.LunchStartTime, template.SchoolEndTime)));
        if (template.SchoolEndTime > template.LunchEndTime)
            intervals.Add((Max(template.LunchEndTime, template.SchoolStartTime), template.SchoolEndTime));
        return intervals.Where(i => i.End > i.Start).ToList();
    }

    private static int GetChineseWeekOffset(DayOfWeek dayOfWeek)
        => dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _dailyCache.Clear();
        }
        if (File.Exists(_cachePath))
            File.Delete(_cachePath);
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath);
                var list = JsonSerializer.Deserialize<List<DailyStatsRecord>>(json);
                if (list != null)
                {
                    lock (_cacheLock)
                    {
                        _dailyCache = list.ToDictionary(r => r.Date, r => r);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolStats] 缓存加载失败: {ex.Message}");
            lock (_cacheLock)
            {
                _dailyCache = [];
            }
        }
    }

    private void SaveCache()
    {
        try
        {
            List<DailyStatsRecord> list;
            lock (_cacheLock)
            {
                list = _dailyCache.Values.OrderBy(r => r.Date).ToList();
            }
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(list));
        }
        catch { }
    }
}
