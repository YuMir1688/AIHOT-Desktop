using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AiHot;
public sealed class TiboWindow : Window
{
    const string Endpoint = "https://aihot.news/api/v1/codex-resets/recent";
    static TiboWindow? current;
    static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(25) };
    public sealed class Saved { public string Json {get;set;}=""; public string ETag {get;set;}=""; }
    readonly StackPanel content = new();
    readonly TextBlock status = Ui.Text("",11,Ui.Muted);
    readonly Button refresh = new() { Content="刷新", MinWidth=72 };
    readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMinutes(10) };
    readonly CancellationTokenSource cancellation = new();
    Saved saved = new(); TiboData? data; bool busy, closed; DateTimeOffset retryAt;
    internal static void Open()
    {
        if(current!=null) { current.Show();current.WindowState=WindowState.Normal;current.Activate();return; }
        current=new TiboWindow();current.Closed+=(_,_)=>current=null;current.Show();
    }
    public TiboWindow(string? sampleJson=null, bool online=true)
    {
        Title="AIHOT · Tibo 重置监控";Width=1200;Height=750;MinWidth=800;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel {Margin=new Thickness(28,20,28,24)};
        var top=new DockPanel {Margin=new Thickness(0,0,0,18)};
        var actions=new StackPanel {Orientation=Orientation.Horizontal};actions.Children.Add(Link("完整历史 ↗","https://aihot.news/codex-reset"));actions.Children.Add(refresh);DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);top.Children.Add(Ui.Text("Tibo 重置监控",24));DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        status.Margin=new Thickness(0,12,0,0);DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        root.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Ui.Shell(this,"Tibo 重置监控",root);
        try { saved=sampleJson==null?Storage.Read<Saved>("tibo-api-cache.json"):new Saved{Json=sampleJson};if(saved.Json.Length>0)data=TiboData.Parse(saved.Json); } catch { saved=new(); }
        Paint();refresh.Click+=async(_,_)=>await Refresh();timer.Tick+=async(_,_)=>await Refresh();
        if(online)Loaded+=async(_,_)=>{timer.Start();await Refresh();};
        Closed+=(_,_)=>{closed=true;timer.Stop();cancellation.Cancel();};
    }
    Button Link(string label,string url)
    {
        var button=new Button {Content=label,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0)};
        button.Click+=(_,_)=>{if(!TiboData.SafeLink(url))return;try {Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch {status.Text="无法打开浏览器，请稍后重试。";}};return button;
    }
    void Paint()
    {
        content.Children.Clear();
        var note=Ui.Text("Codex 额度重置与重置卡动态 · 北京时间\n数据来源：AIHOT。确认帖时间不等于精确到账时间，个人额度以账户内显示为准。",13,Ui.Muted);note.Margin=new Thickness(0,0,0,18);content.Children.Add(note);
        if(data==null){content.Children.Add(Ui.Text("尚未取得监控数据",18));return;}
        if(data.Events.Length==0)content.Children.Add(Ui.Text("近期暂无重置或发卡记录",18));
        foreach(var e in data.Events)
        {
            var body=new StackPanel();body.Children.Add(Ui.Text(e.Kind+"  /  "+e.Status,13,Ui.Mint));
            var heading=Ui.Text(e.Title,23);heading.Margin=new Thickness(0,8,0,10);body.Children.Add(heading);
            body.Children.Add(Ui.Text("适用范围："+(e.Audience.Length==0?"尚未明确":e.Audience),13,Ui.Muted));
            if(e.Confirmed.Length>0)body.Children.Add(Ui.Text("确认帖时间："+TiboData.Time(e.Confirmed),13,Ui.Muted));
            if(e.Estimate.Length>0)body.Children.Add(Ui.Text("原始预告："+e.Estimate+"（预计，非到账凭据）",12,Ui.Muted));
            foreach(var post in e.Posts)
            {
                var row=new StackPanel {Margin=new Thickness(0,14,0,0)};row.Children.Add(Ui.Text(post.Stage,11,Ui.Muted));row.Children.Add(Ui.Text(post.Text,15));
                if(post.Url.Length>0){var link=Link("查看原帖 ↗",post.Url);link.HorizontalAlignment=HorizontalAlignment.Left;link.Margin=new Thickness(0,8,0,0);row.Children.Add(link);}body.Children.Add(row);
            }
            var card=Ui.Card(body);card.Margin=new Thickness(0,0,0,14);content.Children.Add(card);
        }
        status.Text=$"来源最后核验：{TiboData.Time(data.CheckedAt)} · {(data.Health=="healthy"?"来源监控正常":"来源状态待核实")} · 打开期间每 10 分钟刷新";
    }
    async Task Refresh()
    {
        if(busy||closed)return;
        if(DateTimeOffset.UtcNow<retryAt){status.Text="服务暂时限流，请稍后重试。";return;}
        busy=true;refresh.IsEnabled=false;status.Text="正在核对监控状态…";
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,Endpoint);request.Headers.UserAgent.ParseAdd("aihot-api/2.0.0");
            if(saved.ETag.Length>0&&data!=null)request.Headers.TryAddWithoutValidation("If-None-Match",saved.ETag);
            using var response=await Client.SendAsync(request,cancellation.Token);
            if(response.Headers.RetryAfter?.Delta is TimeSpan delay)retryAt=DateTimeOffset.UtcNow+delay;
            else if(response.Headers.RetryAfter?.Date is DateTimeOffset date)retryAt=date;
            if(response.StatusCode==HttpStatusCode.NotModified){if(!closed){Paint();status.Text+=" · 已核对，无变化";}return;}
            response.EnsureSuccessStatusCode();var json=await response.Content.ReadAsStringAsync(cancellation.Token);
            var next=await Task.Run(()=>TiboData.Parse(json),cancellation.Token);if(closed)return;
            data=next;saved=new Saved{Json=json,ETag=response.Headers.ETag?.ToString()??""};Storage.Save("tibo-api-cache.json",saved);Paint();
        }
        catch(OperationCanceledException)when(closed){}
        catch(Exception){if(!closed){Paint();status.Text="刷新失败 · "+(data==null?"暂无缓存":$"保留上次结果，来源核验 {TiboData.Time(data.CheckedAt)}")+" · 请稍后重试";}}
        finally {busy=false;if(!closed)refresh.IsEnabled=true;}
    }
}
