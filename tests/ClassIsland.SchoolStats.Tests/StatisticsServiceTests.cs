using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;

namespace ClassIsland.SchoolStats.Tests;

public sealed class StatisticsServiceTests
{
    [Fact]
    public async Task OverlappingManualExclusionsAreSubtractedOnlyOnce()
    {
        var date = new DateTime(2026, 7, 6);
        var configuration = ConfigurationForSingleDay(date);
        configuration.ManualTimeExclusions =
        [
            Exclusion(date, 9, 11),
            Exclusion(date, 10, 12),
            Exclusion(date, 9, 11)
        ];
        using var service = new StatisticsService(configuration, new StubHolidayDataService());

        var stats = await service.CalculateStatsAsync(date.AddHours(18));

        Assert.Equal(5, stats.TotalSchoolHours, 6);
        Assert.Equal(5, stats.PassedSchoolHours, 6);
    }

    [Theory]
    [InlineData(7, 59, 0)]
    [InlineData(8, 0, 0)]
    [InlineData(10, 30, 2.5)]
    [InlineData(12, 0, 4)]
    [InlineData(12, 59, 4)]
    [InlineData(13, 0, 4)]
    [InlineData(15, 30, 6.5)]
    [InlineData(17, 0, 8)]
    [InlineData(18, 0, 8)]
    public async Task CurrentDayHoursRespectSchoolAndLunchBoundaries(
        int hour,
        int minute,
        double expectedHours)
    {
        var date = new DateTime(2026, 7, 6);
        using var service = new StatisticsService(
            ConfigurationForSingleDay(date),
            new StubHolidayDataService());

        var stats = await service.CalculateStatsAsync(date.AddHours(hour).AddMinutes(minute));

        Assert.Equal(expectedHours, stats.PassedSchoolHours, 6);
    }

    [Fact]
    public async Task DateSpecificScheduleOverridesWeekdaySchedule()
    {
        var date = new DateTime(2026, 7, 6);
        var configuration = ConfigurationForSingleDay(date);
        var monday = Template("星期一", 8, 15);
        var special = Template("指定日期", 9, 14);
        configuration.ScheduleTemplates = [configuration.ScheduleTemplates[0], monday, special];
        configuration.ScheduleTemplateRules =
        [
            new ScheduleTemplateRule { TemplateId = monday.Id, DayOfWeek = DayOfWeek.Monday },
            new ScheduleTemplateRule { TemplateId = special.Id, Date = date }
        ];
        using var service = new StatisticsService(configuration, new StubHolidayDataService());

        var stats = await service.CalculateStatsAsync(date.AddHours(18));

        Assert.Equal(special.DailyHours, stats.TotalSchoolHours, 6);
    }

    [Fact]
    public async Task CrossYearSemesterWarmsAndAggregatesBothYears()
    {
        var configuration = new SemesterConfiguration
        {
            StartDate = new DateTime(2025, 12, 29),
            EndDate = new DateTime(2026, 1, 4),
            ExcludeWeekends = true
        };
        var holidayStart = new DateTime(2025, 12, 31);
        var holidayEnd = new DateTime(2026, 1, 2);
        var holidayService = new StubHolidayDataService(date =>
            date >= holidayStart && date <= holidayEnd
                ? new SchoolDayResult(false, "跨年假期")
                : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    ? new SchoolDayResult(false, "周末")
                    : new SchoolDayResult(true, null));
        using var service = new StatisticsService(configuration, holidayService);

        var stats = await service.CalculateStatsAsync(new DateTime(2026, 1, 4, 18, 0, 0));

        Assert.Equal(2, stats.TotalSchoolDays);
        Assert.Equal(16, stats.TotalSchoolHours, 6);
        Assert.Equal(new DateTime(2025, 12, 29), stats.CurrentWeek.StartDate);
        Assert.Equal(new DateTime(2026, 1, 4), stats.CurrentWeek.EndDate);
    }

    [Fact]
    public async Task InvalidSemesterRangeReturnsSafeEmptyResult()
    {
        var configuration = new SemesterConfiguration
        {
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 8, 1)
        };
        using var service = new StatisticsService(configuration, new StubHolidayDataService());

        var stats = await service.CalculateStatsAsync(new DateTime(2026, 8, 15));

        Assert.Equal(0, stats.TotalSchoolDays);
        Assert.Equal(0, stats.TotalSchoolHours);
        Assert.Equal(0, stats.ProgressPercentage);
    }

    [Fact]
    public async Task StaticSemesterSnapshotIsReusedAndInvalidatedByConfigurationChange()
    {
        var date = new DateTime(2026, 7, 6);
        var configuration = ConfigurationForSingleDay(date);
        var holidayService = new StubHolidayDataService();
        using var service = new StatisticsService(configuration, holidayService);

        await service.CalculateStatsAsync(date.AddHours(9));
        await service.CalculateStatsAsync(date.AddHours(10));
        Assert.Equal(1, holidayService.ResolveCallCount);

        configuration.SchoolEndTime = TimeSpan.FromHours(18);
        var rebuilt = await service.CalculateStatsAsync(date.AddHours(19));

        Assert.Equal(2, holidayService.ResolveCallCount);
        Assert.Equal(9, rebuilt.TotalSchoolHours, 6);
    }

    [Fact]
    public async Task ExcessiveSemesterRangeIsRejectedBeforeAnyHolidayRequests()
    {
        var configuration = new SemesterConfiguration
        {
            StartDate = new DateTime(2020, 1, 1),
            EndDate = new DateTime(2025, 1, 1)
        };
        var holidayService = new StubHolidayDataService();
        using var service = new StatisticsService(configuration, holidayService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CalculateStatsAsync(new DateTime(2024, 1, 1)));

        Assert.Equal(0, holidayService.ResolveCallCount);
        Assert.Contains(
            configuration.ValidateConfiguration(),
            issue => issue.Severity == ConfigurationIssueSeverity.Error
                && issue.Message.Contains(SemesterConfiguration.MaximumSemesterDays.ToString()));
    }

    [Fact]
    public async Task MaximumDateSingleDaySemesterDoesNotOverflow()
    {
        var date = DateTime.MaxValue.Date;
        using var service = new StatisticsService(
            ConfigurationForSingleDay(date),
            new StubHolidayDataService());

        var stats = await service.CalculateStatsAsync(date.AddHours(18));

        Assert.Equal(1, stats.TotalSchoolDays);
        Assert.Equal(8, stats.TotalSchoolHours, 6);
        Assert.Equal(100, stats.ProgressPercentage);
    }

    [Fact]
    public async Task NetworkSnapshotRefreshesAfterItsLifetime()
    {
        var date = new DateTime(2026, 7, 6);
        var configuration = ConfigurationForSingleDay(date);
        configuration.EnableNetworkHolidayUpdate = true;
        var holidayService = new StubHolidayDataService();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(date, TimeSpan.Zero));
        using var service = new StatisticsService(
            configuration,
            holidayService,
            timeProvider: timeProvider);

        await service.CalculateStatsAsync(date.AddHours(9));
        timeProvider.Advance(TimeSpan.FromHours(13));
        await service.CalculateStatsAsync(date.AddHours(10));

        Assert.Equal(2, holidayService.ResolveCallCount);
        Assert.Equal(1, holidayService.InvalidateCallCount);
    }

    [Fact]
    public async Task ConcurrentExpiredSnapshotCannotRebuildBeforeHolidayInvalidation()
    {
        var date = new DateTime(2026, 7, 6);
        var configuration = ConfigurationForSingleDay(date);
        configuration.EnableNetworkHolidayUpdate = true;
        using var holidayService = new BlockingRefreshHolidayDataService();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(date, TimeSpan.Zero));
        using var service = new StatisticsService(
            configuration,
            holidayService,
            timeProvider: timeProvider);
        Assert.Equal(1, (await service.CalculateStatsAsync(date.AddHours(18))).TotalSchoolDays);
        timeProvider.Advance(TimeSpan.FromHours(13));

        var firstRefresh = Task.Run(() => service.CalculateStatsAsync(date.AddHours(18)));
        Assert.True(holidayService.InvalidationEntered.Wait(TimeSpan.FromSeconds(5)));
        var concurrentRefresh = Task.Run(() => service.CalculateStatsAsync(date.AddHours(18)));
        holidayService.ContinueInvalidation.Set();
        var results = await Task.WhenAll(firstRefresh, concurrentRefresh);

        Assert.All(results, stats => Assert.Equal(0, stats.TotalSchoolDays));
    }

    private static SemesterConfiguration ConfigurationForSingleDay(DateTime date)
        => new()
        {
            StartDate = date,
            EndDate = date,
            ExcludeWeekends = false
        };

    private static ManualTimeExclusion Exclusion(DateTime date, int startHour, int endHour)
        => new()
        {
            Date = date,
            StartTime = TimeSpan.FromHours(startHour),
            EndTime = TimeSpan.FromHours(endHour)
        };

    private static ScheduleTemplate Template(string name, int startHour, int endHour)
        => new()
        {
            Name = name,
            SchoolStartTime = TimeSpan.FromHours(startHour),
            LunchStartTime = TimeSpan.FromHours(12),
            LunchEndTime = TimeSpan.FromHours(13),
            SchoolEndTime = TimeSpan.FromHours(endHour)
        };
}
