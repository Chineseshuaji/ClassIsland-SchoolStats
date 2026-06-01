using System.Text.Json;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class StatisticsService : IStatisticsService
{
    private readonly SemesterConfiguration _config;
    private readonly IHolidayDataService _holidayService;
    private readonly string _cachePath;
    private Dictionary<DateTime, DailyStatsRecord> _dailyCache = [];

    public StatisticsService(SemesterConfiguration config, IHolidayDataService holidayService, string cacheDir)
    {
        _config = config;
        _holidayService = holidayService;
        _cachePath = Path.Combine(cacheDir, "stats_cache.json");
        LoadCache();
    }

    public AggregatedStats CalculateStats()
        => CalculateStats(DateTime.Now.Date);

    public AggregatedStats CalculateStats(DateTime referenceDate)
    {
        referenceDate = referenceDate.Date;
        var start = _config.StartDate.Date;
        var end = _config.EndDate.Date;

        var lastCachedDate = _dailyCache.Keys.Any()
            ? _dailyCache.Keys.Max()
            : start.AddDays(-1);

        for (var d = lastCachedDate.AddDays(1); d <= end; d = d.AddDays(1))
        {
            var isSchool = _holidayService.IsSchoolDay(d, out var reason);
            _dailyCache[d] = new DailyStatsRecord
            {
                Date = d,
                IsSchoolDay = isSchool,
                ExclusionReason = reason
            };
        }

        SaveCache();

        var allRecords = _dailyCache
            .Where(kv => kv.Key >= start && kv.Key <= end)
            .ToList();

        var totalSchoolDays = allRecords.Count(r => r.Value.IsSchoolDay);
        var passedRecords = allRecords
            .Where(kv => kv.Key <= referenceDate)
            .ToList();
        var passedSchoolDays = passedRecords.Count(r => r.Value.IsSchoolDay);
        var passedSchoolHours = passedSchoolDays * _config.DailyHours;
        var totalSchoolHours = totalSchoolDays * _config.DailyHours;

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
            PassedSchoolHours = Math.Round(passedSchoolHours, 1),
            TotalSchoolHours = Math.Round(totalSchoolHours, 1),
            AppliedExclusions = exclusions,
            ReferenceDate = referenceDate
        };
    }

    public void InvalidateCache()
    {
        _dailyCache.Clear();
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
                    _dailyCache = list.ToDictionary(r => r.Date, r => r);
            }
        }
        catch
        {
            _dailyCache = [];
        }
    }

    private void SaveCache()
    {
        try
        {
            var list = _dailyCache.Values.OrderBy(r => r.Date).ToList();
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(list));
        }
        catch { }
    }
}
