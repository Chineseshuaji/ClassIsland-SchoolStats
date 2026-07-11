using System.Runtime.CompilerServices;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;

namespace ClassIsland.SchoolStats.Tests;

public sealed class BundledHolidayDataTests
{
    [Fact]
    public async Task Bundled2026DataMatchesPublishedKeyDates()
    {
        var provider = new LocalHolidayProvider(GetRepositoryRoot());
        var holidays = await provider.GetHolidaysAsync(2026);

        Assert.Contains(holidays, holiday =>
            holiday.Name == "元旦调休补班"
            && holiday.StartDate == new DateTime(2026, 1, 4)
            && holiday.Category == HolidayCategory.MakeUpWorkday);
        Assert.Contains(holidays, holiday =>
            holiday.Name == "春节"
            && holiday.StartDate == new DateTime(2026, 2, 15)
            && holiday.EndDate == new DateTime(2026, 2, 23));
        Assert.Contains(holidays, holiday =>
            holiday.StartDate == new DateTime(2026, 9, 20)
            && holiday.Category == HolidayCategory.MakeUpWorkday);
        Assert.DoesNotContain(holidays, holiday =>
            holiday.StartDate == new DateTime(2026, 9, 27)
            && holiday.Category == HolidayCategory.MakeUpWorkday);
    }

    [Fact]
    public async Task BundledDataDoesNotPresentUnpublished2027ScheduleAsOfficial()
    {
        var provider = new LocalHolidayProvider(GetRepositoryRoot());

        Assert.Empty(await provider.GetHolidaysAsync(2027));
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
