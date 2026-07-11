using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;
using ClassIsland.Shared;

namespace ClassIsland.SchoolStats.Controls;

public partial class StatsComponentSettingsControl : ComponentBase<StatsComponentSettings>
{
    private SemesterConfiguration? _config;
    private IStatisticsService? _statsService;
    private IHolidayProvider? _holidayProvider;
    private PluginConfigurationStore? _configurationStore;
    private readonly ObservableCollection<HolidayDisplayItem> _holidays = [];
    private readonly ObservableCollection<HolidayDisplayItem> _workdays = [];
    private readonly ObservableCollection<ScheduleTemplateDisplayItem> _templates = [];
    private readonly ObservableCollection<ScheduleRuleDisplayItem> _scheduleRules = [];
    private readonly ObservableCollection<ManualExclusionDisplayItem> _manualExclusions = [];
    private readonly DispatcherTimer _holidayStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _isInitialized;
    private bool _isLoadingUi;

    public StatsComponentSettingsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        HolidayListBox.ItemsSource = _holidays;
        WorkdayListBox.ItemsSource = _workdays;
        ScheduleTemplateListBox.ItemsSource = _templates;
        RuleTemplateCombo.ItemsSource = _templates;
        ScheduleRuleListBox.ItemsSource = _scheduleRules;
        ManualExclusionListBox.ItemsSource = _manualExclusions;
        _holidayStatusTimer.Tick += (_, _) => UpdateHolidayStatus();
        Unloaded += (_, _) => _holidayStatusTimer.Stop();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _config = IAppHost.GetService<SemesterConfiguration>();
        _statsService = IAppHost.GetService<IStatisticsService>();
        _holidayProvider = IAppHost.GetService<IHolidayProvider>();
        _configurationStore = IAppHost.GetService<PluginConfigurationStore>();

        if (_isInitialized)
        {
            ApplyDisplaySettingsToUi();
            LoadSemesterConfig();
            UpdateHolidayStatus();
            _holidayStatusTimer.Start();
            return;
        }
        _isInitialized = true;

        // 显示设置绑定
        ShowProgressBarCheck.IsCheckedChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            Settings.ShowProgressBar = ShowProgressBarCheck.IsChecked == true;
        };
        ShowHoursCheck.IsCheckedChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            Settings.ShowHours = ShowHoursCheck.IsChecked == true;
        };
        CompactModeCheck.IsCheckedChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            Settings.CompactMode = CompactModeCheck.IsChecked == true;
        };
        RemainingTimePrecisionCombo.SelectionChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (RemainingTimePrecisionCombo.SelectedIndex >= 0)
                Settings.RemainingTimePrecision = (RemainingTimeDisplayPrecision)RemainingTimePrecisionCombo.SelectedIndex;
        };

        ApplyDisplaySettingsToUi();

        // 学期配置绑定
        LoadSemesterConfig();
        StartDatePicker.SelectedDateChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null && StartDatePicker.SelectedDate.HasValue)
            {
                _config.StartDate = StartDatePicker.SelectedDate.Value.DateTime;
                InvalidateServiceCache();
            }
        };
        EndDatePicker.SelectedDateChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null && EndDatePicker.SelectedDate.HasValue)
            {
                _config.EndDate = EndDatePicker.SelectedDate.Value.DateTime;
                InvalidateServiceCache();
            }
        };
        SchoolStartTimePicker.SelectedTimeChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null && SchoolStartTimePicker.SelectedTime.HasValue)
            {
                _config.SchoolStartTime = SchoolStartTimePicker.SelectedTime.Value;
                UpdateTimeSummary();
            }
        };
        LunchStartTimePicker.SelectedTimeChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null && LunchStartTimePicker.SelectedTime.HasValue)
            {
                _config.LunchStartTime = LunchStartTimePicker.SelectedTime.Value;
                UpdateTimeSummary();
            }
        };
        LunchEndTimePicker.SelectedTimeChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null && LunchEndTimePicker.SelectedTime.HasValue)
            {
                _config.LunchEndTime = LunchEndTimePicker.SelectedTime.Value;
                UpdateTimeSummary();
            }
        };
        SchoolEndTimePicker.SelectedTimeChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null && SchoolEndTimePicker.SelectedTime.HasValue)
            {
                _config.SchoolEndTime = SchoolEndTimePicker.SelectedTime.Value;
                UpdateTimeSummary();
            }
        };
        ExcludeWeekendsCheck.IsCheckedChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null)
            {
                _config.ExcludeWeekends = ExcludeWeekendsCheck.IsChecked == true;
                InvalidateServiceCache();
            }
        };
        NetworkHolidayCheck.IsCheckedChanged += (_, _) =>
        {
            if (_isLoadingUi) return;
            if (_config != null)
            {
                _config.EnableNetworkHolidayUpdate = NetworkHolidayCheck.IsChecked == true;
                InvalidateServiceCache();
                UpdateHolidayStatus();
            }
        };

        AddHolidayBtn.Click += (_, _) => AddHoliday();
        RemoveHolidayBtn.Click += (_, _) => RemoveHoliday();
        AddWorkdayBtn.Click += (_, _) => AddWorkday();
        RemoveWorkdayBtn.Click += (_, _) => RemoveWorkday();
        AddTemplateBtn.Click += (_, _) => AddScheduleTemplate();
        RemoveTemplateBtn.Click += (_, _) => RemoveScheduleTemplate();
        AddDateRuleBtn.Click += (_, _) => AddDateScheduleRule();
        AddWeekdayRuleBtn.Click += (_, _) => AddWeekdayScheduleRule();
        RemoveRuleBtn.Click += (_, _) => RemoveScheduleRule();
        AddManualExclusionBtn.Click += (_, _) => AddManualExclusion();
        RemoveManualExclusionBtn.Click += (_, _) => RemoveManualExclusion();
        UpdateHolidayStatus();
        _holidayStatusTimer.Start();
    }

    private void ApplyDisplaySettingsToUi()
    {
        RunWithUiLoading(() =>
        {
            ShowProgressBarCheck.IsChecked = Settings.ShowProgressBar;
            ShowHoursCheck.IsChecked = Settings.ShowHours;
            CompactModeCheck.IsChecked = Settings.CompactMode;
            var precisionIndex = (int)Settings.RemainingTimePrecision;
            RemainingTimePrecisionCombo.SelectedIndex = precisionIndex is >= 0 and <= 3
                ? precisionIndex
                : (int)RemainingTimeDisplayPrecision.Seconds;
        });
    }

    private void LoadSemesterConfig()
    {
        if (_config == null) return;
        RunWithUiLoading(() =>
        {
            StartDatePicker.SelectedDate = new DateTimeOffset(_config.StartDate);
            EndDatePicker.SelectedDate = new DateTimeOffset(_config.EndDate);
            SchoolStartTimePicker.SelectedTime = _config.SchoolStartTime;
            LunchStartTimePicker.SelectedTime = _config.LunchStartTime;
            LunchEndTimePicker.SelectedTime = _config.LunchEndTime;
            SchoolEndTimePicker.SelectedTime = _config.SchoolEndTime;
            ExcludeWeekendsCheck.IsChecked = _config.ExcludeWeekends;
            NetworkHolidayCheck.IsChecked = _config.EnableNetworkHolidayUpdate;
        });
        UpdateTimeSummary();

        _holidays.Clear();
        foreach (var h in _config.CustomHolidays)
            _holidays.Add(new HolidayDisplayItem { Name = h.Name, StartDate = h.StartDate, EndDate = h.EndDate, Category = h.Category });

        _workdays.Clear();
        foreach (var w in _config.CustomWorkdays)
            _workdays.Add(new HolidayDisplayItem { Name = w.Name, StartDate = w.StartDate, EndDate = w.EndDate, Category = w.Category });

        ApplyDefaultDateInputs();
        RefreshScheduleTemplateList();
        RefreshScheduleRuleList();
        RefreshManualExclusionList();
        ApplyDefaultScheduleInputs();
        UpdateHolidayStatus();
        UpdateConfigurationValidation();
    }

    private void ApplyDefaultDateInputs()
    {
        var today = new DateTimeOffset(DateTime.Now.Date);
        RunWithUiLoading(() =>
        {
            HolidayStartPicker.SelectedDate ??= today;
            HolidayEndPicker.SelectedDate ??= today;
            WorkdayDatePicker.SelectedDate ??= today;
        });
    }

    private void ApplyDefaultScheduleInputs()
    {
        if (_config == null) return;
        var template = _config.ScheduleTemplates.FirstOrDefault() ?? ScheduleTemplate.CreateDefault();
        RunWithUiLoading(() =>
        {
            TemplateSchoolStartPicker.SelectedTime = template.SchoolStartTime;
            TemplateLunchStartPicker.SelectedTime = template.LunchStartTime;
            TemplateLunchEndPicker.SelectedTime = template.LunchEndTime;
            TemplateSchoolEndPicker.SelectedTime = template.SchoolEndTime;
            RuleDatePicker.SelectedDate ??= new DateTimeOffset(DateTime.Now);
            ManualExclusionDatePicker.SelectedDate ??= new DateTimeOffset(DateTime.Now);
            ManualExclusionStartPicker.SelectedTime ??= template.LunchStartTime;
            ManualExclusionEndPicker.SelectedTime ??= template.LunchEndTime;
        });
        if (RuleTemplateCombo.SelectedIndex < 0 && _templates.Count > 0)
            RuleTemplateCombo.SelectedIndex = 0;
    }

    private void RunWithUiLoading(Action action)
    {
        var wasLoading = _isLoadingUi;
        _isLoadingUi = true;
        try
        {
            action();
        }
        finally
        {
            _isLoadingUi = wasLoading;
        }
    }

    private void RefreshScheduleTemplateList()
    {
        if (_config == null) return;
        _templates.Clear();
        foreach (var template in _config.ScheduleTemplates)
            _templates.Add(new ScheduleTemplateDisplayItem(template));
        if ((RuleTemplateCombo.SelectedIndex < 0 || RuleTemplateCombo.SelectedIndex >= _templates.Count)
            && _templates.Count > 0)
        {
            RuleTemplateCombo.SelectedIndex = 0;
        }
    }

    private void RefreshScheduleRuleList()
    {
        if (_config == null) return;
        _scheduleRules.Clear();
        foreach (var rule in _config.ScheduleTemplateRules)
        {
            var template = _config.ScheduleTemplates.FirstOrDefault(t => t.Id == rule.TemplateId);
            _scheduleRules.Add(new ScheduleRuleDisplayItem(rule, template));
        }
    }

    private void RefreshManualExclusionList()
    {
        if (_config == null) return;
        _manualExclusions.Clear();
        foreach (var exclusion in _config.ManualTimeExclusions)
            _manualExclusions.Add(new ManualExclusionDisplayItem(exclusion));
    }

    private void AddHoliday()
    {
        if (_config == null) return;
        var name = string.IsNullOrWhiteSpace(HolidayNameInput.Text)
            ? "自定义假期"
            : HolidayNameInput.Text!;
        var start = (HolidayStartPicker.SelectedDate?.DateTime ?? DateTime.Now).Date;
        var end = (HolidayEndPicker.SelectedDate?.DateTime ?? start).Date;
        _config.CustomHolidays.Add(new HolidayInfo { Name = name, StartDate = start, EndDate = end, Category = HolidayCategory.Custom });
        _holidays.Add(new HolidayDisplayItem { Name = name, StartDate = start, EndDate = end, Category = HolidayCategory.Custom });
        HolidayNameInput.Text = "";
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomHolidays));
    }

    private void RemoveHoliday()
    {
        if (_config == null || HolidayListBox.SelectedIndex < 0) return;
        var idx = HolidayListBox.SelectedIndex;
        if (idx >= _config.CustomHolidays.Count || idx >= _holidays.Count)
            return;

        _config.CustomHolidays.RemoveAt(idx);
        _holidays.RemoveAt(idx);
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomHolidays));
    }

    private void AddWorkday()
    {
        if (_config == null) return;
        var name = string.IsNullOrWhiteSpace(WorkdayNameInput.Text)
            ? "调休补班"
            : WorkdayNameInput.Text!;
        var date = (WorkdayDatePicker.SelectedDate?.DateTime ?? DateTime.Now).Date;
        _config.CustomWorkdays.Add(new HolidayInfo { Name = name, StartDate = date, EndDate = date, Category = HolidayCategory.MakeUpWorkday });
        _workdays.Add(new HolidayDisplayItem { Name = name, StartDate = date, EndDate = date, Category = HolidayCategory.MakeUpWorkday });
        WorkdayNameInput.Text = "";
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomWorkdays));
    }

    private void RemoveWorkday()
    {
        if (_config == null || WorkdayListBox.SelectedIndex < 0) return;
        var idx = WorkdayListBox.SelectedIndex;
        if (idx >= _config.CustomWorkdays.Count || idx >= _workdays.Count)
            return;

        _config.CustomWorkdays.RemoveAt(idx);
        _workdays.RemoveAt(idx);
        NotifyConfigChanged(nameof(SemesterConfiguration.CustomWorkdays));
    }

    private void AddScheduleTemplate()
    {
        if (_config == null) return;
        var template = new ScheduleTemplate
        {
            Name = string.IsNullOrWhiteSpace(TemplateNameInput.Text)
                ? "新作息模板"
                : TemplateNameInput.Text!,
            SchoolStartTime = TemplateSchoolStartPicker.SelectedTime ?? _config.SchoolStartTime,
            LunchStartTime = TemplateLunchStartPicker.SelectedTime ?? _config.LunchStartTime,
            LunchEndTime = TemplateLunchEndPicker.SelectedTime ?? _config.LunchEndTime,
            SchoolEndTime = TemplateSchoolEndPicker.SelectedTime ?? _config.SchoolEndTime
        };

        _config.ScheduleTemplates.Add(template);
        TemplateNameInput.Text = "";
        NotifyConfigChanged(nameof(SemesterConfiguration.ScheduleTemplates));
        RefreshScheduleTemplateList();
    }

    private void RemoveScheduleTemplate()
    {
        if (_config == null || ScheduleTemplateListBox.SelectedIndex <= 0)
            return;

        var idx = ScheduleTemplateListBox.SelectedIndex;
        if (idx >= _config.ScheduleTemplates.Count)
            return;

        var templateId = _config.ScheduleTemplates[idx].Id;
        _config.ScheduleTemplates.RemoveAt(idx);
        _config.ScheduleTemplateRules.RemoveAll(r => r.TemplateId == templateId);
        NotifyConfigChanged(nameof(SemesterConfiguration.ScheduleTemplates));
        RefreshScheduleTemplateList();
        RefreshScheduleRuleList();
    }

    private void AddDateScheduleRule()
    {
        if (_config == null || RuleTemplateCombo.SelectedItem is not ScheduleTemplateDisplayItem template)
            return;

        var date = RuleDatePicker.SelectedDate?.DateTime.Date ?? DateTime.Now.Date;
        _config.ScheduleTemplateRules.RemoveAll(r => r.Date.HasValue && r.Date.Value.Date == date);
        _config.ScheduleTemplateRules.Add(new ScheduleTemplateRule
        {
            TemplateId = template.Template.Id,
            Date = date
        });
        NotifyConfigChanged(nameof(SemesterConfiguration.ScheduleTemplateRules));
        RefreshScheduleRuleList();
    }

    private void AddWeekdayScheduleRule()
    {
        if (_config == null || RuleTemplateCombo.SelectedItem is not ScheduleTemplateDisplayItem template)
            return;

        var dayOfWeek = GetSelectedDayOfWeek();
        _config.ScheduleTemplateRules.RemoveAll(r => !r.Date.HasValue && r.DayOfWeek == dayOfWeek);
        _config.ScheduleTemplateRules.Add(new ScheduleTemplateRule
        {
            TemplateId = template.Template.Id,
            DayOfWeek = dayOfWeek
        });
        NotifyConfigChanged(nameof(SemesterConfiguration.ScheduleTemplateRules));
        RefreshScheduleRuleList();
    }

    private void RemoveScheduleRule()
    {
        if (_config == null || ScheduleRuleListBox.SelectedIndex < 0)
            return;

        var idx = ScheduleRuleListBox.SelectedIndex;
        if (idx >= _config.ScheduleTemplateRules.Count)
            return;

        _config.ScheduleTemplateRules.RemoveAt(idx);
        NotifyConfigChanged(nameof(SemesterConfiguration.ScheduleTemplateRules));
        RefreshScheduleRuleList();
    }

    private void AddManualExclusion()
    {
        if (_config == null) return;
        var startTime = ManualExclusionStartPicker.SelectedTime ?? _config.LunchStartTime;
        var endTime = ManualExclusionEndPicker.SelectedTime ?? _config.LunchEndTime;
        if (endTime <= startTime)
        {
            ShowConfigurationMessage("错误：手动排除的结束时间必须晚于开始时间。");
            return;
        }

        var exclusion = new ManualTimeExclusion
        {
            Name = string.IsNullOrWhiteSpace(ManualExclusionNameInput.Text)
                ? "手动排除"
                : ManualExclusionNameInput.Text!,
            Date = ManualExclusionDatePicker.SelectedDate?.DateTime.Date ?? DateTime.Now.Date,
            StartTime = startTime,
            EndTime = endTime
        };

        _config.ManualTimeExclusions.Add(exclusion);
        ManualExclusionNameInput.Text = "";
        NotifyConfigChanged(nameof(SemesterConfiguration.ManualTimeExclusions));
        RefreshManualExclusionList();
    }

    private void RemoveManualExclusion()
    {
        if (_config == null || ManualExclusionListBox.SelectedIndex < 0)
            return;

        var idx = ManualExclusionListBox.SelectedIndex;
        if (idx >= _config.ManualTimeExclusions.Count)
            return;

        _config.ManualTimeExclusions.RemoveAt(idx);
        NotifyConfigChanged(nameof(SemesterConfiguration.ManualTimeExclusions));
        RefreshManualExclusionList();
    }

    private DayOfWeek GetSelectedDayOfWeek()
    {
        return RuleDayOfWeekCombo.SelectedIndex switch
        {
            1 => DayOfWeek.Tuesday,
            2 => DayOfWeek.Wednesday,
            3 => DayOfWeek.Thursday,
            4 => DayOfWeek.Friday,
            5 => DayOfWeek.Saturday,
            6 => DayOfWeek.Sunday,
            _ => DayOfWeek.Monday
        };
    }

    /// <summary>
    /// 触发 SemesterConfiguration.PropertyChanged，通知 StatisticsService 刷新缓存
    /// 并通知 StatsComponent 刷新显示
    /// </summary>
    private void NotifyConfigChanged(string propertyName)
    {
        if (_config == null) return;
        // 通过属性 setter 触发 PropertyChanged（List mutation 不自动触发）
        if (propertyName == nameof(SemesterConfiguration.CustomHolidays))
            _config.CustomHolidays = _config.CustomHolidays;
        else if (propertyName == nameof(SemesterConfiguration.CustomWorkdays))
            _config.CustomWorkdays = _config.CustomWorkdays;
        else if (propertyName == nameof(SemesterConfiguration.ScheduleTemplates))
            _config.ScheduleTemplates = _config.ScheduleTemplates;
        else if (propertyName == nameof(SemesterConfiguration.ScheduleTemplateRules))
            _config.ScheduleTemplateRules = _config.ScheduleTemplateRules;
        else if (propertyName == nameof(SemesterConfiguration.ManualTimeExclusions))
            _config.ManualTimeExclusions = _config.ManualTimeExclusions;
        else
            InvalidateServiceCache();

        UpdateTimeSummary();
        UpdateConfigurationValidation();
    }

    private void InvalidateServiceCache()
    {
        _statsService?.InvalidateCache();
    }

    private void UpdateTimeSummary()
    {
        if (_config == null) return;

        DailyHoursPreviewText.Text = $"当前每日在校时长：{_config.DailyHours:F1} 小时";
        var warning = GetTimeValidationWarning();
        TimeValidationText.Text = warning;
        TimeValidationText.IsVisible = !string.IsNullOrWhiteSpace(warning);
        RefreshScheduleTemplateList();
        UpdateConfigurationValidation();
    }

    private string GetTimeValidationWarning()
    {
        if (_config == null) return "";

        var warnings = new List<string>();
        if (_config.SchoolEndTime <= _config.SchoolStartTime)
            warnings.Add("放学时间必须晚于上课时间。");
        if (_config.LunchEndTime <= _config.LunchStartTime)
            warnings.Add("午休结束时间应晚于午休开始时间。");
        if (_config.LunchStartTime < _config.SchoolStartTime || _config.LunchEndTime > _config.SchoolEndTime)
            warnings.Add("午休时间建议位于上课时间和放学时间之间。");

        return string.Join("\n", warnings);
    }

    private void UpdateHolidayStatus()
    {
        var status = _holidayProvider?.Status.DisplayText ?? "节假日数据：状态不可用";
        if (!string.IsNullOrWhiteSpace(_configurationStore?.LastError))
            status += $"\n配置：{_configurationStore.LastError}";
        HolidayDataStatusText.Text = status;
    }

    private void UpdateConfigurationValidation()
    {
        if (_config == null) return;
        var issues = _config.ValidateConfiguration();
        ConfigurationValidationText.Text = FormatConfigurationIssues(issues);
        ConfigurationValidationText.IsVisible = issues.Count > 0;
    }

    private void ShowConfigurationMessage(string message)
    {
        ConfigurationValidationText.Text = message;
        ConfigurationValidationText.IsVisible = true;
    }

    private static string FormatConfigurationIssues(IEnumerable<ConfigurationIssue> issues)
    {
        return string.Join(
            "\n",
            issues.Select(i => i.Severity == ConfigurationIssueSeverity.Error
                ? $"错误：{i.Message}"
                : $"提示：{i.Message}"));
    }
}

public class HolidayDisplayItem
{
    public string Name { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public HolidayCategory Category { get; set; }
    public string DisplayText => EndDate == StartDate
        ? $"{Name}（{StartDate:yyyy-MM-dd}）"
        : $"{Name}（{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}）";
    public override string ToString() => DisplayText;
}

public class ScheduleTemplateDisplayItem
{
    public ScheduleTemplate Template { get; }

    public ScheduleTemplateDisplayItem(ScheduleTemplate template)
    {
        Template = template;
    }

    public string DisplayText =>
        $"{Template.DisplayName}（{Template.SchoolStartTime:hh\\:mm}-{Template.SchoolEndTime:hh\\:mm}，"
        + $"午休 {Template.LunchStartTime:hh\\:mm}-{Template.LunchEndTime:hh\\:mm}，"
        + $"{Template.DailyHours:F1} 小时）";

    public override string ToString() => DisplayText;
}

public class ScheduleRuleDisplayItem
{
    private readonly ScheduleTemplateRule _rule;
    private readonly ScheduleTemplate? _template;

    public ScheduleRuleDisplayItem(ScheduleTemplateRule rule, ScheduleTemplate? template)
    {
        _rule = rule;
        _template = template;
    }

    public string DisplayText
    {
        get
        {
            var target = _rule.Date.HasValue
                ? _rule.Date.Value.ToString("yyyy-MM-dd")
                : GetDayOfWeekText(_rule.DayOfWeek);
            var templateName = _template?.DisplayName ?? "已删除模板";
            return $"{target} 使用 {templateName}";
        }
    }

    public override string ToString() => DisplayText;

    private static string GetDayOfWeekText(DayOfWeek? dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            DayOfWeek.Sunday => "星期日",
            _ => "未选择"
        };
    }
}

public class ManualExclusionDisplayItem
{
    private readonly ManualTimeExclusion _exclusion;

    public ManualExclusionDisplayItem(ManualTimeExclusion exclusion)
    {
        _exclusion = exclusion;
    }

    public string DisplayText =>
        $"{_exclusion.DisplayName}（{_exclusion.Date:yyyy-MM-dd} "
        + $"{_exclusion.StartTime:hh\\:mm}-{_exclusion.EndTime:hh\\:mm}）";

    public override string ToString() => DisplayText;
}
