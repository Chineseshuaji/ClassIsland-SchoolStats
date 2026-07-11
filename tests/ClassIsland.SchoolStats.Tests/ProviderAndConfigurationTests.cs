using System.Net;
using System.Text;
using ClassIsland.SchoolStats.Models;
using ClassIsland.SchoolStats.Services;

namespace ClassIsland.SchoolStats.Tests;

public sealed class ProviderAndConfigurationTests
{
    [Fact]
    public async Task LocalProviderReloadsYearAfterInvalidation()
    {
        using var directory = new TemporaryDirectory();
        var assetsDirectory = System.IO.Path.Combine(directory.Path, "Assets");
        Directory.CreateDirectory(assetsDirectory);
        var dataPath = System.IO.Path.Combine(assetsDirectory, "holidays.json");
        await File.WriteAllTextAsync(dataPath, HolidayJson("第一版"));
        var provider = new LocalHolidayProvider(directory.Path);

        Assert.Equal("第一版", Assert.Single(await provider.GetHolidaysAsync(2026)).Name);
        await File.WriteAllTextAsync(dataPath, HolidayJson("第二版"));
        provider.InvalidateCache(2026);

        Assert.Equal("第二版", Assert.Single(await provider.GetHolidaysAsync(2026)).Name);
    }

    [Fact]
    public async Task FallbackProviderSwitchesNetworkModeWithoutRestart()
    {
        var configuration = new SemesterConfiguration { EnableNetworkHolidayUpdate = false };
        var network = new StubHolidayProvider(Holiday("网络", HolidayCategory.LegalHoliday));
        var local = new StubHolidayProvider(Holiday("本地", HolidayCategory.LegalHoliday));
        var provider = new FallbackHolidayProvider(network, local, configuration);

        Assert.Equal("本地", Assert.Single(await provider.GetHolidaysAsync(2026)).Name);
        Assert.Equal(0, network.CallCount);

        configuration.EnableNetworkHolidayUpdate = true;
        Assert.Equal("网络", Assert.Single(await provider.GetHolidaysAsync(2026)).Name);
        Assert.Equal(1, network.CallCount);
    }

    [Fact]
    public async Task NetworkProviderUsesPersistedLastKnownDataOnFailure()
    {
        using var directory = new TemporaryDirectory();
        using (var onlineClient = new HttpClient(new StubHttpHandler(_ =>
                   new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StringContent(ApiResponse, Encoding.UTF8, "application/json")
                   })))
        using (var onlineProvider = new NetworkHolidayProvider(directory.Path, httpClient: onlineClient))
        {
            Assert.Single(await onlineProvider.GetHolidaysAsync(2026));
        }

        using var offlineClient = new HttpClient(new StubHttpHandler(_ => throw new HttpRequestException("offline")));
        using var offlineProvider = new NetworkHolidayProvider(directory.Path, httpClient: offlineClient);

        var cached = await offlineProvider.GetHolidaysAsync(2026);

        Assert.Equal("元旦", Assert.Single(cached).Name);
        Assert.Equal("节假日缓存", offlineProvider.Status.Source);
    }

    [Fact]
    public async Task NetworkProviderRejectsOversizedResponses()
    {
        using var directory = new TemporaryDirectory();
        using var client = new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[1024 * 1024 + 1])
            }));
        using var provider = new NetworkHolidayProvider(directory.Path, httpClient: client);

        var result = await provider.GetHolidaysAsync(2026);

        Assert.Empty(result);
        Assert.False(provider.Status.IsAvailable);
        Assert.Contains("大小限制", provider.Status.Message);
    }

    [Fact]
    public async Task PartiallyMalformedNetworkResponseUsesLastKnownGoodCache()
    {
        using var directory = new TemporaryDirectory();
        using (var validClient = new HttpClient(new StubHttpHandler(_ =>
                   new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StringContent(ApiResponse, Encoding.UTF8, "application/json")
                   })))
        using (var validProvider = new NetworkHolidayProvider(directory.Path, httpClient: validClient))
        {
            Assert.Single(await validProvider.GetHolidaysAsync(2026));
        }

        var cachePath = System.IO.Path.Combine(
            directory.Path,
            "holiday-cache",
            "holidays-2026.json");
        var originalCache = await File.ReadAllTextAsync(cachePath);
        using var malformedClient = new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(PartiallyMalformedApiResponse, Encoding.UTF8, "application/json")
            }));
        using var malformedProvider = new NetworkHolidayProvider(
            directory.Path,
            httpClient: malformedClient);

        var result = await malformedProvider.GetHolidaysAsync(2026);

        Assert.Equal("元旦", Assert.Single(result).Name);
        Assert.Equal(originalCache, await File.ReadAllTextAsync(cachePath));
        Assert.Equal("节假日缓存", malformedProvider.Status.Source);
    }

    [Fact]
    public async Task ConflictingPersistentRecordsAreRejected()
    {
        using var directory = new TemporaryDirectory();
        var cacheDirectory = System.IO.Path.Combine(directory.Path, "holiday-cache");
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(cacheDirectory, "holidays-2026.json"),
            """
            [
              {"Name":"放假","StartDate":"2026-01-01","EndDate":"2026-01-01","Category":"LegalHoliday"},
              {"Name":"补班","StartDate":"2026-01-01","EndDate":"2026-01-01","Category":"MakeUpWorkday"}
            ]
            """);
        using var offlineClient = new HttpClient(new StubHttpHandler(_ =>
            throw new HttpRequestException("offline")));
        using var provider = new NetworkHolidayProvider(directory.Path, httpClient: offlineClient);

        Assert.Empty(await provider.GetHolidaysAsync(2026));
    }

    [Fact]
    public void ConfigurationMigrationIsIndependentOfJsonPropertyOrder()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        var templateId = Guid.NewGuid();
        File.WriteAllText(path, $$"""
        {
          "ScheduleTemplates": [
            {
              "Id": "{{templateId}}",
              "Name": "模板优先",
              "SchoolStartTime": "07:00:00",
              "LunchStartTime": "12:00:00",
              "LunchEndTime": "13:00:00",
              "SchoolEndTime": "16:00:00"
            }
          ],
          "SchoolStartTime": "09:00:00",
          "SchoolEndTime": "18:00:00"
        }
        """);

        var store = new PluginConfigurationStore(path);

        Assert.Equal(TimeSpan.FromHours(7), store.Configuration.SchoolStartTime);
        Assert.Equal(TimeSpan.FromHours(16), store.Configuration.SchoolEndTime);
        Assert.Equal("模板优先", store.Configuration.ScheduleTemplates[0].Name);
    }

    [Fact]
    public void EmptyLegacyTemplateListKeepsLegacyTimes()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, """
            {
              "ScheduleTemplates": [],
              "SchoolStartTime": "09:00:00",
              "LunchStartTime": "12:00:00",
              "LunchEndTime": "13:30:00",
              "SchoolEndTime": "18:00:00"
            }
            """);

        var configuration = new PluginConfigurationStore(path).Configuration;

        Assert.Single(configuration.ScheduleTemplates);
        Assert.Equal(TimeSpan.FromHours(9), configuration.ScheduleTemplates[0].SchoolStartTime);
        Assert.Equal(TimeSpan.FromHours(18), configuration.ScheduleTemplates[0].SchoolEndTime);
        Assert.Equal(TimeSpan.FromMinutes(13 * 60 + 30), configuration.ScheduleTemplates[0].LunchEndTime);
    }

    [Fact]
    public void CorruptedConfigurationIsBackedUpAndDefaultsAreLoadedSafely()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{ not-json");

        var store = new PluginConfigurationStore(path);

        Assert.NotNull(store.LastError);
        Assert.NotEmpty(Directory.GetFiles(directory.Path, "settings.json.corrupt-*"));
        Assert.Equal(SemesterConfiguration.CurrentSchemaVersion, store.Configuration.SchemaVersion);
    }

    [Fact]
    public void FutureConfigurationVersionIsNeverOverwritten()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        const string futureConfiguration = """
            {
              "schemaVersion": 999,
              "StartDate": "2026-09-01T00:00:00",
              "futureField": "must-survive"
            }
            """;
        File.WriteAllText(path, futureConfiguration);
        var store = new PluginConfigurationStore(path);

        store.Configuration.StartDate = new DateTime(2026, 10, 1);

        Assert.True(store.IsReadOnly);
        Assert.Equal(futureConfiguration, File.ReadAllText(path));
    }

    [Fact]
    public void NullCollectionItemsAreRecoveredAsCorruptedConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, """{"ScheduleTemplates":[null]}""");

        var store = new PluginConfigurationStore(path);

        Assert.False(store.IsReadOnly);
        Assert.Single(store.Configuration.ScheduleTemplates);
        Assert.NotEmpty(Directory.GetFiles(directory.Path, "settings.json.corrupt-*"));
    }

    [Fact]
    public void UnknownFutureDataRemainsReadOnlyEvenWhenTypedDeserializationFails()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        const string futureConfiguration = """
            {
              "schemaVersion": 999,
              "CustomHolidays": [
                {
                  "Name": "未来类型",
                  "StartDate": "2026-01-01T00:00:00",
                  "EndDate": "2026-01-01T00:00:00",
                  "Category": "FutureCategory"
                }
              ]
            }
            """;
        File.WriteAllText(path, futureConfiguration);

        var store = new PluginConfigurationStore(path);
        store.Configuration.EndDate = new DateTime(2026, 12, 31);

        Assert.True(store.IsReadOnly);
        Assert.Equal(futureConfiguration, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "settings.json.corrupt-*"));
    }

    [Fact]
    public void OversizedCollectionsLoadReadOnlyWithoutSilentTruncation()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        var entries = Enumerable.Range(0, SemesterConfiguration.MaximumScheduleTemplates + 1)
            .Select(index => $$"""{"Name":"模板{{index}}"}""");
        File.WriteAllText(path, $$"""{"ScheduleTemplates":[{{string.Join(',', entries)}}]}""");

        var store = new PluginConfigurationStore(path);

        Assert.True(store.IsReadOnly);
        Assert.Contains("超过安全限制", store.LastError);
        Assert.Empty(Directory.GetFiles(directory.Path, "settings.json.corrupt-*"));
    }

    [Fact]
    public void ConfigurationThatSerializesPastReadLimitIsNotWritten()
    {
        using var directory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(directory.Path, "settings.json");
        var store = new PluginConfigurationStore(path);
        store.Save();
        var original = File.ReadAllText(path);
        var longName = new string('中', SemesterConfiguration.MaximumNameLength);

        store.Configuration.ManualTimeExclusions = Enumerable
            .Range(0, SemesterConfiguration.MaximumManualExclusions)
            .Select(index => new ManualTimeExclusion
            {
                Name = longName,
                Date = new DateTime(2026, 1, 1).AddDays(index % 365),
                StartTime = TimeSpan.FromHours(8),
                EndTime = TimeSpan.FromHours(9)
            })
            .ToList();

        Assert.Contains("文件大小限制", store.LastError);
        Assert.Equal(original, File.ReadAllText(path));
    }

    private static HolidayInfo Holiday(string name, HolidayCategory category)
        => new()
        {
            Name = name,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 1),
            Category = category
        };

    private static string HolidayJson(string name) => $$"""
        [
          {
            "Name": "{{name}}",
            "StartDate": "2026-01-01",
            "EndDate": "2026-01-01",
            "Category": "LegalHoliday"
          }
        ]
        """;

    private const string ApiResponse = """
        {
          "code": 0,
          "holiday": {
            "01-01": {
              "holiday": true,
              "name": "元旦",
              "date": "2026-01-01"
            }
          }
        }
        """;

    private const string PartiallyMalformedApiResponse = """
        {
          "code": 0,
          "holiday": {
            "valid": {
              "holiday": true,
              "name": "元旦",
              "date": "2026-01-01"
            },
            "invalid": {
              "name": "缺少 holiday 字段",
              "date": "2026-01-02"
            }
          }
        }
        """;

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
