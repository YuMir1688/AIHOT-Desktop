using System.Net.Http;
using AiHot;
using System.Text.Json;
namespace YuMir.Hot;
public static class HotFeed {
 public const string Url="https://aihot.news/api/v1/items?mode=all&window=24h&by=published&limit=100";
 static readonly HttpClient Client=new(){Timeout=TimeSpan.FromSeconds(30)};
 static Cache cache=Storage.Read<Cache>("today-all-cache.json");
 public static List<NewsItem> Visible(NewsService service,Settings settings)=>cache.Query==Url ? KeepOrder(cache.Items,DateTimeOffset.UtcNow) : new();
 public static List<NewsItem> KeepOrder(IEnumerable<NewsItem> items,DateTimeOffset now)=>items.Where(n=>!string.IsNullOrWhiteSpace(n.Id)&&!string.IsNullOrWhiteSpace(n.Title)&&NewsService.BeijingDate(n.PublishedAt??n.DiscoveredAt)==NewsService.BeijingDate(now)&&(n.PublishedAt??n.DiscoveredAt)<=now).DistinctBy(n=>n.Id).OrderByDescending(n=>n.PublishedAt??n.DiscoveredAt).ThenBy(n=>n.Id).ToList();
 public static string Meta(NewsItem item)=>$"{item.CategoryLabel} · {item.Source.Name} · {(item.PublishedAt??item.DiscoveredAt).ToOffset(TimeSpan.FromHours(8)):MM-dd HH:mm}";
 public static async Task<List<NewsItem>> Fetch(DateTimeOffset now,Func<string,Task<string>> get){
  var items=new List<NewsItem>(); var cursors=new HashSet<string>(); string url=Url;
  while(true){
   using var doc=JsonDocument.Parse(await get(url)); var root=doc.RootElement;
   if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidOperationException("资讯数据格式已变更");
   var pageItems=JsonSerializer.Deserialize<List<NewsItem>>(root.GetProperty("items").GetRawText(),Storage.Json)??throw new InvalidOperationException("资讯列表缺失");
   if(pageItems.Any(n=>n.Source==null||n.Links==null||string.IsNullOrWhiteSpace(n.Id)))throw new InvalidOperationException("资讯字段不完整");
   items.AddRange(pageItems);
   var page=root.GetProperty("page");
   if(!page.GetProperty("hasMore").GetBoolean())break;
   var cursor=page.GetProperty("nextCursor").GetString();
   if(string.IsNullOrWhiteSpace(cursor)||!cursors.Add(cursor))throw new InvalidOperationException("资讯分页异常");
   url=Url+"&cursor="+Uri.EscapeDataString(cursor);
  }
  return KeepOrder(items,now);
 }
 public static async Task<bool> Refresh(NewsService service,Settings settings){
  var now=DateTimeOffset.UtcNow;
  var items=await Fetch(now,async url=>{using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("YuMir-AIHOT-Desktop/1.0");using var response=await Client.SendAsync(request);response.EnsureSuccessStatusCode();return await response.Content.ReadAsStringAsync();});
  if(NewsService.BeijingDate(now)!=NewsService.BeijingDate(DateTimeOffset.UtcNow))return await Refresh(service,settings);
  bool changed=JsonSerializer.Serialize(items)!=JsonSerializer.Serialize(cache.Items);
  var next=new Cache{Query=Url,FetchedAt=DateTimeOffset.Now,Items=items};
  Storage.Save("today-all-cache.json",next);cache=next;
  typeof(NewsService).GetProperty("Cache")!.SetValue(service,next);
  typeof(NewsArchive).GetMethod("Record",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,new object[]{items});
  return changed;
 }
}
