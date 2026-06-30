using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

/// <summary>
/// 通过网络 API（timor.tech）获取中国法定节假日与调休安排。
/// 支持节假日 (holiday=true) 和调休补班 (holiday=false)。
/// </summary>
public class NetworkHolidayProvider : IHolidayProvider
{
    private static readonly Uri ApiBase = new("https://timor.tech/api/holiday/year/");

    private readonly HttpClient _httpClient;
    private readonly Dictionary<int, List<HolidayInfo>> _cache = [];
    private readonly object _cacheLock = new();
    public HolidayDataStatus Status { get; } = new()
    {
        Source = "联网节假日数据",
        Message = "尚未加载节假日数据"
    };

    public NetworkHolidayProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "ClassIsland.SchoolStats/1.0");
    }

    public async Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(int year)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(year, out var cached))
                return cached;
        }

        var list = await FetchYearAsync(year);
        lock (_cacheLock)
        {
            _cache[year] = list;
        }
        return list;
    }

    public void InvalidateCache(int year)
    {
        lock (_cacheLock)
        {
            _cache.Remove(year);
        }
    }

    private async Task<List<HolidayInfo>> FetchYearAsync(int year)
    {
        try
        {
            var url = $"{ApiBase}{year}/";
            var json = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("code", out var codeProp)
                || codeProp.GetInt32() != 0)
            {
                return SetFailedStatus(year, "接口返回异常");
            }

            if (!doc.RootElement.TryGetProperty("holiday", out var holidayProp))
                return SetFailedStatus(year, "接口响应缺少 holiday 字段");

            var result = new List<HolidayInfo>();
            foreach (var entry in holidayProp.EnumerateObject())
            {
                var item = entry.Value;
                if (!item.TryGetProperty("date", out var dateProp))
                    continue;

                var date = dateProp.GetDateTime();
                var isHoliday = item.TryGetProperty("holiday", out var holProp)
                    && holProp.GetBoolean();

                var name = item.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? ""
                    : "";

                result.Add(new HolidayInfo
                {
                    Name = name,
                    StartDate = date,
                    EndDate = date,
                    Category = isHoliday
                        ? HolidayCategory.LegalHoliday
                        : HolidayCategory.MakeUpWorkday
                });
            }

            Status.IsAvailable = result.Count > 0;
            Status.Message = result.Count > 0
                ? $"{year} 年已加载 {result.Count} 条记录"
                : $"{year} 年没有可用记录";
            Status.LastUpdatedAt = DateTime.Now;
            return result;
        }
        catch (HttpRequestException ex)
        {
            return SetFailedStatus(year, $"网络请求失败：{ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            return SetFailedStatus(year, $"请求超时：{ex.Message}");
        }
        catch (JsonException ex)
        {
            return SetFailedStatus(year, $"数据解析失败：{ex.Message}");
        }
    }

    private List<HolidayInfo> SetFailedStatus(int year, string message)
    {
        Status.IsAvailable = false;
        Status.Message = $"{year} 年加载失败，{message}";
        Status.LastUpdatedAt = DateTime.Now;
        return [];
    }
}
