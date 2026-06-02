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
        if (_cache.TryGetValue(year, out var cached))
            return cached;

        var list = await FetchYearAsync(year);
        _cache[year] = list;
        return list;
    }

    public void InvalidateCache(int year) => _cache.Remove(year);

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
                return [];
            }

            if (!doc.RootElement.TryGetProperty("holiday", out var holidayProp))
                return [];

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

            return result;
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (TaskCanceledException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
