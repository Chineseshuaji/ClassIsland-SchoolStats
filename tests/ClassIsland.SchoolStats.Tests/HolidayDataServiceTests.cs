using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;

namespace ClassIsland.SchoolStats.Tests;

public sealed class HolidayDataServiceTests
{
    [Fact]
    public async Task CustomWorkdayOverridesEveryOtherRule()
    {
        var date = new DateTime(2026, 5, 2);
        var configuration = new SemesterConfiguration
        {
            CustomWorkdays = [Holiday("补课", date, HolidayCategory.MakeUpWorkday)],
            CustomHolidays = [Holiday("校假", date, HolidayCategory.Custom)]
        };
        var provider = new StubHolidayProvider(Holiday("法定假", date, HolidayCategory.LegalHoliday));
        using var service = new HolidayDataService(provider, configuration);

        var result = await service.GetSchoolDayAsync(date);

        Assert.True(result.IsSchoolDay);
        Assert.Null(result.ExclusionReason);
    }

    [Fact]
    public async Task CustomHolidayOverridesOfficialMakeUpWorkday()
    {
        var date = new DateTime(2026, 5, 9);
        var configuration = new SemesterConfiguration
        {
            CustomHolidays = [Holiday("校运会", date, HolidayCategory.Custom)]
        };
        var provider = new StubHolidayProvider(Holiday("劳动节补班", date, HolidayCategory.MakeUpWorkday));
        using var service = new HolidayDataService(provider, configuration);

        var result = await service.GetSchoolDayAsync(date);

        Assert.False(result.IsSchoolDay);
        Assert.Contains("校运会", result.ExclusionReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficialConflictIsIndependentOfInputOrder(bool makeUpFirst)
    {
        var date = new DateTime(2026, 9, 20);
        var legalHoliday = Holiday("法定假", date, HolidayCategory.LegalHoliday);
        var makeUpWorkday = Holiday("调休补班", date, HolidayCategory.MakeUpWorkday);
        var provider = makeUpFirst
            ? new StubHolidayProvider(makeUpWorkday, legalHoliday)
            : new StubHolidayProvider(legalHoliday, makeUpWorkday);
        using var service = new HolidayDataService(provider, new SemesterConfiguration());

        var result = await service.GetSchoolDayAsync(date);

        Assert.True(result.IsSchoolDay);
    }

    [Fact]
    public async Task OfficialMakeUpWorkdayOverridesWeekend()
    {
        var date = new DateTime(2026, 1, 4);
        var provider = new StubHolidayProvider(Holiday("元旦补班", date, HolidayCategory.MakeUpWorkday));
        using var service = new HolidayDataService(provider, new SemesterConfiguration());

        Assert.True((await service.GetSchoolDayAsync(date)).IsSchoolDay);
    }

    [Fact]
    public async Task OfficialLegalHolidayIsExcludedWithItsReason()
    {
        var date = new DateTime(2026, 10, 1);
        var provider = new StubHolidayProvider(Holiday("国庆节", date, HolidayCategory.LegalHoliday));
        using var service = new HolidayDataService(provider, new SemesterConfiguration());

        var result = await service.GetSchoolDayAsync(date);

        Assert.False(result.IsSchoolDay);
        Assert.Contains("国庆节", result.ExclusionReason);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WeekendRuleFollowsConfiguration(bool excludeWeekends, bool expectedSchoolDay)
    {
        var configuration = new SemesterConfiguration { ExcludeWeekends = excludeWeekends };
        using var service = new HolidayDataService(new StubHolidayProvider(), configuration);

        var result = await service.GetSchoolDayAsync(new DateTime(2026, 7, 11));

        Assert.Equal(expectedSchoolDay, result.IsSchoolDay);
    }

    [Fact]
    public async Task OrdinaryWeekdayIsSchoolDay()
    {
        using var service = new HolidayDataService(
            new StubHolidayProvider(),
            new SemesterConfiguration());

        Assert.True((await service.GetSchoolDayAsync(new DateTime(2026, 7, 6))).IsSchoolDay);
    }

    private static HolidayInfo Holiday(string name, DateTime date, HolidayCategory category)
        => new()
        {
            Name = name,
            StartDate = date,
            EndDate = date,
            Category = category
        };
}
