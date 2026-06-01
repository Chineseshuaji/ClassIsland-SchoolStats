using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class HolidayDataService : IHolidayDataService
{
    private readonly IHolidayProvider _holidayProvider;
    private readonly SemesterConfiguration _config;
    private readonly Dictionary<int, IReadOnlyList<HolidayInfo>> _legalHolidayCache = [];

    public HolidayDataService(IHolidayProvider holidayProvider, SemesterConfiguration config)
    {
        _holidayProvider = holidayProvider;
        _config = config;
    }

    public bool IsSchoolDay(DateTime date, out string? reason)
    {
        date = date.Date;
        reason = null;

        foreach (var wd in _config.CustomWorkdays)
        {
            if (wd.Contains(date))
                return true;
        }

        foreach (var ch in _config.CustomHolidays)
        {
            if (ch.Contains(date))
            {
                reason = $"自定义假期：{ch.Name}";
                return false;
            }
        }

        var legalHolidays = GetLegalHolidays(date.Year);
        foreach (var lh in legalHolidays)
        {
            if (lh.Category == HolidayCategory.MakeUpWorkday && lh.Contains(date))
                return true;

            if (lh.Category == HolidayCategory.LegalHoliday && lh.Contains(date))
            {
                reason = $"法定节假日：{lh.Name}";
                return false;
            }
        }

        foreach (var ch in _config.CustomHolidays)
        {
            if ((ch.Category == HolidayCategory.WinterBreak || ch.Category == HolidayCategory.SummerBreak)
                && ch.Contains(date))
            {
                reason = (ch.Category == HolidayCategory.WinterBreak ? "寒假" : "暑假") + $"：{ch.Name}";
                return false;
            }
        }

        if (_config.ExcludeWeekends)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                reason = "周末";
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<HolidayInfo> GetLegalHolidays(int year)
    {
        if (!_legalHolidayCache.TryGetValue(year, out var holidays))
        {
            holidays = _holidayProvider.GetHolidaysAsync(year).GetAwaiter().GetResult();
            _legalHolidayCache[year] = holidays;
        }
        return holidays;
    }

    public async Task WarmUpAsync(int year)
    {
        var holidays = await _holidayProvider.GetHolidaysAsync(year);
        _legalHolidayCache[year] = holidays;
    }
}
