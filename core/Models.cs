using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AiHot;

public sealed class NewsSource { public string Name { get; set; } = ""; }
public sealed class NewsLinks { public string Original { get; set; } = ""; public string Aihot { get; set; } = ""; }
public sealed class NewsItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? Reason { get; set; }
    public string? Category { get; set; }
    public NewsSource Source { get; set; } = new();
    public NewsLinks Links { get; set; } = new();
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
    public string CategoryLabel => Category switch { "ai-models" => "模型更新", "ai-products" => "产品发布", "industry" => "行业动态", "paper" => "论文研究", "tip" => "技巧观点", _ => "AI 动态" };
    public string Meta => $"{CategoryLabel}  ·  {Source.Name}  ·  {(PublishedAt ?? DiscoveredAt).ToLocalTime():MM-dd HH:mm}";
}
public sealed class Feed { public int SchemaVersion { get; set; } public List<NewsItem> Items { get; set; } = new(); }
public sealed class Settings
{
    public double Left { get; set; } = -100000;
    public double Top { get; set; } = -100000;
    public double Width { get; set; } = 960;
    public double Opacity { get; set; } = 0.96;
    public double Speed { get; set; } = 48;
    public bool Topmost { get; set; } = true;
    public bool PositionLocked { get; set; }
    public bool LightTheme { get; set; }
    public bool HideFullscreen { get; set; } = true;
    public string Category { get; set; } = "";
    public string Window { get; set; } = "today";
    public int FeedPreferenceVersion { get; set; }
}
public sealed class Cache
{
    public string Query { get; set; } = "";
    public string? ETag { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public List<NewsItem> Items { get; set; } = new();
}
public static class Storage
{
    public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YuMir", "AIHOT.Desktop");
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string? LastError { get; private set; }
    public static T Read<T>(string name) where T : new()
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Root, name)), Json) ?? new T(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new T(); }
    }
    public static void Save<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(Root);
            string path = Path.Combine(Root, name);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json));
            File.Move(path + ".tmp", path, true);
            LastError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastError = e.Message; }
    }
}
public sealed class NewsService : IDisposable
{
    private readonly HttpClient client;
    public Cache Cache { get; private set; } = Storage.Read<Cache>("cache.json");
    public NewsService(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YuMir-AIHOT-Desktop/1.0");
    }
    public static string Query(Settings s) => "https://aihot.news/api/v1/items?mode=selected&window=" + (s.Window == "24h" ? "24h" : "7d") + (s.Window == "today" ? "&by=published" : "") + "&limit=100" + (string.IsNullOrEmpty(s.Category) ? "" : "&category=" + Uri.EscapeDataString(s.Category));
    public static DateTime BeijingDate(DateTimeOffset time) => time.ToOffset(TimeSpan.FromHours(8)).Date;
    public static List<NewsItem> FilterToday(IEnumerable<NewsItem> items, DateTimeOffset now) => items.Where(n => BeijingDate(n.PublishedAt ?? n.DiscoveredAt) == BeijingDate(now) && (n.PublishedAt ?? n.DiscoveredAt) <= now).ToList();
    public static List<NewsItem> LatestHundred(IEnumerable<NewsItem> items, DateTimeOffset now)
    {
        return items.Where(n => !string.IsNullOrWhiteSpace(n.Id) && !string.IsNullOrWhiteSpace(n.Title) && (n.PublishedAt ?? n.DiscoveredAt) <= now)
            .OrderByDescending(n => n.PublishedAt ?? n.DiscoveredAt).DistinctBy(n => n.Id).Take(100).ToList();
    }
    public List<NewsItem> VisibleItems(Settings settings) => Cache.Query != Query(settings) ? new() : settings.Window == "today" ? LatestHundred(Cache.Items, DateTimeOffset.UtcNow) : Cache.Items;
    public async Task<bool> Refresh(Settings settings)
    {
        string query = Query(settings);
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        if (Cache.Query == query && Cache.ETag != null) request.Headers.TryAddWithoutValidation("If-None-Match", Cache.ETag);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (Cache.Query != query) throw new InvalidOperationException("收到无对应缓存的响应");
            Cache.FetchedAt = DateTimeOffset.Now;
            Storage.Save("cache.json", Cache);
            NewsArchive.Record(Cache.Items);
            return false;
        }
        if (!response.IsSuccessStatusCode)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
            throw new FeedException((int)response.StatusCode, delay);
        }
        var feed = JsonSerializer.Deserialize<Feed>(await response.Content.ReadAsStringAsync(), Storage.Json);
        if (feed == null || feed.SchemaVersion != 1 || feed.Items == null) throw new InvalidOperationException("资讯数据格式已变更");
        Cache = new Cache { Query = query, ETag = response.Headers.ETag?.ToString(), FetchedAt = DateTimeOffset.Now,
            Items = feed.Items.Where(n => !string.IsNullOrWhiteSpace(n.Id) && !string.IsNullOrWhiteSpace(n.Title) && n.Source != null && n.Links != null).DistinctBy(n => n.Id).ToList() };
        Storage.Save("cache.json", Cache);
        NewsArchive.Record(Cache.Items);
        return true;
    }
    public void Dispose() => client.Dispose();
}
public sealed class FeedException : Exception
{
    public TimeSpan? Retry { get; }
    public FeedException(int status, TimeSpan? retry) : base($"服务暂不可用（{status}）") { Retry = retry; }
}
