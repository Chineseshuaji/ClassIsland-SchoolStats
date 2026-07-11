using System.ComponentModel;
using System.Text.Json;
using ClassIsland.SchoolStats.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SchoolStats.Services;

public sealed class HolidayDataService : IHolidayDataService, IDisposable
{
    private readonly IHolidayProvider _holidayProvider;
    private readonly SemesterConfiguration _configuration;
    private readonly ILogger<HolidayDataService>? _logger;
    private readonly Dictionary<int, IReadOnlyList<HolidayInfo>> _legalHolidayCache = [];
    private readonly Dictionary<int, SemaphoreSlim> _yearLocks = [];
    private readonly object _cacheLock = new();
    private long _generation;

    public HolidayDataService(
        IHolidayProvider holidayProvider,
        SemesterConfiguration configuration,
        ILogger<HolidayDataService>? logger = null)
    {
        _holidayProvider = holidayProvider;
        _configuration = configuration;
        _logger = logger;
        _configuration.PropertyChanged += OnConfigurationChanged;
    }

    public async Task<SchoolDayResult> GetSchoolDayAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        date = date.Date;

        // Explicit school days are the strongest user override.
        if (_configuration.CustomWorkdays.Any(workday => workday.Contains(date)))
            return new SchoolDayResult(true, null);

        var customHoliday = _configuration.CustomHolidays.FirstOrDefault(holiday => holiday.Contains(date));
        if (customHoliday is not null)
        {
            var reason = customHoliday.Category switch
            {
                HolidayCategory.WinterBreak => $"寒假：{customHoliday.Name}",
                HolidayCategory.SummerBreak => $"暑假：{customHoliday.Name}",
                HolidayCategory.LegalHoliday => $"法定节假日：{customHoliday.Name}",
                _ => $"自定义假期：{customHoliday.Name}"
            };
            return new SchoolDayResult(false, reason);
        }

        var legalHolidays = await GetLegalHolidaysAsync(date.Year, cancellationToken)
            .ConfigureAwait(false);
        var matchingOfficialRecords = legalHolidays
            .Where(holiday => holiday.Contains(date))
            .ToList();

        // Make-up workdays are evaluated before legal holidays so malformed or
        // duplicated source data cannot make the result depend on list ordering.
        if (matchingOfficialRecords.Any(holiday => holiday.Category == HolidayCategory.MakeUpWorkday))
            return new SchoolDayResult(true, null);

        var legalHoliday = matchingOfficialRecords
            .FirstOrDefault(holiday => holiday.Category == HolidayCategory.LegalHoliday);
        if (legalHoliday is not null)
            return new SchoolDayResult(false, $"法定节假日：{legalHoliday.Name}");

        if (_configuration.ExcludeWeekends
            && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return new SchoolDayResult(false, "周末");
        }

        return new SchoolDayResult(true, null);
    }

    public async Task WarmUpAsync(
        IEnumerable<int> years,
        CancellationToken cancellationToken = default)
    {
        var tasks = years
            .Distinct()
            .Select(year => GetLegalHolidaysAsync(year, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public void InvalidateCache()
    {
        int[] years;
        lock (_cacheLock)
        {
            _generation++;
            years = _legalHolidayCache.Keys
                .Concat(_yearLocks.Keys)
                .Distinct()
                .ToArray();
            _legalHolidayCache.Clear();
        }

        foreach (var year in years)
            _holidayProvider.InvalidateCache(year);
    }

    private async Task<IReadOnlyList<HolidayInfo>> GetLegalHolidaysAsync(
        int year,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_cacheLock)
            {
                if (_legalHolidayCache.TryGetValue(year, out var cached))
                    return cached;
            }

            var yearLock = GetYearLock(year);
            await yearLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                long generation;
                lock (_cacheLock)
                {
                    if (_legalHolidayCache.TryGetValue(year, out var cached))
                        return cached;
                    generation = _generation;
                }

                var holidays = await _holidayProvider.GetHolidaysAsync(year, cancellationToken)
                    .ConfigureAwait(false);
                lock (_cacheLock)
                {
                    if (generation == _generation)
                    {
                        _legalHolidayCache[year] = holidays;
                        return holidays;
                    }
                }

                _logger?.LogDebug(
                    "Holiday source changed while loading {Year}; retrying",
                    year);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
            {
                _logger?.LogError(ex, "Unable to load holiday data for {Year}", year);
                return [];
            }
            finally
            {
                yearLock.Release();
            }
        }
    }

    private SemaphoreSlim GetYearLock(int year)
    {
        lock (_cacheLock)
        {
            if (!_yearLocks.TryGetValue(year, out var semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _yearLocks[year] = semaphore;
            }
            return semaphore;
        }
    }

    private void OnConfigurationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterConfiguration.EnableNetworkHolidayUpdate))
            InvalidateCache();
    }

    public void Dispose()
    {
        _configuration.PropertyChanged -= OnConfigurationChanged;
    }
}
