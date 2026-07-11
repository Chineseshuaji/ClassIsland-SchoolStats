using System.ComponentModel;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SchoolStats.Controls;

[ComponentInfo("4E8A2C7D-3F1B-4A9E-B6D5-08C1E3F7A9B2", "在校时间统计", "\uf19d", "统计学期内在校天数与时长，自动排除假期")]
public partial class StatsComponent : ComponentBase<StatsComponentSettings>
{
    private readonly IStatisticsService _statisticsService;
    private readonly SemesterConfiguration _configuration;
    private readonly IExactTimeService _exactTimeService;
    private readonly ILogger<StatsComponent>? _logger;
    private readonly DispatcherTimer _refreshTimer = new();
    private CancellationTokenSource? _lifetimeCancellation;
    private DateTime _lastReferenceSecond;
    private AggregatedStats? _cachedStats;
    private bool _isSubscribed;
    private bool _refreshQueued;
    private bool _refreshRunning;

    public StatsComponent()
        : this(
            IAppHost.GetService<IStatisticsService>(),
            IAppHost.GetService<SemesterConfiguration>(),
            IAppHost.GetService<IExactTimeService>(),
            IAppHost.GetService<ILogger<StatsComponent>>())
    {
    }

    public StatsComponent(
        IStatisticsService statisticsService,
        SemesterConfiguration configuration,
        IExactTimeService exactTimeService,
        ILogger<StatsComponent>? logger = null)
    {
        _statisticsService = statisticsService;
        _configuration = configuration;
        _exactTimeService = exactTimeService;
        _logger = logger;
        InitializeComponent();
        ShowLoadingState();

        _refreshTimer.Tick += (_, _) => RequestRefresh();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }

    private void Attach()
    {
        if (_isSubscribed)
            return;

        _isSubscribed = true;
        _lifetimeCancellation = new CancellationTokenSource();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        _configuration.PropertyChanged += OnConfigurationPropertyChanged;
        SyncSettings();
        RestartRefreshTimer();
        RequestRefresh(force: true);
    }

    private void Detach()
    {
        if (!_isSubscribed)
            return;

        _isSubscribed = false;
        _refreshTimer.Stop();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _configuration.PropertyChanged -= OnConfigurationPropertyChanged;
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;
        _refreshQueued = false;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncSettings();
        RestartRefreshTimer();
        RequestRefresh(force: true);
    }

    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RequestRefresh(force: true);

    private void SyncSettings()
    {
        CompactStack.IsVisible = Settings.CompactMode;
        StandardStack.IsVisible = !Settings.CompactMode;
        ProgressBar.IsVisible = Settings.ShowProgressBar;
        HoursLabel.IsVisible = Settings.ShowHours;
    }

    private void RestartRefreshTimer()
    {
        _refreshTimer.Stop();
        _refreshTimer.Interval = Settings.RemainingTimePrecision switch
        {
            RemainingTimeDisplayPrecision.Days => TimeSpan.FromMinutes(5),
            RemainingTimeDisplayPrecision.Hours => TimeSpan.FromMinutes(1),
            RemainingTimeDisplayPrecision.Minutes => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(1)
        };
        if (_isSubscribed)
            _refreshTimer.Start();
    }

    private void RequestRefresh(bool force = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RequestRefresh(force));
            return;
        }

        if (!_isSubscribed)
            return;

        if (force)
        {
            _cachedStats = null;
            _lastReferenceSecond = default;
        }

        _refreshQueued = true;
        if (!_refreshRunning)
            _ = ProcessRefreshQueueAsync();
    }

    private async Task ProcessRefreshQueueAsync()
    {
        _refreshRunning = true;
        try
        {
            while (_isSubscribed && _refreshQueued)
            {
                _refreshQueued = false;
                await UpdateDisplayAsync();
            }
        }
        finally
        {
            _refreshRunning = false;
        }
    }

    private async Task UpdateDisplayAsync()
    {
        var cancellationToken = _lifetimeCancellation?.Token ?? new CancellationToken(true);
        var referenceTime = _exactTimeService.GetCurrentLocalDateTime();
        var referenceSecond = new DateTime(
            referenceTime.Year,
            referenceTime.Month,
            referenceTime.Day,
            referenceTime.Hour,
            referenceTime.Minute,
            referenceTime.Second);

        if (_cachedStats is not null && _lastReferenceSecond == referenceSecond)
        {
            Render(_cachedStats, referenceTime);
            return;
        }

        try
        {
            var stats = await _statisticsService
                .CalculateStatsAsync(referenceTime, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _lastReferenceSecond = referenceSecond;
            _cachedStats = stats;
            Render(stats, referenceTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Component was detached or a host shutdown is in progress.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unable to refresh SchoolStats component");
            ShowErrorState("统计暂时不可用，请检查插件设置或日志。");
        }
    }

    private void Render(AggregatedStats stats, DateTime referenceTime)
    {
        var remainingTimeText = stats.FormatRemainingSchoolTime(Settings.RemainingTimePrecision);
        var compactRemainingTimeText = stats.FormatRemainingSchoolTime(
            Settings.RemainingTimePrecision,
            true);
        var approximateHoursText = Settings.ShowHours
            && Settings.RemainingTimePrecision != RemainingTimeDisplayPrecision.Hours
                ? $"（约 {stats.RemainingSchoolHours:F1} 小时）"
                : string.Empty;

        DaysLabel.Text = $"{stats.PassedSchoolDays} 天 / {stats.TotalSchoolDays} 天";
        HoursLabel.Text = $"实际在校时间还有 {remainingTimeText}{approximateHoursText}";
        WeekLabel.Text = $"本周剩余 {stats.CurrentWeek.RemainingSchoolHours:F1} 小时 · "
            + $"{stats.CurrentWeek.ProgressPercentage:F1}%";
        ProgressBar.Value = stats.ProgressPercentage;
        CompactDaysLabel.Text = $"余 {compactRemainingTimeText}";
        CompactPercentLabel.Text = $"{stats.ProgressPercentage:F1}%";

        TooltipTitle.Text = $"在校时间统计（截至 {referenceTime:yyyy-MM-dd HH:mm:ss}）";
        TooltipDetail.Text = $"学期：{_configuration.StartDate:yyyy-MM-dd} ~ {_configuration.EndDate:yyyy-MM-dd}\n"
            + $"已在校：{stats.PassedSchoolDays} 天（{stats.PassedSchoolHours:F1} 小时）\n"
            + $"剩余实际在校时间：{remainingTimeText}（约 {stats.RemainingSchoolHours:F1} 小时）\n"
            + $"剩余完整在校日：{stats.RemainingSchoolDays} 天\n"
            + $"本周：{stats.CurrentWeek.StartDate:MM-dd} ~ {stats.CurrentWeek.EndDate:MM-dd}\n"
            + $"本周已在校：{stats.CurrentWeek.PassedSchoolDays}/{stats.CurrentWeek.TotalSchoolDays} 天"
            + $"（{stats.CurrentWeek.PassedSchoolHours:F1}/{stats.CurrentWeek.TotalSchoolHours:F1} 小时）\n"
            + $"本周剩余：{stats.CurrentWeek.RemainingSchoolDays} 天"
            + $"（{stats.CurrentWeek.RemainingSchoolHours:F1} 小时）\n"
            + $"进度：{stats.ProgressPercentage:F1}%";

        TooltipExclusions.Text = stats.AppliedExclusions.Count == 0
            ? string.Empty
            : "已排除：\n" + string.Join(
                "\n",
                stats.AppliedExclusions.Take(5).Select(detail =>
                    detail.StartDate == detail.EndDate
                        ? $"{detail.Name}（{detail.StartDate:MM-dd}）：1 天"
                        : $"{detail.Name}（{detail.StartDate:MM-dd}~{detail.EndDate:MM-dd}）："
                          + $"{detail.ExcludedDays} 天"));
    }

    private void ShowLoadingState()
    {
        DaysLabel.Text = "正在计算…";
        HoursLabel.Text = string.Empty;
        WeekLabel.Text = string.Empty;
        CompactDaysLabel.Text = "计算中…";
        CompactPercentLabel.Text = string.Empty;
        TooltipTitle.Text = "在校时间统计";
        TooltipDetail.Text = "正在加载节假日数据并计算学期进度。";
        TooltipExclusions.Text = string.Empty;
        ProgressBar.Value = 0;
    }

    private void ShowErrorState(string message)
    {
        DaysLabel.Text = "统计不可用";
        HoursLabel.Text = message;
        WeekLabel.Text = string.Empty;
        CompactDaysLabel.Text = "不可用";
        CompactPercentLabel.Text = string.Empty;
        TooltipTitle.Text = "在校时间统计";
        TooltipDetail.Text = message;
        TooltipExclusions.Text = string.Empty;
    }

    public void ForceRefresh() => RequestRefresh(force: true);
}
