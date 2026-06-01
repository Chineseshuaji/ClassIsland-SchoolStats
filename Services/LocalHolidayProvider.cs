using System.Text.Json;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class LocalHolidayProvider : IHolidayProvider
{
    private readonly string _dataDir;
    private Dictionary<int, List<HolidayInfo>>? _cache;

    public LocalHolidayProvider(string dataDir)
    {
        _dataDir = dataDir;
    }

    public async Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(int year)
    {
        _cache ??= await LoadAllAsync();
        return _cache.TryGetValue(year, out var list) ? list : [];
    }

    public void InvalidateCache(int year)
    {
        _cache?.Remove(year);
    }

    private async Task<Dictionary<int, List<HolidayInfo>>> LoadAllAsync()
    {
        var path = Path.Combine(_dataDir, "Assets", "holidays.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        var records = JsonSerializer.Deserialize<List<HolidayJsonRecord>>(json) ?? [];

        return records
            .GroupBy(r => r.StartDate.Year)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new HolidayInfo
                {
                    Name = r.Name,
                    StartDate = r.StartDate,
                    EndDate = r.EndDate,
                    Category = r.Category
                }).ToList()
            );
    }

    private class HolidayJsonRecord
    {
        public string Name { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public HolidayCategory Category { get; set; }
    }
}
