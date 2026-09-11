using AiHot;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace YuMir.Hot;
public static class HotFeed {
 public const string Url="https://aihot.news/hot";
 static readonly HttpClient Client=new(){Timeout=TimeSpan.FromSeconds(25)};
 static Cache cache=Storage.Read<Cache>("hot-cache.json");
 public static List<NewsItem> Visible(NewsService service,Settings settings)=>cache.Query==Url ? cache.Items.ToList() : new();
 public static List<NewsItem> KeepOrder(IEnumerable<NewsItem> items,DateTimeOffset now)=>items.ToList();
 public static string Meta(NewsItem item)=>item.Id.StartsWith("hot:") ? "48 小时热点 · "+item.Source.Name : $"{item.CategoryLabel} · {item.Source.Name} · {(item.PublishedAt??item.DiscoveredAt).ToLocalTime():MM-dd HH:mm}";
 static string Clean(string s)=>WebUtility.HtmlDecode(Regex.Replace(s,"<[^>]+>","",RegexOptions.Singleline)).Trim();
 public static List<NewsItem> Parse(string html){
  var blocks=Regex.Matches(html,"<script[^>]*type=\"application/ld\\+json\"[^>]*>(.*?)</script>",RegexOptions.Singleline);
  JsonElement list=default;
  foreach(Match block in blocks){using var doc=JsonDocument.Parse(block.Groups[1].Value); if(doc.RootElement.TryGetProperty("@type",out var type)&&type.GetString()=="ItemList"&&doc.RootElement.TryGetProperty("itemListElement",out var found)){list=found.Clone();break;}}
  if(list.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("热榜结构已变化，暂时保留上次榜单");
  var rows=Regex.Matches(html,"<li class=\"hot-rank-row\".*?(?=<li class=\"hot-rank-row\"|</ol>)",RegexOptions.Singleline);
  var result=new List<NewsItem>();
  foreach(var entry in list.EnumerateArray().OrderBy(e=>e.GetProperty("position").GetInt32())){
   string url=entry.GetProperty("url").GetString()!,title=entry.GetProperty("name").GetString()!;
   if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Host!="aihot.news"||!uri.AbsolutePath.StartsWith("/story/"))throw new InvalidOperationException("热榜事件链接异常");
   var row=rows.Cast<Match>().SingleOrDefault(r=>r.Value.Contains("href=\""+uri.AbsolutePath+"\""))?.Value??throw new InvalidOperationException("热榜条目不完整");
   var summary=Regex.Match(row,"<p class=\"hot-rank-summary\"><span>报道摘要</span>(.*?)</p>",RegexOptions.Singleline);
   var heat=Regex.Match(row,"class=\"hot-rank-heat-value\">(.*?)<small>",RegexOptions.Singleline);
   var time=Regex.Match(row,"<time dateTime=\"([^\"]+)\"");
   if(string.IsNullOrWhiteSpace(title)||!summary.Success||!time.Success)throw new InvalidOperationException("热榜字段不完整");
   result.Add(new(){Id="hot:"+uri.Segments.Last(),Title=title,Summary=Clean(summary.Groups[1].Value),Reason="AIHOT 过去 48 小时热点榜，按讨论热度排序；点击查看事件及相关报道。",Source=new(){Name="热度 "+Clean(heat.Groups[1].Value)},Links=new(){Original=url,Aihot=url},PublishedAt=DateTimeOffset.Parse(time.Groups[1].Value),DiscoveredAt=DateTimeOffset.Parse(time.Groups[1].Value)});
  }
  if(result.Count!=rows.Count||result.Select(n=>n.Id).Distinct().Count()!=result.Count)throw new InvalidOperationException("热榜数量校验失败");
  return result;
 }
 public static async Task<bool> Refresh(NewsService service,Settings settings){
  using var request=new HttpRequestMessage(HttpMethod.Get,Url);request.Headers.UserAgent.ParseAdd("Mozilla/5.0 YuMir-AIHOT/1.0");
  using var response=await Client.SendAsync(request);response.EnsureSuccessStatusCode();
  var items=Parse(await response.Content.ReadAsStringAsync());
  foreach(var item in items){
   var previous=cache.Items.FirstOrDefault(n=>n.Id==item.Id&&n.Title==item.Title&&IsOriginal(n.Links.Original));
   item.Links.Original=previous?.Links.Original??await ResolveOriginal(item);
   item.Reason="AIHOT 过去 48 小时热点榜，按讨论热度排序；点击直达该摘要对应的原始报道。";
  }
  bool changed=JsonSerializer.Serialize(items)!=JsonSerializer.Serialize(cache.Items);
  var next=new Cache{Query=Url,FetchedAt=DateTimeOffset.Now,Items=items};Storage.Save("hot-cache.json",next);cache=next;
  return changed;
 }
 static bool IsOriginal(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)&&(u.Scheme=="https"||u.Scheme=="http")&&u.Host!="aihot.news";
 static async Task<string> Page(string url){using var req=new HttpRequestMessage(HttpMethod.Get,url);req.Headers.UserAgent.ParseAdd("Mozilla/5.0");using var res=await Client.SendAsync(req);res.EnsureSuccessStatusCode();return await res.Content.ReadAsStringAsync();}
 public static async Task<string> ResolveOriginal(NewsItem item){
  var story=await Page(item.Links.Aihot);
  var links=Regex.Matches(story,"<a[^>]*href=\"(/items/[^\"]+)\"[^>]*>(.*?)</a>",RegexOptions.Singleline);
  var match=links.Cast<Match>().FirstOrDefault(m=>Clean(m.Groups[2].Value)==item.Title);
  if(match==null)throw new InvalidOperationException("未找到热点摘要对应的原始报道："+item.Title);
  var article=await Page("https://aihot.news"+match.Groups[1].Value);
  var original=Regex.Matches(article,"<a\\b[^>]*>",RegexOptions.Singleline).Cast<Match>().FirstOrDefault(m=>m.Value.Contains("data-track=\"click_external\""));
  var url=original==null?"":WebUtility.HtmlDecode(Regex.Match(original.Value,"href=\"([^\"]+)\"").Groups[1].Value);
  if(!IsOriginal(url))throw new InvalidOperationException("原文链接不可用："+item.Title+" tag="+original?.Value+" page="+match.Groups[1].Value);
  return url;
 }
}
