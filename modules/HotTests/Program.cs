using AiHot;
using System.IO;
using YuMir.Hot;
using System.Net.Http;
Storage.Root=Path.Combine(Path.GetTempPath(),"aihot-hot-test-"+Guid.NewGuid().ToString("N"));
using var http=new HttpClient();http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
var html=await http.GetStringAsync(HotFeed.Url);
var items=HotFeed.Parse(html);
if(items.Count==0)throw new Exception("Expected live ranking");
if(!NewsService.LatestHundred(items,DateTimeOffset.Now).Select(n=>n.Id).SequenceEqual(items.Select(n=>n.Id)))throw new Exception("Order changed");
for(int i=0;i<items.Count;i++)Console.WriteLine($"{i+1:00} {items[i].Title}");
try{HotFeed.Parse("<html>invalid response</html>");throw new Exception("Invalid response accepted");}catch(InvalidOperationException){}
var empty=HotFeed.Parse("<script type=\"application/ld+json\">{\"@type\":\"ItemList\",\"itemListElement\":[]}</script>");if(empty.Count!=0)throw new Exception("Empty feed failed");
using var service=new NewsService();await service.Refresh(new Settings());
if(service.VisibleItems(new Settings()).Count==0)throw new Exception("Bridge refresh failed");
foreach(var item in service.VisibleItems(new Settings())){if(new Uri(item.Links.Original).Host=="aihot.news")throw new Exception("Original link unresolved");Console.WriteLine(item.Title+" => "+item.Links.Original);}
Console.WriteLine($"PASS {items.Count} website events, original order, links, invalid/empty response, patched refresh, isolated cache");
