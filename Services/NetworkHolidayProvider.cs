using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class NetworkHolidayProvider : IHolidayProvider
{
    private readonly HttpClient? _httpClient;
    private readonly Dictionary<int, List<HolidayInfo>> _cache = [];

    public NetworkHolidayProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(int year)
    {
        if (_cache.TryGetValue(year, out var cached))
            return cached;

        _cache[year] = [];
        return _cache[year];
    }

    public void InvalidateCache(int year) => _cache.Remove(year);
}
