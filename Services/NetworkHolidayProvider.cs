using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.SchoolStats.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SchoolStats.Services;

/// <summary>
/// Fetches Chinese legal holidays and make-up workdays from timor.tech.
/// A successful response is persisted so the last known data remains available
/// during a later network outage.
/// </summary>
public sealed class NetworkHolidayProvider : IHolidayProvider, IDisposable
{
    private static readonly Uri ApiBase = new("https://timor.tech/api/holiday/year/");
    private const int MaximumResponseBytes = 1024 * 1024;
    private const int MaximumRecordsPerYear = 512;
    private const int MaximumNameLength = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _cacheDirectory;
    private readonly ILogger<NetworkHolidayProvider>? _logger;
    private readonly Dictionary<int, IReadOnlyList<HolidayInfo>> _cache = [];
    private readonly Dictionary<int, SemaphoreSlim> _yearLocks = [];
    private readonly object _cacheLock = new();

    public HolidayDataStatus Status { get; } = new()
    {
        Source = "联网节假日数据",
        Message = "尚未加载节假日数据"
    };

    public NetworkHolidayProvider(
        string cacheDirectory,
        ILogger<NetworkHolidayProvider>? logger = null,
        HttpClient? httpClient = null)
    {
        _cacheDirectory = Path.Combine(cacheDirectory, "holiday-cache");
        _logger = logger;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = RequestTimeout
        };
        var assemblyVersion = typeof(NetworkHolidayProvider).Assembly
            .GetName().Version?.ToString(4) ?? "unknown";
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", $"ClassIsland.SchoolStats/{assemblyVersion}");
    }

    public async Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(year, out var cached))
                return cached;
        }

        var yearLock = GetYearLock(year);
        await yearLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(year, out var cached))
                    return cached;
            }

            var downloaded = await FetchYearAsync(year, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<HolidayInfo> result;
            if (downloaded.Count > 0)
            {
                result = downloaded;
                SavePersistentCache(year, downloaded);
                Status.Update(
                    "联网节假日数据",
                    $"{year} 年已加载 {downloaded.Count} 条记录",
                    true);
            }
            else
            {
                var persisted = await LoadPersistentCacheAsync(year, cancellationToken)
                    .ConfigureAwait(false);
                result = persisted;
                if (persisted.Count > 0)
                {
                    Status.Update(
                        "节假日缓存",
                        $"{year} 年联网不可用，已使用最近缓存（{persisted.Count} 条）",
                        true);
                }
            }

            if (result.Count > 0)
            {
                lock (_cacheLock)
                {
                    _cache[year] = result;
                }
            }
            return result;
        }
        finally
        {
            yearLock.Release();
        }
    }

    public void InvalidateCache(int year)
    {
        lock (_cacheLock)
        {
            _cache.Remove(year);
        }
    }

    private SemaphoreSlim GetYearLock(int year)
    {
        lock (_cacheLock)
        {
            if (!_yearLocks.TryGetValue(year, out var semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _yearLocks[year] = semaphore;
            }
            return semaphore;
        }
    }

    private async Task<List<HolidayInfo>> FetchYearAsync(
        int year,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(RequestTimeout);
        var requestToken = timeoutSource.Token;

        try
        {
            var url = new Uri(ApiBase, $"{year}/");
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                requestToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                SetFailureStatus(year, "接口响应超过大小限制");
                return [];
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(requestToken)
                .ConfigureAwait(false);
            using var boundedBuffer = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var bytesRead = await responseStream
                    .ReadAsync(buffer, requestToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                    break;
                if (boundedBuffer.Length + bytesRead > MaximumResponseBytes)
                {
                    SetFailureStatus(year, "接口响应超过大小限制");
                    return [];
                }
                await boundedBuffer.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    requestToken).ConfigureAwait(false);
            }
            boundedBuffer.Position = 0;
            using var document = await JsonDocument.ParseAsync(
                boundedBuffer,
                new JsonDocumentOptions { MaxDepth = 16 },
                requestToken).ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("code", out var codeProperty)
                || codeProperty.ValueKind != JsonValueKind.Number
                || !codeProperty.TryGetInt32(out var code)
                || code != 0)
            {
                SetFailureStatus(year, "接口返回异常");
                return [];
            }

            if (!document.RootElement.TryGetProperty("holiday", out var holidayProperty)
                || holidayProperty.ValueKind != JsonValueKind.Object)
            {
                SetFailureStatus(year, "接口响应缺少 holiday 字段");
                return [];
            }

            var result = new List<HolidayInfo>();
            var processedEntries = 0;
            foreach (var entry in holidayProperty.EnumerateObject())
            {
                processedEntries++;
                if (processedEntries > MaximumRecordsPerYear)
                {
                    SetFailureStatus(year, "接口返回的记录数量异常");
                    return [];
                }

                var item = entry.Value;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    SetFailureStatus(year, "接口包含格式错误的记录");
                    return [];
                }
                if (!item.TryGetProperty("date", out var dateProperty)
                    || dateProperty.ValueKind != JsonValueKind.String
                    || !dateProperty.TryGetDateTime(out var date)
                    || date.Year != year)
                {
                    SetFailureStatus(year, "接口包含无效日期记录");
                    return [];
                }

                if (!item.TryGetProperty("holiday", out var holidayFlag)
                    || holidayFlag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    SetFailureStatus(year, "接口包含无效 holiday 标记");
                    return [];
                }

                var isHoliday = holidayFlag.GetBoolean();
                var name = item.TryGetProperty("name", out var nameProperty)
                    && nameProperty.ValueKind == JsonValueKind.String
                        ? nameProperty.GetString() ?? string.Empty
                        : string.Empty;
                if (name.Length > MaximumNameLength)
                    name = name[..MaximumNameLength];
                if (string.IsNullOrWhiteSpace(name))
                {
                    SetFailureStatus(year, "接口包含未命名记录");
                    return [];
                }

                result.Add(new HolidayInfo
                {
                    Name = name,
                    StartDate = date.Date,
                    EndDate = date.Date,
                    Category = isHoliday
                        ? HolidayCategory.LegalHoliday
                        : HolidayCategory.MakeUpWorkday
                });
            }

            var validated = ValidateRecords(year, result);
            if (validated is null)
            {
                SetFailureStatus(year, "接口返回了冲突或越界记录");
                return [];
            }

            if (validated.Count == 0)
                SetFailureStatus(year, "接口没有返回可用记录");
            return validated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogWarning(ex, "Holiday request timed out for {Year}", year);
            SetFailureStatus(year, "请求超时");
            return [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _logger?.LogWarning(ex, "Unable to download holiday data for {Year}", year);
            SetFailureStatus(year, ex is TaskCanceledException ? "请求超时" : "网络或数据解析失败");
            return [];
        }
    }

    private async Task<IReadOnlyList<HolidayInfo>> LoadPersistentCacheAsync(
        int year,
        CancellationToken cancellationToken)
    {
        var path = GetCachePath(year);
        if (!File.Exists(path))
            return [];

        try
        {
            if (new FileInfo(path).Length > MaximumResponseBytes)
            {
                _logger?.LogWarning("Holiday cache for {Year} exceeds the size limit", year);
                return [];
            }
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var records = JsonSerializer.Deserialize<List<HolidayInfo?>>(json, CacheJsonOptions);
            return records is null ? [] : ValidateRecords(year, records) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Unable to read holiday cache for {Year}", year);
            return [];
        }
    }

    private void SavePersistentCache(int year, IReadOnlyList<HolidayInfo> holidays)
    {
        try
        {
            var json = JsonSerializer.Serialize(holidays, CacheJsonOptions);
            AtomicFile.WriteAllText(GetCachePath(year), json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Unable to persist holiday cache for {Year}", year);
        }
    }

    private string GetCachePath(int year) => Path.Combine(_cacheDirectory, $"holidays-{year}.json");

    private static List<HolidayInfo>? ValidateRecords(
        int year,
        IEnumerable<HolidayInfo?> records)
    {
        var result = new List<HolidayInfo>();
        var categoriesByDate = new Dictionary<DateTime, HolidayCategory>();
        var processedRecords = 0;
        foreach (var record in records)
        {
            processedRecords++;
            if (record is null
                || processedRecords > MaximumRecordsPerYear
                || record.StartDate.Date != record.EndDate.Date
                || record.StartDate.Year != year
                || record.Category is not (HolidayCategory.LegalHoliday or HolidayCategory.MakeUpWorkday)
                || string.IsNullOrWhiteSpace(record.Name))
            {
                return null;
            }

            var date = record.StartDate.Date;
            if (categoriesByDate.TryGetValue(date, out var existingCategory))
            {
                if (existingCategory != record.Category)
                    return null;
                continue;
            }

            categoriesByDate[date] = record.Category;
            var normalizedName = record.Name.Trim();
            if (normalizedName.Length > MaximumNameLength)
                normalizedName = normalizedName[..MaximumNameLength];
            result.Add(new HolidayInfo
            {
                Name = normalizedName,
                StartDate = date,
                EndDate = date,
                Category = record.Category
            });
        }

        return result;
    }

    private void SetFailureStatus(int year, string message)
        => Status.Update("联网节假日数据", $"{year} 年加载失败，{message}", false);

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
