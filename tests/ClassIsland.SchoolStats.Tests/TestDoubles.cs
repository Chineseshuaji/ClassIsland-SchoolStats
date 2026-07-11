using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;

namespace ClassIsland.SchoolStats.Tests;

internal sealed class StubHolidayProvider : IHolidayProvider
{
    private readonly IReadOnlyList<HolidayInfo> _holidays;

    public StubHolidayProvider(params HolidayInfo[] holidays)
    {
        _holidays = holidays;
    }

    public HolidayDataStatus Status { get; } = new();
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult<IReadOnlyList<HolidayInfo>>(
            _holidays.Where(holiday =>
                holiday.StartDate.Year <= year && holiday.EndDate.Year >= year).ToList());
    }

    public void InvalidateCache(int year)
    {
    }
}

internal sealed class StubHolidayDataService : IHolidayDataService
{
    private readonly Func<DateTime, SchoolDayResult> _resolver;

    public StubHolidayDataService(Func<DateTime, SchoolDayResult>? resolver = null)
    {
        _resolver = resolver ?? (_ => new SchoolDayResult(true, null));
    }

    public int ResolveCallCount { get; private set; }
    public int InvalidateCallCount { get; private set; }

    public Task<SchoolDayResult> GetSchoolDayAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResolveCallCount++;
        return Task.FromResult(_resolver(date.Date));
    }

    public Task WarmUpAsync(
        IEnumerable<int> years,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void InvalidateCache()
    {
        InvalidateCallCount++;
    }
}

internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow += duration;
}

internal sealed class BlockingRefreshHolidayDataService : IHolidayDataService, IDisposable
{
    private volatile bool _isSchoolDay = true;

    public ManualResetEventSlim InvalidationEntered { get; } = new(false);
    public ManualResetEventSlim ContinueInvalidation { get; } = new(false);

    public Task<SchoolDayResult> GetSchoolDayAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_isSchoolDay
            ? new SchoolDayResult(true, null)
            : new SchoolDayResult(false, "刷新后的假期"));

    public Task WarmUpAsync(
        IEnumerable<int> years,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void InvalidateCache()
    {
        InvalidationEntered.Set();
        if (!ContinueInvalidation.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Test invalidation was not released.");
        _isSchoolDay = false;
    }

    public void Dispose()
    {
        InvalidationEntered.Dispose();
        ContinueInvalidation.Dispose();
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "ClassIsland.SchoolStats.Tests",
        Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
        catch
        {
            // Test cleanup is best effort on Windows where virus scanners may hold files briefly.
        }
    }
}
