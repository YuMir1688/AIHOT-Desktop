using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AiHot;
namespace YuMir.Cards;
public static class TelegramPush
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YuMir", "AIHOT.Desktop");
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static int started;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public sealed class State { public HashSet<string> Seen { get; set; } = []; public List<NewsItem> Pending { get; set; } = []; public string? InFlight { get; set; } }
    public static void Start()
    {
        ScrollbarAppearance.Install();
        if (Interlocked.Exchange(ref started, 1) != 0) return;
        _ = Task.Run(async () => { while (true) { try { await Poll(); } catch { Status("自动推送暂未完成，将稍后重试。请检查网络或本地配置。"); } await Task.Delay(TimeSpan.FromSeconds(20)); } });
    }
    private static void Save(State state)
    {
        string path = Path.Combine(Root, "telegram-state.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state, Json)); File.Move(path + ".tmp", path, true);
    }
    private static void Status(string message) => File.WriteAllText(Path.Combine(Root, "telegram-status.txt"), DateTimeOffset.Now.ToString("u") + " " + message);
    public static int Discover(State state, IEnumerable<NewsItem> items)
    {
        int count = 0;
        foreach (var item in items.OrderBy(n => n.PublishedAt ?? n.DiscoveredAt))
            if (!string.IsNullOrWhiteSpace(item.Id) && state.Seen.Add(item.Id)) { state.Pending.Add(item); count++; }
        return count;
    }
    private static async Task Poll()
    {
        string configPath = Path.Combine(Root, "telegram.local.json");
        if (!File.Exists(configPath)) return;
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var c = config.RootElement;
        if (c.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) return;
        string token = c.GetProperty("bot_token").GetString()?.Trim() ?? "";
        string channel = c.GetProperty("channel_id").GetString() ?? "";
        if (token.Length == 0 || channel.Length == 0) return;
        string cachePath = Path.Combine(Root, "cache.json"); if (!File.Exists(cachePath)) return;
        using var cache = JsonDocument.Parse(File.ReadAllText(cachePath));
        var items = JsonSerializer.Deserialize<List<NewsItem>>(cache.RootElement.GetProperty("Items").GetRawText(), Json) ?? [];
        string statePath = Path.Combine(Root, "telegram-state.json");
        if (!File.Exists(statePath)) { Save(new State { Seen = items.Select(n => n.Id).ToHashSet() }); Status("已启用，新精选出现后推送；现有资讯不补发。"); return; }
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath), Json) ?? throw new InvalidDataException();
        if (state.InFlight != null)
        {
            // After an interrupted request its delivery is uncertain: do not blindly resend.
            state.Pending.RemoveAll(n => n.Id == state.InFlight); state.InFlight = null; Save(state);
            Status("上次发送结果未确认，已避免自动重复发送。");
        }
        Discover(state, items); Save(state);
        foreach (var item in state.Pending.ToArray())
        {
            string summary = item.Summary ?? ""; if (summary.Length > 2300) summary = summary[..2300] + "…";
            string title = item.Title.Length > 500 ? item.Title[..500] : item.Title;
            string message = $"AIHOT 精选 · {item.CategoryLabel}\n\n{title}\n\n{summary}\n\n来源：{item.Source.Name}";
            if (Uri.TryCreate(item.Links.Original, UriKind.Absolute, out var url) && (url.Scheme == "https" || url.Scheme == "http") && url.AbsoluteUri.Length < 800) message += "\n阅读完整内容：" + url.AbsoluteUri;
            state.InFlight = item.Id; Save(state);
            using var response = await Client.PostAsync("https://api.telegram.org/bot" + token + "/sendMessage", new StringContent(JsonSerializer.Serialize(new { chat_id = channel, text = message, link_preview_options = new { is_disabled = true } }), Encoding.UTF8, "application/json"));
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!response.IsSuccessStatusCode || !result.RootElement.GetProperty("ok").GetBoolean())
            {
                state.InFlight = null; Save(state); Status("频道发送失败，等待重试。HTTP " + (int)response.StatusCode);
                int delay = 60;
                if (result.RootElement.TryGetProperty("parameters", out var parameters) && parameters.TryGetProperty("retry_after", out var retry)) delay = Math.Max(delay, retry.GetInt32());
                await Task.Delay(TimeSpan.FromSeconds(delay)); return;
            }
            state.Pending.RemoveAll(n => n.Id == item.Id); state.InFlight = null; Save(state); Status("新精选已发送到频道。");
            await Task.Delay(1500);
        }
    }
}
