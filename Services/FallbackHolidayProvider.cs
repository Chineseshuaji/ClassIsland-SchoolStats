using ClassIsland.SchoolStats.Models;
using System.Text.Json;

namespace ClassIsland.SchoolStats.Services;

public class FallbackHolidayProvider : IHolidayProvider
{
    private readonly IHolidayProvider _primary;
    private readonly IHolidayProvider _fallback;
    private readonly Models.SemesterConfiguration? _configuration;

    public HolidayDataStatus Status { get; } = new()
    {
        Source = "节假日数据",
        Message = "尚未加载节假日数据"
    };

    public FallbackHolidayProvider(
        IHolidayProvider primary,
        IHolidayProvider fallback,
        Models.SemesterConfiguration? configuration = null)
    {
        _primary = primary;
        _fallback = fallback;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<HolidayInfo>> GetHolidaysAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        if (_configuration is { EnableNetworkHolidayUpdate: false })
        {
            var localOnlyResult = await _fallback.GetHolidaysAsync(year, cancellationToken)
                .ConfigureAwait(false);
            UpdateStatus(
                "本地节假日数据",
                localOnlyResult.Count > 0
                    ? $"{year} 年已加载 {localOnlyResult.Count} 条记录（联网更新已关闭）"
                    : $"{year} 年没有可用的本地数据",
                localOnlyResult.Count > 0);
            return localOnlyResult;
        }

        IReadOnlyList<HolidayInfo> primaryResult;
        try
        {
            primaryResult = await _primary.GetHolidaysAsync(year, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            Status.Update(
                "节假日数据",
                $"{year} 年联网数据异常，正在回退到本地数据",
                false);
            primaryResult = [];
        }
        if (primaryResult.Count > 0)
        {
            UpdateStatus(
                _primary.Status.Source,
                $"{year} 年已加载 {primaryResult.Count} 条记录",
                true);
            return primaryResult;
        }

        var fallbackResult = await _fallback.GetHolidaysAsync(year, cancellationToken)
            .ConfigureAwait(false);
        if (fallbackResult.Count > 0)
        {
            UpdateStatus(
                "节假日数据",
                $"{year} 年联网暂无数据，已回退到本地数据（{fallbackResult.Count} 条）",
                true);
            return fallbackResult;
        }

        UpdateStatus(
            "节假日数据",
            $"{year} 年联网和本地均无可用数据",
            false);
        return [];
    }

    public void InvalidateCache(int year)
    {
        _primary.InvalidateCache(year);
        _fallback.InvalidateCache(year);
        Status.Update("节假日数据", $"{year} 年缓存已失效，等待重新加载", false);
    }

    private void UpdateStatus(string source, string message, bool isAvailable)
    {
        Status.Update(source, message, isAvailable);
    }
}
