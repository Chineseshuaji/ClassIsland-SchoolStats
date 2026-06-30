using System.Diagnostics;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class HolidayDataService : IHolidayDataService
{
    private readonly IHolidayProvider _holidayProvider;
    private readonly SemesterConfiguration _config;
    private readonly Dictionary<int, IReadOnlyList<HolidayInfo>> _legalHolidayCache = [];
    private readonly object _cacheLock = new();

    public HolidayDataService(IHolidayProvider holidayProvider, SemesterConfiguration config)
    {
        _holidayProvider = holidayProvider;
        _config = config;

        // 后台预热节假日缓存，避免首次 IsSchoolDay 调用时触发同步等待
        _ = Task.Run(async () =>
        {
            var year = DateTime.Now.Year;
            await WarmUpAsync(year + 1);
            await WarmUpAsync(year);
        });
    }

    public bool IsSchoolDay(DateTime date, out string? reason)
    {
        date = date.Date;
        reason = null;

        // 调休工作日优先（补班日强制算上学）
        foreach (var wd in _config.CustomWorkdays)
        {
            if (wd.Contains(date))
                return true;
        }

        // 自定义假期（含寒暑假、法定假、通用假期），按分类输出原因
        foreach (var ch in _config.CustomHolidays)
        {
            if (!ch.Contains(date))
                continue;

            reason = ch.Category switch
            {
                HolidayCategory.WinterBreak => $"寒假：{ch.Name}",
                HolidayCategory.SummerBreak => $"暑假：{ch.Name}",
                HolidayCategory.LegalHoliday => $"法定节假日：{ch.Name}",
                _ => $"自定义假期：{ch.Name}"
            };
            return false;
        }

        // 网络/本地节假日数据中的法定节假日与调休
        var legalHolidays = GetLegalHolidays(date.Year);
        foreach (var lh in legalHolidays)
        {
            if (!lh.Contains(date))
                continue;

            if (lh.Category == HolidayCategory.MakeUpWorkday)
                return true;

            if (lh.Category == HolidayCategory.LegalHoliday)
            {
                reason = $"法定节假日：{lh.Name}";
                return false;
            }
        }

        // 周末排除
        if (_config.ExcludeWeekends && date is { DayOfWeek: DayOfWeek.Saturday or DayOfWeek.Sunday })
        {
            reason = "周末";
            return false;
        }

        return true;
    }

    private IReadOnlyList<HolidayInfo> GetLegalHolidays(int year)
    {
        lock (_cacheLock)
        {
            if (_legalHolidayCache.TryGetValue(year, out var cached))
                return cached;
        }

        // Task.Run 将异步调用调度到线程池，避免 UI 同步上下文死锁
        var holidays = Task.Run(() => _holidayProvider.GetHolidaysAsync(year))
            .GetAwaiter().GetResult();

        lock (_cacheLock)
        {
            _legalHolidayCache[year] = holidays;
        }

        return holidays;
    }

    public async Task WarmUpAsync(int year)
    {
        var holidays = await _holidayProvider.GetHolidaysAsync(year);
        lock (_cacheLock)
        {
            _legalHolidayCache[year] = holidays;
        }
    }
}
