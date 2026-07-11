using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.SchoolStats.Models;

namespace ClassIsland.SchoolStats.Services;

public sealed class PluginConfigurationStore
{
    private const long MaximumConfigurationBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _saveLock = new();
    private bool _isSaving;

    public SemesterConfiguration Configuration { get; }
    public bool IsReadOnly { get; }
    public string? LastError { get; private set; }

    public PluginConfigurationStore(string path)
    {
        _path = path;
        var loadResult = Load(path);
        Configuration = loadResult.Configuration;
        IsReadOnly = loadResult.IsReadOnly;
        LastError = loadResult.Error;
        Configuration.PropertyChanged += OnConfigurationChanged;
    }

    public void Save()
    {
        if (IsReadOnly)
            return;

        var limitError = Configuration.GetStorageLimitError();
        if (limitError is not null)
        {
            LastError = $"配置未保存：{limitError}";
            return;
        }

        lock (_saveLock)
        {
            if (_isSaving)
                return;

            _isSaving = true;
            try
            {
                Configuration.SchemaVersion = SemesterConfiguration.CurrentSchemaVersion;
                var json = JsonSerializer.Serialize(Configuration, JsonOptions);
                if (Encoding.UTF8.GetByteCount(json) > MaximumConfigurationBytes)
                {
                    LastError = "配置未保存：序列化结果超过文件大小限制。";
                    return;
                }
                AtomicFile.WriteAllText(_path, json);
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LastError = $"保存配置失败：{ex.Message}";
                Trace.TraceError("[SchoolStats] {0}", LastError);
            }
            finally
            {
                _isSaving = false;
            }
        }
    }

    private void OnConfigurationChanged(object? sender, PropertyChangedEventArgs e) => Save();

    private static (SemesterConfiguration Configuration, bool IsReadOnly, string? Error) Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new SemesterConfiguration();
            defaults.NormalizeAfterLoad();
            return (defaults, false, null);
        }

        var futureSchemaVersion = 0;
        try
        {
            if (new FileInfo(path).Length > MaximumConfigurationBytes)
            {
                var safeConfiguration = new SemesterConfiguration();
                safeConfiguration.NormalizeAfterLoad();
                return (
                    safeConfiguration,
                    true,
                    "配置文件超过大小限制，已按只读安全模式加载且不会覆盖原文件。");
            }

            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("配置根节点必须是 JSON 对象。");

            if (TryGetProperty(document.RootElement, "schemaVersion", out var schemaElement))
            {
                if (schemaElement.ValueKind != JsonValueKind.Number
                    || !schemaElement.TryGetInt32(out futureSchemaVersion))
                {
                    throw new JsonException("schemaVersion 必须是整数。");
                }
            }

            if (futureSchemaVersion <= SemesterConfiguration.CurrentSchemaVersion)
                ValidateRawConfiguration(document.RootElement);

            var configuration = JsonSerializer.Deserialize<SemesterConfiguration>(json, JsonOptions)
                ?? new SemesterConfiguration();

            // In schema v1 both the four legacy time fields and ScheduleTemplates
            // could be present. Re-read templates from the raw JSON so migration is
            // independent of JSON property order.
            if (TryGetProperty(document.RootElement, nameof(SemesterConfiguration.ScheduleTemplates), out var templatesElement)
                && templatesElement.ValueKind == JsonValueKind.Array)
            {
                var serializedTemplates =
                    templatesElement.Deserialize<List<ScheduleTemplate>>(JsonOptions) ?? [];
                if (serializedTemplates.Count > 0)
                    configuration.ScheduleTemplates = serializedTemplates;
            }

            var sourceVersion = futureSchemaVersion > 0
                ? futureSchemaVersion
                : configuration.SchemaVersion;
            configuration.NormalizeAfterLoad();

            if (sourceVersion > SemesterConfiguration.CurrentSchemaVersion)
            {
                return (
                    configuration,
                    true,
                    $"配置版本 {sourceVersion} 高于当前支持的版本 "
                    + $"{SemesterConfiguration.CurrentSchemaVersion}，已按只读方式加载。");
            }

            return (configuration, false, null);
        }
        catch (ConfigurationLimitException ex)
        {
            var defaults = new SemesterConfiguration();
            defaults.NormalizeAfterLoad();
            return (
                defaults,
                true,
                $"配置超过安全限制，已按只读模式加载且不会覆盖原文件：{ex.Message}");
        }
        catch (JsonException ex)
        {
            if (futureSchemaVersion > SemesterConfiguration.CurrentSchemaVersion)
            {
                var safeConfiguration = new SemesterConfiguration();
                safeConfiguration.NormalizeAfterLoad();
                return (
                    safeConfiguration,
                    true,
                    $"配置版本 {futureSchemaVersion} 高于当前支持版本，且包含无法识别的数据；"
                    + "原文件已保留且不会被覆盖。");
            }

            var error = $"加载配置失败，已使用默认配置：{ex.Message}";
            TryBackupCorruptedConfiguration(path);
            Trace.TraceError("[SchoolStats] {0}", error);

            var defaults = new SemesterConfiguration();
            defaults.NormalizeAfterLoad();
            return (defaults, false, error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var error = $"暂时无法读取配置，已按只读安全模式运行：{ex.Message}";
            Trace.TraceError("[SchoolStats] {0}", error);
            var defaults = new SemesterConfiguration();
            defaults.NormalizeAfterLoad();
            return (defaults, true, error);
        }
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void ValidateRawConfiguration(JsonElement root)
    {
        ValidateArray(
            root,
            nameof(SemesterConfiguration.ScheduleTemplates),
            SemesterConfiguration.MaximumScheduleTemplates);
        ValidateArray(
            root,
            nameof(SemesterConfiguration.ScheduleTemplateRules),
            SemesterConfiguration.MaximumScheduleRules);
        ValidateArray(
            root,
            nameof(SemesterConfiguration.ManualTimeExclusions),
            SemesterConfiguration.MaximumManualExclusions);
        ValidateArray(
            root,
            nameof(SemesterConfiguration.CustomHolidays),
            SemesterConfiguration.MaximumCustomDateRanges);
        ValidateArray(
            root,
            nameof(SemesterConfiguration.CustomWorkdays),
            SemesterConfiguration.MaximumCustomDateRanges);
    }

    private static void ValidateArray(JsonElement root, string propertyName, int maximumCount)
    {
        if (!TryGetProperty(root, propertyName, out var arrayElement))
            return;
        if (arrayElement.ValueKind == JsonValueKind.Null)
            return;
        if (arrayElement.ValueKind != JsonValueKind.Array)
            throw new JsonException($"{propertyName} 必须是数组。");
        if (arrayElement.GetArrayLength() > maximumCount)
            throw new ConfigurationLimitException($"{propertyName} 超过 {maximumCount} 条限制。");

        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new JsonException($"{propertyName} 包含空项或非对象项。");
            if (TryGetProperty(item, "Name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && (nameElement.GetString()?.Length ?? 0) > SemesterConfiguration.MaximumNameLength)
            {
                throw new ConfigurationLimitException(
                    $"{propertyName} 中的名称超过 {SemesterConfiguration.MaximumNameLength} 个字符。");
            }
        }
    }

    private static void TryBackupCorruptedConfiguration(string path)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            File.Copy(path, $"{path}.corrupt-{timestamp}", false);

            var directory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
            var searchPattern = Path.GetFileName(path) + ".corrupt-*";
            foreach (var staleBackup in new DirectoryInfo(directory)
                         .EnumerateFiles(searchPattern)
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Skip(3))
            {
                staleBackup.Delete();
            }
        }
        catch
        {
            // Preserve startup even if the diagnostic backup cannot be created.
        }
    }

    private sealed class ConfigurationLimitException(string message) : Exception(message);
}
