using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AiHot;
public sealed class TiboPanel : UserControl
{
    const string Endpoint = "https://aihot.news/api/v1/codex-resets";
    static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(25) };
    public sealed class Saved { public string Json {get;set;}=""; public string ETag {get;set;}=""; }
    readonly Grid content = new();
    readonly TextBlock status = Ui.Text("",11,Ui.Muted);
    readonly Button refresh = new() { Content="刷新", MinWidth=72, Height=36, FontSize=12 };
    readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMinutes(10) };
    readonly CancellationTokenSource cancellation = new();
    static DateTime Today => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;
    DateTime month = new(Today.Year,Today.Month,1);
    DateTime? selectedDate;
    readonly StackPanel details = new();
    Saved saved = new(); TiboData? data; bool busy, closed; DateTimeOffset retryAt;
    public TiboPanel(string? sampleJson=null, bool online=true)
    {
        var root=new DockPanel {Margin=new Thickness(24,8,24,18)};
        var top=new DockPanel {Margin=new Thickness(0,0,0,18)};
        var actions=new StackPanel {Orientation=Orientation.Horizontal};actions.Children.Add(refresh);DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);top.Children.Add(Ui.Text("重置日历",18));DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        status.Margin=new Thickness(0,8,0,0);status.Visibility=Visibility.Collapsed;DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        root.Children.Add(content);Content=root;
        try { saved=sampleJson==null?Storage.Read<Saved>("tibo-calendar-cache.json"):new Saved{Json=sampleJson};if(saved.Json.Length>0)data=TiboData.Parse(saved.Json); } catch { saved=new(); }
        Paint();refresh.Click+=async(_,_)=>await Refresh();timer.Tick+=async(_,_)=>await Refresh();
        if(online)Loaded+=async(_,_)=>{timer.Start();await Refresh();};
        Unloaded+=(_,_)=>timer.Stop();
    }
    internal void Stop() { closed=true;timer.Stop();cancellation.Cancel();
    }
    Button Link(string label,string url)
    {
        var button=new Button {Content=label,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0)};
        button.Click+=(_,_)=>{if(!TiboData.SafeLink(url))return;try {Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch {status.Visibility=Visibility.Visible;status.Text="无法打开浏览器，请稍后重试。";}};return button;
    }
    string Footer() => data==null ? "尚未取得监控数据" : $"来源：AIHOT · 核验 {TiboData.Time(data.CheckedAt)} · 北京时间 · 每 10 分钟刷新";
    void Paint()
    {
        status.Visibility=Visibility.Collapsed;content.Children.Clear();content.RowDefinitions.Clear();content.RowDefinitions.Add(new RowDefinition());status.Text=Footer();
        if(data==null || data.Events.Length==0){content.Children.Add(Ui.Text(data==null?"尚未取得监控数据":"近期暂无重置或发卡记录",18));return;}
        var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1.35,GridUnitType.Star)});layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(18)});layout.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(layout,0);
        var calendar=new DockPanel();
        var navigation=new DockPanel {Margin=new Thickness(0,0,0,14)};
        var controls=new StackPanel {Orientation=Orientation.Horizontal};
        void Navigate(string label,Action action){var button=new Button {Content=label,Height=32,MinWidth=32,Padding=new Thickness(10,4,10,4),Margin=new Thickness(6,0,0,0)};button.Click+=(_,_)=>{action();Paint();};controls.Children.Add(button);}
        Navigate("‹",()=>{month=month.AddMonths(-1);selectedDate=null;});Navigate("本月",()=>{var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;month=new(now.Year,now.Month,1);selectedDate=now;});Navigate("›",()=>{month=month.AddMonths(1);selectedDate=null;});DockPanel.SetDock(controls,Dock.Right);navigation.Children.Add(controls);navigation.Children.Add(Ui.Text(month.ToString("yyyy 年 M 月"),20));DockPanel.SetDock(navigation,Dock.Top);calendar.Children.Add(navigation);
        var legend=Ui.Text("● 已确认   ○ 待确认 · 具体日期含义见右侧详情",11,Ui.Muted);legend.Margin=new Thickness(0,10,0,0);legend.TextAlignment=TextAlignment.Center;legend.HorizontalAlignment=HorizontalAlignment.Stretch;DockPanel.SetDock(legend,Dock.Bottom);calendar.Children.Add(legend);
        var start=month.AddDays(-((int)month.DayOfWeek+6)%7);
        int weeks=(int)Math.Ceiling(((month-start).Days+DateTime.DaysInMonth(month.Year,month.Month))/7.0);
        var cells=new Grid();for(int i=0;i<7;i++)cells.ColumnDefinitions.Add(new ColumnDefinition());cells.RowDefinitions.Add(new RowDefinition {Height=new GridLength(28)});for(int i=0;i<weeks;i++)cells.RowDefinitions.Add(new RowDefinition());
        var weekdays=new[]{"一","二","三","四","五","六","日"};for(int i=0;i<7;i++){var day=Ui.Text(weekdays[i],12,Ui.Muted);day.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(day,i);cells.Children.Add(day);}
        selectedDate??=data.Events.FirstOrDefault(e=>e.CalendarDate?.Year==month.Year && e.CalendarDate?.Month==month.Month)?.CalendarDate??month;
        for(int i=0;i<weeks*7;i++)
        {
            var date=start.AddDays(i);var records=data.Events.Where(e=>e.CalendarDate==date).ToArray();var row=new StackPanel();var number=Ui.Text(date.Day.ToString(),14,date.Month==month.Month?Ui.Ink:Ui.Muted);var dayHeader=new DockPanel();if(date==Today){var today=Ui.Text("今天",10,Ui.Mint);today.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(today,Dock.Right);dayHeader.Children.Add(today);}dayHeader.Children.Add(number);number.LineHeight=20;row.Children.Add(dayHeader);
            if(records.Length>0){var text=Ui.Text((records.All(e=>e.RawStatus=="confirmed")?"● ":"○ ")+(records.Length==1?records[0].Kind:records.Length+" 条动态"),10,records.All(e=>e.RawStatus=="confirmed")?Ui.Mint:Ui.Muted);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;row.Children.Add(text);}
            var button=new Button {Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(10,5,10,5),Margin=new Thickness(3),BorderThickness=new Thickness(1),BorderBrush=Ui.Brush(selectedDate==date?"#538F82":date==Today?"#34534F":"#1B2834"),Background=Ui.Brush(selectedDate==date?"#193531":date.Month==month.Month?"#14202B":"#101922")};System.Windows.Automation.AutomationProperties.SetName(button,date.ToString("yyyy-MM-dd")+" · "+records.Length+" 条记录");button.Click+=(_,_)=>{selectedDate=date;if(date.Month!=month.Month)month=new(date.Year,date.Month,1);Paint();};Grid.SetColumn(button,i%7);Grid.SetRow(button,i/7+1);cells.Children.Add(button);
        }
        calendar.Children.Add(cells);var calendarCard=Ui.Card(calendar,"#111B25",18);calendarCard.VerticalAlignment=VerticalAlignment.Top;calendarCard.Height=weeks*64+132;layout.Children.Add(calendarCard);
        var detailPanel=new DockPanel();var dayHeading=Ui.Text(selectedDate.Value.ToString("M 月 d 日")+" · 记录",20);dayHeading.Height=32;dayHeading.Margin=new Thickness(0,0,0,14);DockPanel.SetDock(dayHeading,Dock.Top);detailPanel.Children.Add(dayHeading);var detailScroll=new ScrollViewer {Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};detailPanel.Children.Add(detailScroll);var detailCard=Ui.Card(detailPanel,"#111B25",18);detailCard.VerticalAlignment=VerticalAlignment.Top;detailCard.Height=weeks*64+132;Grid.SetColumn(detailCard,2);layout.Children.Add(detailCard);content.Children.Add(layout);
        details.Children.Clear();
        var dayEvents=data.Events.Where(e=>e.CalendarDate==selectedDate).ToArray();if(dayEvents.Length==0){var empty=new StackPanel();empty.Children.Add(Ui.Text("当天暂无记录",18));var hint=Ui.Text("带有状态标记的日期可以查看重置和发卡动态。",13,Ui.Muted);hint.Margin=new Thickness(0,10,0,0);empty.Children.Add(hint);details.Children.Add(Ui.Card(empty,"#121C27",24));}foreach(var e in dayEvents)ShowDetail(e);
    }
    void ShowDetail(TiboEvent e)
    {
        var body=new StackPanel();body.Children.Add(Ui.Badge(e.Kind+" / "+e.Status));
        var heading=Ui.Text(e.Title,20);heading.Margin=new Thickness(0,12,0,18);body.Children.Add(heading);
        body.Children.Add(Ui.Text(e.DateMeaning+"："+e.CalendarDate?.ToString("yyyy-MM-dd"),12,Ui.Muted));
        body.Children.Add(Ui.Text("适用人群："+(e.Audience.Length==0?"尚未明确":e.Audience),14));
        if(e.Confirmed.Length>0)body.Children.Add(Ui.Text("确认帖时间："+TiboData.Time(e.Confirmed),13,Ui.Muted));
        if(e.Estimate.Length>0)body.Children.Add(Ui.Text("原始预告："+e.Estimate+"（预计）",12,Ui.Muted));
        StackPanel Post(TiboPost post)
        {
            var row=new StackPanel {Margin=new Thickness(0,12,0,12)};row.Children.Add(Ui.Text(post.Stage,12,Ui.Mint));row.Children.Add(Ui.Text(post.Text,15));
            if(post.Url.Length>0){var link=Link("查看原帖 ↗",post.Url);link.HorizontalAlignment=HorizontalAlignment.Left;link.Margin=new Thickness(0,10,0,0);row.Children.Add(link);}return row;
        }
        if(e.Posts.Length>0)
        {
            var label=Ui.Text("最新进展",14,Ui.Mint);label.Margin=new Thickness(0,18,0,0);body.Children.Add(new Border {Height=1,Background=Ui.Line,Margin=new Thickness(0,18,0,0)});body.Children.Add(label);body.Children.Add(Post(e.Posts[0]));
            if(e.Posts.Length>1){var history=new StackPanel();foreach(var post in e.Posts.Skip(1))history.Children.Add(Post(post));body.Children.Add(new Expander {Foreground=Ui.Muted,Header=$"展开更早的 {e.Posts.Length-1} 条原帖",Content=history,Margin=new Thickness(0,12,0,0)});}
        }
        var card=Ui.Card(body,padding:20);card.Margin=new Thickness(0,0,0,12);details.Children.Add(card);
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
            if(response.StatusCode==HttpStatusCode.NotModified){if(!closed){status.Visibility=Visibility.Collapsed;status.Text=Footer()+" · 已核对，无变化";}return;}
            response.EnsureSuccessStatusCode();var json=await response.Content.ReadAsStringAsync(cancellation.Token);
            var next=await Task.Run(()=>TiboData.Parse(json),cancellation.Token);if(closed)return;
            data=next;saved=new Saved{Json=json,ETag=response.Headers.ETag?.ToString()??""};Storage.Save("tibo-calendar-cache.json",saved);Paint();
        }
        catch(OperationCanceledException)when(closed){}
        catch(Exception){if(!closed){status.Visibility=Visibility.Visible;status.Text="刷新失败 · "+(data==null?"暂无缓存":$"保留上次结果，来源核验 {TiboData.Time(data.CheckedAt)}")+" · 请稍后重试";}}
        finally {busy=false;if(!closed)refresh.IsEnabled=true;}
    }
}




// Preview host; production navigation embeds the panel in the existing reader.
public sealed class TiboWindow : Window
{
    internal static void Open()
    {
        if(Application.Current.MainWindow is MainWindow main) main.NavigateCalendar();
    }
    public TiboWindow(string? sampleJson=null,bool online=true)
    {
        Title="AIHOT · Tibo 重置监控";Width=1200;Height=750;MinWidth=800;MinHeight=500;
        var panel=new TiboPanel(sampleJson,online);Ui.Shell(this,"Tibo 重置监控",panel);Closed+=(_,_)=>panel.Stop();
    }
}

