using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public class LocalHolidayProvider : IHolidayProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string[] _dataDirs;
    private readonly object _cacheLock = new();
    private Dictionary<int, List<HolidayInfo>>? _cache;
    public HolidayDataStatus Status { get; } = new()
    {
        Source = "本地节假日数据",
        Message = "尚未加载节假日数据"
    };

    public LocalHolidayProvider(params string[] dataDirs)
    {
        _dataDirs = dataDirs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(int year)
    {
        lock (_cacheLock)
        {
            if (_cache != null)
                return _cache.TryGetValue(year, out var cachedList) ? cachedList : [];
        }

        var loaded = await LoadAllAsync();
        lock (_cacheLock)
        {
            _cache ??= loaded;
            return _cache.TryGetValue(year, out var list) ? list : [];
        }
    }

    public void InvalidateCache(int year)
    {
        lock (_cacheLock)
        {
            _cache?.Remove(year);
        }
    }

    private async Task<Dictionary<int, List<HolidayInfo>>> LoadAllAsync()
    {
        var path = _dataDirs
            .Select(x => Path.Combine(x, "Assets", "holidays.json"))
            .FirstOrDefault(File.Exists);

        if (path == null)
        {
            Status.IsAvailable = false;
            Status.Message = "未找到 Assets/holidays.json";
            Status.LastUpdatedAt = DateTime.Now;
            return [];
        }

        List<HolidayJsonRecord> records;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            records = JsonSerializer.Deserialize<List<HolidayJsonRecord>>(json, JsonOptions) ?? [];
            Status.IsAvailable = records.Count > 0;
            Status.Message = records.Count > 0
                ? $"已加载 {records.Count} 条记录"
                : "文件为空";
            Status.LastUpdatedAt = DateTime.Now;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Status.IsAvailable = false;
            Status.Message = $"加载失败：{ex.Message}";
            Status.LastUpdatedAt = DateTime.Now;
            return [];
        }

        var result = new Dictionary<int, List<HolidayInfo>>();
        foreach (var record in records)
        {
            var startYear = record.StartDate.Year;
            var endYear = record.EndDate.Year;
            for (var year = startYear; year <= endYear; year++)
            {
                if (!result.TryGetValue(year, out var list))
                {
                    list = [];
                    result[year] = list;
                }

                list.Add(new HolidayInfo
                {
                    Name = record.Name,
                    StartDate = record.StartDate,
                    EndDate = record.EndDate,
                    Category = record.Category
                });
            }
        }

        return result;
    }

    private class HolidayJsonRecord
    {
        public string Name { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public HolidayCategory Category { get; set; }
    }
}
