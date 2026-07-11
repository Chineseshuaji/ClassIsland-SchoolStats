using System.ComponentModel;
using ClassIsland.SchoolStats.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SchoolStats.Services;

public sealed class StatisticsService : IStatisticsService, IDisposable
{
    private static readonly TimeSpan NetworkSnapshotLifetime = TimeSpan.FromHours(12);
    private readonly SemesterConfiguration _configuration;
    private readonly IHolidayDataService _holidayService;
    private readonly ILogger<StatisticsService>? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private readonly object _stateLock = new();
    private StaticStatisticsSnapshot? _snapshot;
    private long _generation;
    private bool _holidayRefreshPending;

    public StatisticsService(
        SemesterConfiguration configuration,
        IHolidayDataService holidayService,
        ILogger<StatisticsService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        _holidayService = holidayService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _configuration.PropertyChanged += OnConfigurationChanged;
    }

    public async Task<AggregatedStats> CalculateStatsAsync(
        DateTime referenceDateTime,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return CreateAggregatedStats(snapshot, referenceDateTime);
    }

    public void InvalidateCache()
    {
        lock (_stateLock)
        {
            _generation++;
            _snapshot = null;
        }
    }

    private async Task<StaticStatisticsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_stateLock)
            {
                if (_snapshot is not null)
                {
                    if (!IsSnapshotExpired(_snapshot))
                        return _snapshot;

                    _generation++;
                    _snapshot = null;
                    _holidayRefreshPending = true;
                }
            }

            await _buildGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                long generation;
                bool refreshHolidayData;
                lock (_stateLock)
                {
                    if (_snapshot is not null)
                        return _snapshot;
                    generation = _generation;
                    refreshHolidayData = _holidayRefreshPending;
                    _holidayRefreshPending = false;
                }

                if (refreshHolidayData)
                    _holidayService.InvalidateCache();

                var built = await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
                lock (_stateLock)
                {
                    if (generation == _generation)
                    {
                        _snapshot = built;
                        return built;
                    }
                }

                _logger?.LogDebug("Statistics configuration changed while rebuilding; retrying");
            }
            finally
            {
                _buildGate.Release();
            }
        }
    }

    private bool IsSnapshotExpired(StaticStatisticsSnapshot snapshot)
        => _configuration.EnableNetworkHolidayUpdate
            && _timeProvider.GetUtcNow() - snapshot.BuiltAtUtc >= NetworkSnapshotLifetime;

    private async Task<StaticStatisticsSnapshot> BuildSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var start = _configuration.StartDate.Date;
        var end = _configuration.EndDate.Date;
        if (end < start)
            return StaticStatisticsSnapshot.Empty(start, end, _timeProvider.GetUtcNow());

        var calendarDays = (end - start).Days + 1;
        if (calendarDays > SemesterConfiguration.MaximumSemesterDays)
        {
            throw new InvalidOperationException(
                $"Semester range exceeds the supported limit of "
                + $"{SemesterConfiguration.MaximumSemesterDays} days.");
        }

        var years = Enumerable.Range(start.Year, end.Year - start.Year + 1);
        await _holidayService.WarmUpAsync(years, cancellationToken).ConfigureAwait(false);

        var records = new List<DailyStatsRecord>(calendarDays);
        var effectiveIntervals = new List<IReadOnlyList<(TimeSpan Start, TimeSpan End)>>(
            records.Capacity);
        for (var date = start; ; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = await _holidayService.GetSchoolDayAsync(date, cancellationToken)
                .ConfigureAwait(false);
            var dateIntervals = decision.IsSchoolDay
                ? GetEffectiveSchoolIntervals(date)
                : [];
            effectiveIntervals.Add(dateIntervals);
            records.Add(new DailyStatsRecord
            {
                Date = date,
                IsSchoolDay = decision.IsSchoolDay,
                ExclusionReason = decision.ExclusionReason,
                SchoolHours = CalculateIntervalsHours(dateIntervals)
            });

            if (date == end)
                break;
        }

        return StaticStatisticsSnapshot.Create(
            start,
            end,
            records,
            effectiveIntervals,
            _timeProvider.GetUtcNow());
    }

    private AggregatedStats CreateAggregatedStats(
        StaticStatisticsSnapshot snapshot,
        DateTime referenceDateTime)
    {
        var referenceDate = referenceDateTime.Date;
        var beforeToday = snapshot.GetExclusiveOffset(referenceDate);
        var throughToday = snapshot.GetInclusiveOffset(referenceDate);

        var passedSchoolDays = snapshot.SchoolDayPrefix[throughToday];
        var passedSchoolHours = snapshot.SchoolHourPrefix[beforeToday]
            + CalculateReferenceDateSchoolHours(referenceDateTime, snapshot);

        return new AggregatedStats
        {
            TotalSchoolDays = snapshot.TotalSchoolDays,
            PassedSchoolDays = passedSchoolDays,
            PassedSchoolHours = Math.Round(Math.Min(snapshot.TotalSchoolHours, passedSchoolHours), 4),
            TotalSchoolHours = Math.Round(snapshot.TotalSchoolHours, 4),
            CurrentWeek = CalculateWeeklyStats(referenceDateTime, snapshot),
            AppliedExclusions = snapshot.AppliedExclusions,
            ReferenceDate = referenceDate
        };
    }

    private WeeklyStats CalculateWeeklyStats(
        DateTime referenceDateTime,
        StaticStatisticsSnapshot snapshot)
    {
        var referenceDate = referenceDateTime.Date;
        var weekStart = referenceDate.AddDays(-GetMondayOffset(referenceDate.DayOfWeek));
        var weekEnd = AddDaysClamped(weekStart, 6);
        var weekStartOffset = snapshot.GetExclusiveOffset(weekStart);
        var weekEndOffset = snapshot.GetInclusiveOffset(weekEnd);
        var throughReferenceOffset = Math.Clamp(
            snapshot.GetInclusiveOffset(referenceDate),
            weekStartOffset,
            weekEndOffset);
        var beforeReferenceOffset = Math.Clamp(
            snapshot.GetExclusiveOffset(referenceDate),
            weekStartOffset,
            weekEndOffset);

        var totalDays = snapshot.SchoolDayPrefix[weekEndOffset]
            - snapshot.SchoolDayPrefix[weekStartOffset];
        var passedDays = snapshot.SchoolDayPrefix[throughReferenceOffset]
            - snapshot.SchoolDayPrefix[weekStartOffset];
        var totalHours = snapshot.SchoolHourPrefix[weekEndOffset]
            - snapshot.SchoolHourPrefix[weekStartOffset];
        var passedHours = snapshot.SchoolHourPrefix[beforeReferenceOffset]
            - snapshot.SchoolHourPrefix[weekStartOffset];

        if (referenceDate >= weekStart && referenceDate <= weekEnd)
            passedHours += CalculateReferenceDateSchoolHours(referenceDateTime, snapshot);

        return new WeeklyStats
        {
            StartDate = weekStart,
            EndDate = weekEnd,
            TotalSchoolDays = totalDays,
            PassedSchoolDays = passedDays,
            TotalSchoolHours = Math.Round(totalHours, 4),
            PassedSchoolHours = Math.Round(Math.Min(totalHours, passedHours), 4)
        };
    }

    private double CalculateReferenceDateSchoolHours(
        DateTime referenceDateTime,
        StaticStatisticsSnapshot snapshot)
    {
        var date = referenceDateTime.Date;
        var index = snapshot.GetRecordIndex(date);
        if (index < 0 || !snapshot.Records[index].IsSchoolDay)
            return 0;

        return CalculateIntervalsHours(
            snapshot.EffectiveIntervals[index],
            referenceDateTime.TimeOfDay);
    }

    private IReadOnlyList<(TimeSpan Start, TimeSpan End)> GetEffectiveSchoolIntervals(
        DateTime date)
    {
        date = date.Date;
        var schoolIntervals = GetSchoolIntervals(_configuration.GetScheduleTemplate(date));
        var exclusions = MergeIntervals(_configuration.ManualTimeExclusions
            .Where(exclusion => exclusion.Date.Date == date)
            .Select(exclusion => (exclusion.StartTime, exclusion.EndTime)));
        if (exclusions.Count == 0)
            return schoolIntervals;

        var effectiveIntervals = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var schoolInterval in schoolIntervals)
        {
            var cursor = schoolInterval.Start;
            foreach (var exclusion in exclusions)
            {
                if (exclusion.End <= cursor)
                    continue;
                if (exclusion.Start >= schoolInterval.End)
                    break;

                if (exclusion.Start > cursor)
                {
                    effectiveIntervals.Add((
                        cursor,
                        Min(exclusion.Start, schoolInterval.End)));
                }

                cursor = Max(cursor, exclusion.End);
                if (cursor >= schoolInterval.End)
                    break;
            }

            if (cursor < schoolInterval.End)
                effectiveIntervals.Add((cursor, schoolInterval.End));
        }

        return effectiveIntervals;
    }

    private static double CalculateIntervalsHours(
        IReadOnlyList<(TimeSpan Start, TimeSpan End)> intervals,
        TimeSpan? cutoffTime = null)
    {
        var total = 0.0;
        foreach (var interval in intervals)
        {
            var intervalEnd = interval.End;
            if (cutoffTime.HasValue)
            {
                if (cutoffTime.Value <= interval.Start)
                    continue;
                intervalEnd = Min(intervalEnd, cutoffTime.Value);
            }

            if (intervalEnd <= interval.Start)
                continue;
            total += (intervalEnd - interval.Start).TotalHours;
        }

        return Math.Max(0, total);
    }

    private static IReadOnlyList<(TimeSpan Start, TimeSpan End)> GetSchoolIntervals(
        ScheduleTemplate template)
    {
        if (template.SchoolEndTime <= template.SchoolStartTime)
            return [];

        var start = template.SchoolStartTime;
        var end = template.SchoolEndTime;
        var lunchStart = Max(start, template.LunchStartTime);
        var lunchEnd = Min(end, template.LunchEndTime);
        if (lunchEnd <= lunchStart)
            return [(start, end)];

        var intervals = new List<(TimeSpan Start, TimeSpan End)>(2);
        if (lunchStart > start)
            intervals.Add((start, lunchStart));
        if (end > lunchEnd)
            intervals.Add((lunchEnd, end));
        return intervals;
    }

    internal static IReadOnlyList<(TimeSpan Start, TimeSpan End)> MergeIntervals(
        IEnumerable<(TimeSpan Start, TimeSpan End)> intervals)
    {
        var ordered = intervals
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ThenBy(interval => interval.End)
            .ToList();
        if (ordered.Count == 0)
            return [];

        var merged = new List<(TimeSpan Start, TimeSpan End)> { ordered[0] };
        foreach (var current in ordered.Skip(1))
        {
            var previous = merged[^1];
            if (current.Start <= previous.End)
            {
                merged[^1] = (previous.Start, Max(previous.End, current.End));
            }
            else
            {
                merged.Add(current);
            }
        }
        return merged;
    }

    private static List<ExclusionDetail> BuildExclusionDetails(
        IReadOnlyList<DailyStatsRecord> records)
    {
        var details = new List<ExclusionDetail>();
        ExclusionDetail? current = null;
        foreach (var record in records.Where(record =>
                     !record.IsSchoolDay && !string.IsNullOrWhiteSpace(record.ExclusionReason)))
        {
            if (current is not null
                && current.Name == record.ExclusionReason
                && current.EndDate < DateTime.MaxValue.Date
                && current.EndDate.AddDays(1) == record.Date)
            {
                current.EndDate = record.Date;
                current.ExcludedDays++;
                continue;
            }

            current = new ExclusionDetail
            {
                Name = record.ExclusionReason!,
                StartDate = record.Date,
                EndDate = record.Date,
                ExcludedDays = 1
            };
            details.Add(current);
        }

        return details
            .OrderByDescending(detail => detail.ExcludedDays)
            .ThenBy(detail => detail.StartDate)
            .ToList();
    }

    private static int GetMondayOffset(DayOfWeek dayOfWeek)
        => dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;

    private static DateTime AddDaysClamped(DateTime date, int days)
    {
        var maximumDays = (DateTime.MaxValue.Date - date.Date).Days;
        return date.Date.AddDays(Math.Min(days, maximumDays));
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private void OnConfigurationChanged(object? sender, PropertyChangedEventArgs e)
        => InvalidateCache();

    public void Dispose()
    {
        _configuration.PropertyChanged -= OnConfigurationChanged;
    }

    private sealed class StaticStatisticsSnapshot
    {
        public DateTime StartDate { get; }
        public DateTime EndDate { get; }
        public IReadOnlyList<DailyStatsRecord> Records { get; }
        public IReadOnlyList<IReadOnlyList<(TimeSpan Start, TimeSpan End)>> EffectiveIntervals { get; }
        public DateTimeOffset BuiltAtUtc { get; }
        public int[] SchoolDayPrefix { get; }
        public double[] SchoolHourPrefix { get; }
        public int TotalSchoolDays => SchoolDayPrefix[^1];
        public double TotalSchoolHours => SchoolHourPrefix[^1];
        public List<ExclusionDetail> AppliedExclusions { get; }

        private StaticStatisticsSnapshot(
            DateTime startDate,
            DateTime endDate,
            IReadOnlyList<DailyStatsRecord> records,
            IReadOnlyList<IReadOnlyList<(TimeSpan Start, TimeSpan End)>> effectiveIntervals,
            DateTimeOffset builtAtUtc,
            int[] schoolDayPrefix,
            double[] schoolHourPrefix,
            List<ExclusionDetail> appliedExclusions)
        {
            StartDate = startDate;
            EndDate = endDate;
            Records = records;
            EffectiveIntervals = effectiveIntervals;
            BuiltAtUtc = builtAtUtc;
            SchoolDayPrefix = schoolDayPrefix;
            SchoolHourPrefix = schoolHourPrefix;
            AppliedExclusions = appliedExclusions;
        }

        public static StaticStatisticsSnapshot Empty(
            DateTime startDate,
            DateTime endDate,
            DateTimeOffset builtAtUtc)
            => new(startDate, endDate, [], [], builtAtUtc, [0], [0], []);

        public static StaticStatisticsSnapshot Create(
            DateTime startDate,
            DateTime endDate,
            IReadOnlyList<DailyStatsRecord> records,
            IReadOnlyList<IReadOnlyList<(TimeSpan Start, TimeSpan End)>> effectiveIntervals,
            DateTimeOffset builtAtUtc)
        {
            var dayPrefix = new int[records.Count + 1];
            var hourPrefix = new double[records.Count + 1];
            for (var index = 0; index < records.Count; index++)
            {
                dayPrefix[index + 1] = dayPrefix[index] + (records[index].IsSchoolDay ? 1 : 0);
                hourPrefix[index + 1] = hourPrefix[index] + records[index].SchoolHours;
            }

            return new StaticStatisticsSnapshot(
                startDate,
                endDate,
                records,
                effectiveIntervals,
                builtAtUtc,
                dayPrefix,
                hourPrefix,
                BuildExclusionDetails(records));
        }

        public int GetExclusiveOffset(DateTime date)
        {
            if (Records.Count == 0 || date <= StartDate)
                return 0;
            if (date > EndDate)
                return Records.Count;
            return Math.Clamp((date.Date - StartDate).Days, 0, Records.Count);
        }

        public int GetInclusiveOffset(DateTime date)
        {
            if (Records.Count == 0 || date.Date < StartDate)
                return 0;
            if (date.Date >= EndDate)
                return Records.Count;
            return Math.Clamp((date.Date - StartDate).Days + 1, 0, Records.Count);
        }

        public int GetRecordIndex(DateTime date)
        {
            if (Records.Count == 0 || date.Date < StartDate || date.Date > EndDate)
                return -1;
            return (date.Date - StartDate).Days;
        }
    }
}
