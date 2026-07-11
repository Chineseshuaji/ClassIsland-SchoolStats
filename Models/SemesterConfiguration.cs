using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ClassIsland.SchoolStats.Models;

public class SemesterConfiguration : INotifyPropertyChanged
{
    public const int CurrentSchemaVersion = 2;
    public const int MaximumSemesterDays = 730;
    public const int MaximumScheduleTemplates = 128;
    public const int MaximumScheduleRules = 512;
    public const int MaximumManualExclusions = 2048;
    public const int MaximumCustomDateRanges = 512;
    public const int MaximumNameLength = 200;

    private int _schemaVersion = CurrentSchemaVersion;
    private DateTime _startDate = DateTime.Now;
    private DateTime _endDate = DateTime.Now.AddMonths(4);
    private TimeSpan _schoolStartTime = new(8, 0, 0);
    private TimeSpan _lunchStartTime = new(12, 0, 0);
    private TimeSpan _lunchEndTime = new(13, 0, 0);
    private TimeSpan _schoolEndTime = new(17, 0, 0);
    private bool _excludeWeekends = true;
    private bool _enableNetworkHolidayUpdate;
    private List<ScheduleTemplate> _scheduleTemplates =
    [
        ScheduleTemplate.CreateDefault()
    ];
    private List<ScheduleTemplateRule> _scheduleTemplateRules = [];
    private List<ManualTimeExclusion> _manualTimeExclusions = [];
    private List<HolidayInfo> _customHolidays = [];
    private List<HolidayInfo> _customWorkdays = [];

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion
    {
        get => _schemaVersion;
        set { if (value != _schemaVersion) { _schemaVersion = value; OnPropertyChanged(); } }
    }

    public DateTime StartDate
    {
        get => _startDate;
        set { if (value != _startDate) { _startDate = value; OnPropertyChanged(); } }
    }

    public DateTime EndDate
    {
        get => _endDate;
        set { if (value != _endDate) { _endDate = value; OnPropertyChanged(); } }
    }

    public TimeSpan SchoolStartTime
    {
        get => _schoolStartTime;
        set { if (value != _schoolStartTime) { _schoolStartTime = value; SyncDefaultScheduleFromLegacyTimes(); OnPropertyChanged(); OnPropertyChanged(nameof(DailyHours)); } }
    }

    public TimeSpan LunchStartTime
    {
        get => _lunchStartTime;
        set { if (value != _lunchStartTime) { _lunchStartTime = value; SyncDefaultScheduleFromLegacyTimes(); OnPropertyChanged(); OnPropertyChanged(nameof(DailyHours)); } }
    }

    public TimeSpan LunchEndTime
    {
        get => _lunchEndTime;
        set { if (value != _lunchEndTime) { _lunchEndTime = value; SyncDefaultScheduleFromLegacyTimes(); OnPropertyChanged(); OnPropertyChanged(nameof(DailyHours)); } }
    }

    public TimeSpan SchoolEndTime
    {
        get => _schoolEndTime;
        set { if (value != _schoolEndTime) { _schoolEndTime = value; SyncDefaultScheduleFromLegacyTimes(); OnPropertyChanged(); OnPropertyChanged(nameof(DailyHours)); } }
    }

    [JsonIgnore]
    public double DailyHours => Math.Round(CalculateDailyHours(), 2);

    public bool ExcludeWeekends
    {
        get => _excludeWeekends;
        set { if (value != _excludeWeekends) { _excludeWeekends = value; OnPropertyChanged(); } }
    }

    public bool EnableNetworkHolidayUpdate
    {
        get => _enableNetworkHolidayUpdate;
        set { if (value != _enableNetworkHolidayUpdate) { _enableNetworkHolidayUpdate = value; OnPropertyChanged(); } }
    }

    public List<ScheduleTemplate> ScheduleTemplates
    {
        get
        {
            EnsureDefaultScheduleTemplate();
            return _scheduleTemplates;
        }
        set { _scheduleTemplates = value ?? []; EnsureDefaultScheduleTemplate(); OnPropertyChanged(); OnPropertyChanged(nameof(DailyHours)); }
    }

    public List<ScheduleTemplateRule> ScheduleTemplateRules
    {
        get => _scheduleTemplateRules;
        set { _scheduleTemplateRules = value ?? []; OnPropertyChanged(); OnPropertyChanged(nameof(DailyHours)); }
    }

    public List<ManualTimeExclusion> ManualTimeExclusions
    {
        get => _manualTimeExclusions;
        set { _manualTimeExclusions = value ?? []; OnPropertyChanged(); }
    }

    public List<HolidayInfo> CustomHolidays
    {
        get => _customHolidays;
        set { _customHolidays = value ?? []; OnPropertyChanged(); }
    }

    public List<HolidayInfo> CustomWorkdays
    {
        get => _customWorkdays;
        set { _customWorkdays = value ?? []; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public int TotalCalendarDays => Math.Max(0, (EndDate.Date - StartDate.Date).Days + 1);

    [JsonIgnore]
    public long Revision { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScheduleTemplate GetScheduleTemplate(DateTime date)
    {
        EnsureDefaultScheduleTemplate();
        var rule = ScheduleTemplateRules
            .Select((value, index) => (value, index))
            .Where(x => x.value.AppliesTo(date)
                && ScheduleTemplates.Any(t => t.Id == x.value.TemplateId))
            .OrderByDescending(x => x.value.Date.HasValue)
            .ThenByDescending(x => x.index)
            .Select(x => x.value)
            .FirstOrDefault();

        return ScheduleTemplates.FirstOrDefault(t => t.Id == rule?.TemplateId)
            ?? ScheduleTemplates.First();
    }

    public double GetDailyHours(DateTime date) => Math.Round(GetScheduleTemplate(date).DailyHours, 2);

    public void NormalizeAfterLoad()
    {
        _startDate = NormalizeDate(_startDate);
        _endDate = NormalizeDate(_endDate);
        _scheduleTemplates = (_scheduleTemplates ?? [])
            .OfType<ScheduleTemplate>()
            .ToList();
        _scheduleTemplateRules = (_scheduleTemplateRules ?? [])
            .OfType<ScheduleTemplateRule>()
            .ToList();
        _manualTimeExclusions = (_manualTimeExclusions ?? [])
            .OfType<ManualTimeExclusion>()
            .ToList();
        _customHolidays = (_customHolidays ?? [])
            .OfType<HolidayInfo>()
            .ToList();
        _customWorkdays = (_customWorkdays ?? [])
            .OfType<HolidayInfo>()
            .ToList();
        EnsureDefaultScheduleTemplate();

        var usedTemplateIds = new HashSet<Guid>();
        foreach (var template in _scheduleTemplates)
        {
            template.Name = DefaultName(template.Name, "默认作息");
            if (template.Id == Guid.Empty || !usedTemplateIds.Add(template.Id))
                template.Id = Guid.NewGuid();
            usedTemplateIds.Add(template.Id);
        }

        foreach (var exclusion in _manualTimeExclusions)
        {
            exclusion.Name = DefaultName(exclusion.Name, "手动排除");
            exclusion.Date = NormalizeDate(exclusion.Date);
        }
        foreach (var holiday in _customHolidays)
        {
            holiday.Name = DefaultName(holiday.Name, "自定义假期");
            holiday.StartDate = NormalizeDate(holiday.StartDate);
            holiday.EndDate = NormalizeDate(holiday.EndDate);
        }
        foreach (var workday in _customWorkdays)
        {
            workday.Name = DefaultName(workday.Name, "调休补班");
            workday.StartDate = NormalizeDate(workday.StartDate);
            workday.EndDate = NormalizeDate(workday.EndDate);
        }
        foreach (var rule in _scheduleTemplateRules.Where(rule => rule.Date.HasValue))
            rule.Date = NormalizeDate(rule.Date!.Value);

        // Schedule templates are the canonical representation in schema v2. For a
        // legacy configuration the default template was populated by the legacy
        // setters during deserialization, so this synchronization is order-safe.
        var defaultTemplate = _scheduleTemplates[0];
        _schoolStartTime = defaultTemplate.SchoolStartTime;
        _lunchStartTime = defaultTemplate.LunchStartTime;
        _lunchEndTime = defaultTemplate.LunchEndTime;
        _schoolEndTime = defaultTemplate.SchoolEndTime;

        _schemaVersion = CurrentSchemaVersion;
    }

    private static string DefaultName(string? name, string fallback)
        => string.IsNullOrWhiteSpace(name) ? fallback : name;

    private static DateTime NormalizeDate(DateTime value)
        => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    public string? GetStorageLimitError()
    {
        if (ScheduleTemplates.OfType<ScheduleTemplate>().Count() != ScheduleTemplates.Count
            || ScheduleTemplateRules.OfType<ScheduleTemplateRule>().Count() != ScheduleTemplateRules.Count
            || ManualTimeExclusions.OfType<ManualTimeExclusion>().Count() != ManualTimeExclusions.Count
            || CustomHolidays.OfType<HolidayInfo>().Count() != CustomHolidays.Count
            || CustomWorkdays.OfType<HolidayInfo>().Count() != CustomWorkdays.Count)
        {
            return "配置集合不能包含空项。";
        }

        if (ScheduleTemplates.Count > MaximumScheduleTemplates)
            return $"作息模板不能超过 {MaximumScheduleTemplates} 个。";
        if (ScheduleTemplateRules.Count > MaximumScheduleRules)
            return $"作息规则不能超过 {MaximumScheduleRules} 条。";
        if (ManualTimeExclusions.Count > MaximumManualExclusions)
            return $"手动排除不能超过 {MaximumManualExclusions} 条。";
        if (CustomHolidays.Count > MaximumCustomDateRanges
            || CustomWorkdays.Count > MaximumCustomDateRanges)
        {
            return $"自定义假期或补班日不能超过 {MaximumCustomDateRanges} 条。";
        }

        var names = ScheduleTemplates.OfType<ScheduleTemplate>().Select(template => template.Name)
            .Concat(ManualTimeExclusions.OfType<ManualTimeExclusion>().Select(exclusion => exclusion.Name))
            .Concat(CustomHolidays.OfType<HolidayInfo>().Select(holiday => holiday.Name))
            .Concat(CustomWorkdays.OfType<HolidayInfo>().Select(workday => workday.Name));
        return names.Any(name => name?.Length > MaximumNameLength)
            ? $"名称不能超过 {MaximumNameLength} 个字符。"
            : null;
    }

    public IReadOnlyList<ConfigurationIssue> ValidateConfiguration()
    {
        var issues = new List<ConfigurationIssue>();
        if (EndDate.Date < StartDate.Date)
            issues.Add(new ConfigurationIssue("学期结束日期早于开始日期。", ConfigurationIssueSeverity.Error));
        else if (TotalCalendarDays > MaximumSemesterDays)
            issues.Add(new ConfigurationIssue(
                $"学期范围不能超过 {MaximumSemesterDays} 天。",
                ConfigurationIssueSeverity.Error));

        EnsureDefaultScheduleTemplate();
        if (ScheduleTemplates.Count == 0)
            issues.Add(new ConfigurationIssue("至少需要保留一个作息模板。", ConfigurationIssueSeverity.Error));

        foreach (var template in ScheduleTemplates)
        {
            if (string.IsNullOrWhiteSpace(template.Name))
                issues.Add(new ConfigurationIssue("存在未命名的作息模板。", ConfigurationIssueSeverity.Warning));
            if (template.SchoolEndTime <= template.SchoolStartTime)
                issues.Add(new ConfigurationIssue($"作息模板“{template.DisplayName}”的放学时间必须晚于上课时间。", ConfigurationIssueSeverity.Error));
            if (template.LunchEndTime <= template.LunchStartTime)
                issues.Add(new ConfigurationIssue($"作息模板“{template.DisplayName}”的午休结束时间应晚于午休开始时间。", ConfigurationIssueSeverity.Warning));
            if (template.LunchStartTime < template.SchoolStartTime || template.LunchEndTime > template.SchoolEndTime)
                issues.Add(new ConfigurationIssue($"作息模板“{template.DisplayName}”的午休时间建议位于上课时间和放学时间之间。", ConfigurationIssueSeverity.Warning));
            if (template.DailyHours <= 0)
                issues.Add(new ConfigurationIssue($"作息模板“{template.DisplayName}”的每日在校时长为 0。", ConfigurationIssueSeverity.Error));
        }

        foreach (var rule in ScheduleTemplateRules)
        {
            if (!ScheduleTemplates.Any(t => t.Id == rule.TemplateId))
                issues.Add(new ConfigurationIssue("存在指向已删除作息模板的规则。", ConfigurationIssueSeverity.Error));
            if (!rule.Date.HasValue && !rule.DayOfWeek.HasValue)
                issues.Add(new ConfigurationIssue("存在未选择日期或星期的作息规则。", ConfigurationIssueSeverity.Warning));
            if (rule.Date.HasValue && rule.DayOfWeek.HasValue)
                issues.Add(new ConfigurationIssue($"{rule.Date.Value:yyyy-MM-dd} 的作息规则同时设置了日期和星期，将按指定日期处理。", ConfigurationIssueSeverity.Warning));
        }

        foreach (var exclusion in ManualTimeExclusions)
        {
            if (string.IsNullOrWhiteSpace(exclusion.Name))
                issues.Add(new ConfigurationIssue("存在未命名的手动排除时间段。", ConfigurationIssueSeverity.Warning));
            if (exclusion.EndTime <= exclusion.StartTime)
                issues.Add(new ConfigurationIssue($"手动排除“{exclusion.DisplayName}”的结束时间必须晚于开始时间。", ConfigurationIssueSeverity.Error));
            if (exclusion.Date.Date < StartDate.Date || exclusion.Date.Date > EndDate.Date)
                issues.Add(new ConfigurationIssue($"手动排除“{exclusion.DisplayName}”不在当前学期范围内。", ConfigurationIssueSeverity.Warning));
            if (!IsTimeRangeInsideTemplate(exclusion.Date, exclusion.StartTime, exclusion.EndTime))
                issues.Add(new ConfigurationIssue($"手动排除“{exclusion.DisplayName}”不在当天作息时间内。", ConfigurationIssueSeverity.Warning));
        }

        ValidateHolidayRanges(CustomHolidays, "假期", issues);
        ValidateHolidayRanges(CustomWorkdays, "补班日", issues);
        ValidateHolidayOverlaps(CustomHolidays, "假期", issues);
        ValidateHolidayOverlaps(CustomWorkdays, "补班日", issues);
        ValidateManualExclusionOverlaps(issues);
        ValidateScheduleRuleDuplicates(issues);
        return issues;
    }

    private double CalculateDailyHours()
    {
        return ScheduleTemplates.Count > 0
            ? ScheduleTemplates.First().DailyHours
            : CalculateDailyHoursForTemplate(SchoolStartTime, LunchStartTime, LunchEndTime, SchoolEndTime);
    }

    internal static double CalculateDailyHoursForTemplate(
        TimeSpan schoolStart,
        TimeSpan lunchStartTime,
        TimeSpan lunchEndTime,
        TimeSpan schoolEnd)
    {
        var total = schoolEnd - schoolStart;
        if (total <= TimeSpan.Zero)
            return 0;

        var lunchStart = Max(schoolStart, lunchStartTime);
        var lunchEnd = Min(schoolEnd, lunchEndTime);
        var lunch = lunchEnd > lunchStart ? lunchEnd - lunchStart : TimeSpan.Zero;

        return Math.Max(0, (total - lunch).TotalHours);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private void EnsureDefaultScheduleTemplate()
    {
        if (_scheduleTemplates.Count == 0)
        {
            _scheduleTemplates.Add(CreateScheduleTemplateFromLegacyTimes());
        }
    }

    private void SyncDefaultScheduleFromLegacyTimes()
    {
        EnsureDefaultScheduleTemplate();
        var first = _scheduleTemplates[0];
        first.SchoolStartTime = SchoolStartTime;
        first.LunchStartTime = LunchStartTime;
        first.LunchEndTime = LunchEndTime;
        first.SchoolEndTime = SchoolEndTime;
    }

    private ScheduleTemplate CreateScheduleTemplateFromLegacyTimes()
    {
        return new ScheduleTemplate
        {
            SchoolStartTime = SchoolStartTime,
            LunchStartTime = LunchStartTime,
            LunchEndTime = LunchEndTime,
            SchoolEndTime = SchoolEndTime
        };
    }

    private static void ValidateHolidayRanges(
        IEnumerable<HolidayInfo> holidays,
        string label,
        ICollection<ConfigurationIssue> issues)
    {
        foreach (var holiday in holidays)
        {
            if (holiday.EndDate.Date < holiday.StartDate.Date)
                issues.Add(new ConfigurationIssue($"{label}“{holiday.Name}”的结束日期早于开始日期。", ConfigurationIssueSeverity.Error));
        }
    }

    private bool IsTimeRangeInsideTemplate(DateTime date, TimeSpan start, TimeSpan end)
    {
        var template = GetScheduleTemplate(date);
        return start >= template.SchoolStartTime && end <= template.SchoolEndTime;
    }

    private static void ValidateHolidayOverlaps(
        IReadOnlyList<HolidayInfo> holidays,
        string label,
        ICollection<ConfigurationIssue> issues)
    {
        for (var i = 0; i < holidays.Count; i++)
        {
            for (var j = i + 1; j < holidays.Count; j++)
            {
                var left = holidays[i];
                var right = holidays[j];
                if (left.StartDate.Date <= right.EndDate.Date && right.StartDate.Date <= left.EndDate.Date)
                    issues.Add(new ConfigurationIssue($"{label}“{left.Name}”与“{right.Name}”日期区间重叠。", ConfigurationIssueSeverity.Warning));
            }
        }
    }

    private void ValidateManualExclusionOverlaps(ICollection<ConfigurationIssue> issues)
    {
        var groups = ManualTimeExclusions.GroupBy(x => x.Date.Date);
        foreach (var group in groups)
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    var left = list[i];
                    var right = list[j];
                    if (left.StartTime < right.EndTime && right.StartTime < left.EndTime)
                    {
                        issues.Add(new ConfigurationIssue(
                            $"{group.Key:yyyy-MM-dd} 的手动排除“{left.DisplayName}”与“{right.DisplayName}”时间重叠。",
                            ConfigurationIssueSeverity.Warning));
                    }
                }
            }
        }
    }

    private void ValidateScheduleRuleDuplicates(ICollection<ConfigurationIssue> issues)
    {
        var duplicateDates = ScheduleTemplateRules
            .Where(r => r.Date.HasValue)
            .GroupBy(r => r.Date!.Value.Date)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var date in duplicateDates)
            issues.Add(new ConfigurationIssue($"{date:yyyy-MM-dd} 存在多条作息规则，最终只会使用其中一条。", ConfigurationIssueSeverity.Warning));

        var duplicateWeekdays = ScheduleTemplateRules
            .Where(r => !r.Date.HasValue && r.DayOfWeek.HasValue)
            .GroupBy(r => r.DayOfWeek!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var dayOfWeek in duplicateWeekdays)
            issues.Add(new ConfigurationIssue($"{GetDayOfWeekText(dayOfWeek)} 存在多条作息规则，最终只会使用其中一条。", ConfigurationIssueSeverity.Warning));
    }

    private static string GetDayOfWeekText(DayOfWeek dayOfWeek)
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
            _ => dayOfWeek.ToString()
        };
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        Revision++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class ScheduleTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "默认作息";
    public TimeSpan SchoolStartTime { get; set; } = new(8, 0, 0);
    public TimeSpan LunchStartTime { get; set; } = new(12, 0, 0);
    public TimeSpan LunchEndTime { get; set; } = new(13, 0, 0);
    public TimeSpan SchoolEndTime { get; set; } = new(17, 0, 0);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名作息" : Name;

    [JsonIgnore]
    public double DailyHours => Math.Round(SemesterConfiguration.CalculateDailyHoursForTemplate(
        SchoolStartTime,
        LunchStartTime,
        LunchEndTime,
        SchoolEndTime), 2);

    public static ScheduleTemplate CreateDefault() => new();
}

public class ScheduleTemplateRule
{
    public Guid TemplateId { get; set; }
    public DateTime? Date { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }

    public bool AppliesTo(DateTime date)
    {
        date = date.Date;
        if (Date.HasValue)
            return Date.Value.Date == date;
        return DayOfWeek.HasValue && DayOfWeek.Value == date.DayOfWeek;
    }
}

public class ManualTimeExclusion
{
    public string Name { get; set; } = "手动排除";
    public DateTime Date { get; set; } = DateTime.Now.Date;
    public TimeSpan StartTime { get; set; } = new(12, 0, 0);
    public TimeSpan EndTime { get; set; } = new(13, 0, 0);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名排除" : Name;
}

public enum ConfigurationIssueSeverity
{
    Warning,
    Error
}

public record ConfigurationIssue(string Message, ConfigurationIssueSeverity Severity);
