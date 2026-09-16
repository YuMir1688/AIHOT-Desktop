using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AiHot;
public sealed class NewsArchive
{
    public DateTimeOffset StartedAt { get; set; }
    public List<NewsItem> Items { get; set; } = new();
    internal static NewsArchive Read() => Storage.Read<NewsArchive>("history.json");
    internal static void Record(IEnumerable<NewsItem> incoming)
    {
        var archive = Read();
        if (archive.StartedAt == default) archive.StartedAt = DateTimeOffset.UtcNow;
        archive.Items = Merge(incoming.Concat(archive.Items ?? new()), DateTimeOffset.UtcNow);
        Storage.Save("history.json", archive);
    }
    internal static List<NewsItem> Merge(IEnumerable<NewsItem> items, DateTimeOffset now) => items
        .Where(n => n != null && !string.IsNullOrWhiteSpace(n.Id) && !string.IsNullOrWhiteSpace(n.Title) && n.Source != null && n.Links != null && (n.PublishedAt ?? n.DiscoveredAt) <= now)
        .DistinctBy(n => n.Id).OrderByDescending(n => n.PublishedAt ?? n.DiscoveredAt).ToList();
}
internal sealed class ReportData
{
    internal DateTime Start, End;
    internal List<NewsItem> Items = new();
    internal static ReportData Create(IEnumerable<NewsItem> items, int period, DateTime anchor, DateTimeOffset now)
    {
        var start = anchor.Date;
        if (period == 2) start = start.AddDays(-((int)start.DayOfWeek + 6) % 7);
        if (period == 3) start = new DateTime(start.Year, start.Month, 1);
        var end = period == 3 ? start.AddMonths(1) : start.AddDays(period == 2 ? 7 : 1);
        return new ReportData { Start = start, End = end, Items = NewsArchive.Merge(items, now).Where(n => NewsService.BeijingDate(n.PublishedAt ?? n.DiscoveredAt) >= start && NewsService.BeijingDate(n.PublishedAt ?? n.DiscoveredAt) < end).ToList() };
    }
}
internal sealed class ReportPanel : DockPanel
{
    private readonly int period;
    private DateTime anchor = NewsService.BeijingDate(DateTimeOffset.UtcNow);
    private readonly TextBlock range = Ui.Text("", 13);
    private readonly StackPanel body = new();
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly SmoothScroll smooth;
    private readonly Button forward = new() { Content = "下一期 →" };
    internal ReportPanel(int period)
    {
        this.period = period; Margin = new Thickness(26, 0, 26, 22);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var back = new Button { Content = "← 上一期", Margin = new Thickness(0, 0, 8, 0) };
        var current = new Button { Content = "回到本期", Margin = new Thickness(8, 0, 0, 0) };
        controls.Children.Add(back); controls.Children.Add(forward); controls.Children.Add(current); DockPanel.SetDock(controls, Dock.Right); toolbar.Children.Add(controls);
        range.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(range); DockPanel.SetDock(toolbar, Dock.Top); Children.Add(toolbar);
        body.Margin = new Thickness(24); scroll.Content = body; Children.Add(Ui.Card(scroll, "#121C27", 0)); smooth = new SmoothScroll(scroll);
        back.Click += (_, _) => { Shift(-1); Render(); }; forward.Click += (_, _) => { Shift(1); Render(); };
        current.Click += (_, _) => { anchor = NewsService.BeijingDate(DateTimeOffset.UtcNow); Render(); };
        Render();
    }
    private void Shift(int direction) => anchor = period == 3 ? anchor.AddMonths(direction) : anchor.AddDays(direction * (period == 2 ? 7 : 1));
    private void Render()
    {
        smooth.Cancel(); body.Children.Clear();
        var archive = NewsArchive.Read();
        var data = ReportData.Create(archive.Items ?? new(), period, anchor, DateTimeOffset.UtcNow);
        range.Text = $"{new[] { "", "日报", "周报", "月报" }[period]}  ·  " + (period == 1 ? $"{data.Start:yyyy.MM.dd}" : $"{data.Start:yyyy.MM.dd} — {data.End.AddDays(-1):MM.dd}");
        forward.IsEnabled = data.End <= NewsService.BeijingDate(DateTimeOffset.UtcNow);
        var sources = data.Items.Select(n => n.Source.Name).Distinct().Count();
        var overview = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        void Metric(string value, string label) { var line = new StackPanel { Orientation = Orientation.Horizontal }; var number = Ui.Text(value, 25); number.FontWeight = FontWeights.SemiBold; line.Children.Add(number); var caption = Ui.Text(label, 11, Ui.Muted); caption.VerticalAlignment = VerticalAlignment.Center; caption.Margin = new Thickness(10, 0, 0, 0); line.Children.Add(caption); var card = Ui.Card(line, "#17222E", 12); card.Margin = new Thickness(0, 0, 8, 0); overview.Children.Add(card); }
        Metric(data.Items.Count.ToString(), "本期精选"); Metric(sources.ToString(), "资讯来源"); Metric(data.Items.Select(n => n.CategoryLabel).Distinct().Count().ToString(), "覆盖分类"); body.Children.Add(overview);
        string observed = archive.StartedAt == default ? "尚未建立存档" : $"本地存档始于 {archive.StartedAt.ToOffset(TimeSpan.FromHours(8)):yyyy-MM-dd HH:mm}";
        string coverage = data.Items.Count == 0 ? "本期暂无已存档资讯。" : $"已存档资讯日期：{NewsService.BeijingDate(data.Items.Last().PublishedAt ?? data.Items.Last().DiscoveredAt):MM-dd} 至 {NewsService.BeijingDate(data.Items.First().PublishedAt ?? data.Items.First().DiscoveredAt):MM-dd}。";
        var infoRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; var toggle = new Button { Content = "数据说明 ⌄", Padding = new Thickness(8, 3, 8, 3), FontSize = 10, Background = System.Windows.Media.Brushes.Transparent }; DockPanel.SetDock(toggle, Dock.Right); infoRow.Children.Add(toggle); var warning = Ui.Text("本机存档 · 数据可能不完整 · 北京时间", 11, Ui.Muted); warning.VerticalAlignment = VerticalAlignment.Center; infoRow.Children.Add(warning); body.Children.Add(infoRow);
        var note = Ui.Text(observed + "。" + coverage + "仅汇总本机已获取内容，可能缺失；不代表完整周期或全站数据。按资讯 ID 去重，摘要沿用数据源，不额外生成推测。", 11, Ui.Muted); note.Margin = new Thickness(0, 0, 0, 12); note.Visibility = Visibility.Collapsed; body.Children.Add(note);
        toggle.Click += (_, _) => { bool expand = note.Visibility != Visibility.Visible; note.Visibility = expand ? Visibility.Visible : Visibility.Collapsed; toggle.Content = expand ? "收起说明 ⌃" : "数据说明 ⌄"; };
        if (data.Items.Count > 0)
        {
            var stats = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
            foreach (var group in data.Items.GroupBy(n => n.CategoryLabel).OrderByDescending(g => g.Count())) { var badge = Ui.Badge($"{group.Key}  {group.Count()}"); badge.Margin = new Thickness(0, 0, 8, 8); stats.Children.Add(badge); } body.Children.Add(stats);
            var heading = new DockPanel(); var order = Ui.Text("最新在前", 10, Ui.Muted); order.HorizontalAlignment = HorizontalAlignment.Right; order.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(order, Dock.Right); heading.Children.Add(order); heading.Children.Add(Ui.Text("资讯回顾", 16)); body.Children.Add(heading);
            foreach (var group in data.Items.GroupBy(n => NewsService.BeijingDate(n.PublishedAt ?? n.DiscoveredAt)))
            {
                var date = Ui.Text($"{group.Key:MM月dd日}  ·  {group.Count()} 条", 13, Ui.Mint); date.Margin = new Thickness(0, 20, 0, 10); body.Children.Add(date);
                foreach (var n in group)
                {
                    var content = new StackPanel(); var headline = Ui.Text(n.Title, 15); headline.FontWeight = FontWeights.SemiBold; content.Children.Add(headline);
                    var meta = Ui.Text(n.Source.Name + " · " + n.CategoryLabel, 10, Ui.Muted); meta.Margin = new Thickness(0, 5, 0, 6); content.Children.Add(meta);
                    if (!string.IsNullOrWhiteSpace(n.Summary)) { var summary = Ui.Text(n.Summary, 12, Ui.Muted); summary.MaxHeight = 60; summary.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(summary); }
                    var link = new Button { Content = "阅读原文 ↗", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) }; link.Click += (_, _) => MainWindow.OpenUrl(n.Links.Original); content.Children.Add(link);
                    var card = Ui.Card(content, "#17222E", 16); card.Margin = new Thickness(0, 0, 0, 8); body.Children.Add(card);
                }
            }
        }
        var credit = Ui.Text("资讯回顾与整理｜YuMir", 11, Ui.Muted); credit.Margin = new Thickness(0, 20, 0, 0); body.Children.Add(credit); scroll.ScrollToTop();
    }
}
