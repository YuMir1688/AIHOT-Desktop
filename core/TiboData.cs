using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AiHot;
public sealed record TiboPost(string Text, string Url, string Stage);
public sealed record TiboEvent(string Id, string Title, string Kind, string Status, string Audience, string Estimate, string Confirmed, TiboPost[] Posts)
{
    public string OccurredOn { get; init; } = "";
    public string ScheduledAt { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public DateTime? CalendarDate => DateTimeOffset.TryParse(OccurredOn.Length>0?OccurredOn:Confirmed.Length>0?Confirmed:ScheduledAt.Length>0?ScheduledAt:CreatedAt, out var date) ? date.ToOffset(TimeSpan.FromHours(8)).Date : null;
    public string DateMeaning => OccurredOn.Length>0?"发生日期":Confirmed.Length>0?"确认帖日期":ScheduledAt.Length>0?"预计日期":"预告帖日期";
    public string RawStatus { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
}
public sealed record TiboData(string CheckedAt, string Health, TiboEvent[] Events)
{
    public static string Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    static JsonElement Child(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
    public static string Time(string value) => DateTimeOffset.TryParse(value, out var time) ? time.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm") : "未提供";
    public static bool SafeLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && (uri.Host == "x.com" || uri.Host == "twitter.com" || uri.Host == "aihot.news");
    public static TiboData Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || Text(root, "timezone") != "Asia/Shanghai" || Child(root,"events").ValueKind != JsonValueKind.Array) throw new InvalidOperationException("监控数据格式已变化");
        var events = new List<TiboEvent>();
        foreach (var e in root.GetProperty("events").EnumerateArray())
        {
            var presentation = Child(e,"presentation");
            string status = Text(presentation,"status"); if (status.Length == 0) status = Text(e,"status");
            string label = status switch { "confirmed" => "已确认", "announced" => "已宣布，等待确认", "expected" => "预计已生效，尚未确认", _ => "待核实 · " + status };
            var posts = Child(e,"posts");
            var parsed = posts.ValueKind == JsonValueKind.Array ? posts.EnumerateArray().Select(p => new TiboPost(Text(p,"text"), SafeLink(Text(p,"url")) ? Text(p,"url") : "", Text(p,"stage"))).ToArray() : Array.Empty<TiboPost>();
            string audience = Text(presentation,"audienceZh"); if (audience.Length == 0) audience = Text(e,"scope");
            string kind = Text(e,"displayLabel"); if (kind.Length == 0) kind = Text(e,"type") == "direct_reset" ? "额度重置" : "重置记录";
            if (Text(e,"id").Length == 0 || Text(e,"title").Length == 0) throw new InvalidOperationException("监控记录不完整");
            events.Add(new(Text(e,"id"),Text(e,"title"),kind,label,audience,Text(Child(e,"schedule"),"label"),Text(e,"confirmedAt"),parsed) { OccurredOn=Text(e,"occurredOn"), ScheduledAt=Text(Child(e,"schedule"),"from"), CreatedAt=Text(e,"createdAt"), RawStatus=Text(e,"status"), UpdatedAt=Text(e,"updatedAt") });
        }
        if (events.Select(e=>e.Id).Distinct().Count()!=events.Count) throw new InvalidOperationException("监控记录重复");
        return new(Text(root,"checkedAt"),Text(Child(root,"monitor"),"status"),events.ToArray());
    }
}
