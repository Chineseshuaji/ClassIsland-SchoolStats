namespace ClassIsland.SchoolStats.Services;

public interface IHolidayProvider
{
    HolidayDataStatus Status { get; }
    Task<IReadOnlyList<Models.HolidayInfo>> GetHolidaysAsync(
        int year,
        CancellationToken cancellationToken = default);
    void InvalidateCache(int year);
}

public class HolidayDataStatus
{
    private readonly object _syncRoot = new();
    private string _source = "未知";
    private string _message = "尚未加载节假日数据";
    private bool _isAvailable;
    private DateTime? _lastUpdatedAt;

    public string Source { get { lock (_syncRoot) return _source; } set { lock (_syncRoot) _source = value; } }
    public string Message { get { lock (_syncRoot) return _message; } set { lock (_syncRoot) _message = value; } }
    public bool IsAvailable { get { lock (_syncRoot) return _isAvailable; } set { lock (_syncRoot) _isAvailable = value; } }
    public DateTime? LastUpdatedAt { get { lock (_syncRoot) return _lastUpdatedAt; } set { lock (_syncRoot) _lastUpdatedAt = value; } }

    public string DisplayText
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastUpdatedAt.HasValue
                    ? $"{_source}：{_message}（{_lastUpdatedAt:HH:mm:ss}）"
                    : $"{_source}：{_message}";
            }
        }
    }

    public void Update(string source, string message, bool isAvailable)
    {
        lock (_syncRoot)
        {
            _source = source;
            _message = message;
            _isAvailable = isAvailable;
            _lastUpdatedAt = DateTime.Now;
        }
    }
}
