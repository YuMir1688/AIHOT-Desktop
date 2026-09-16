using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
namespace AiHot;
public sealed class ReaderWindow : Window
{
    private List<NewsItem> items;
    private readonly StackPanel results = new(), detail = new() { Margin = new Thickness(28, 24, 28, 24) };
    private readonly TextBlock positionLabel = Ui.Text("", 11, Ui.Muted);
    private readonly ScrollViewer listScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 10, 0, 0) };
    private readonly System.Windows.Threading.DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly SmoothScroll smoothList, smoothArticle;
    private readonly TextBox search = new() { Padding = new Thickness(12, 10, 12, 10), ToolTip = "搜索已加载标题、摘要、来源", FontSize = 12 };
    private readonly TextBlock count = Ui.Text("", 11, Ui.Muted), feedStatus = Ui.Text("", 10, Ui.Muted);
    private readonly Dictionary<string, Button> cards = new(), filters = new();
    private string category = "";
    private string? selected;
    private List<NewsItem> visibleItems = new();
    private readonly ScrollViewer articleScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button previous = new() { Content = "← 上一条", ToolTip = "Alt + ↑" }, next = new() { Content = "下一条 →", ToolTip = "Alt + ↓" };
    private readonly StackPanel articleActions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    internal string? SelectedId => selected;
    public ReaderWindow(List<NewsItem> items, string status)
    {
        smoothList = new SmoothScroll(listScroll); smoothArticle = new SmoothScroll(articleScroll);
        this.items = items; Title = "AIHOT · 阅读面板"; Width = 1140; Height = 800; MinWidth = 880; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) }); root.RowDefinitions.Add(new RowDefinition());
        var intro = new Grid { Margin = new Thickness(28, 19, 30, 18), Background = Brushes.Transparent }; Ui.MakeDraggable(intro); intro.ColumnDefinitions.Add(new ColumnDefinition()); intro.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal }; heading.Children.Add(Ui.Text("你的 AI 信息视野", 20)); intro.Children.Add(heading);
        root.Children.Add(intro);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(columns, 1); root.Children.Add(columns);
        var reportHost = new Border { Visibility = Visibility.Collapsed }; Grid.SetRow(reportHost, 1); root.Children.Add(reportHost);
        var sections = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(sections, 1); intro.Children.Add(sections);
        var sectionButtons = new List<Button>();
        for (int section = 0; section < 4; section++)
        {
            int selectedSection = section;
            var sectionButton = new Button { Content = new[] { "资讯", "日报", "周报", "月报" }[section], Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0), Background = section == 0 ? Ui.Brush("#23493F") : Ui.Brush("#1A2633") };
            sectionButton.Click += (_, _) => {
                smoothList.Cancel(); smoothArticle.Cancel();
                columns.Visibility = selectedSection == 0 ? Visibility.Visible : Visibility.Collapsed;
                reportHost.Visibility = selectedSection == 0 ? Visibility.Collapsed : Visibility.Visible;
                if (selectedSection > 0) { NewsArchive.Record(Storage.Read<Cache>("cache.json").Items.Concat(items)); reportHost.Child = new ReportPanel(selectedSection); }
                else reportHost.Child = null;
                for (int j = 0; j < sectionButtons.Count; j++) sectionButtons[j].Background = Ui.Brush(j == selectedSection ? "#23493F" : "#1A2633");
            };
            sectionButtons.Add(sectionButton); sections.Children.Add(sectionButton);
        }
        var left = new DockPanel { Margin = new Thickness(26, 0, 16, 20) }; var header = new StackPanel(); header.Children.Add(Ui.Text("搜索当前资讯  ·  Ctrl+F", 10, Ui.Muted));
        var searchRow = new Grid { Margin = new Thickness(0, 7, 0, 10) }; search.Height = 40; search.VerticalContentAlignment = VerticalAlignment.Center; search.Padding = new Thickness(12, 8, 36, 8); searchRow.Children.Add(search);
        var placeholder = Ui.Text("搜索标题、来源或关键词…", 12, Ui.Muted); placeholder.Margin = new Thickness(13, 0, 32, 0); placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.IsHitTestVisible = false; searchRow.Children.Add(placeholder);
        var clear = new Button { Content = "×", Width = 26, Height = 26, Padding = new Thickness(0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 7, 0), ToolTip = "清空搜索" }; clear.Click += (_, _) => { search.Clear(); search.Focus(); }; searchRow.Children.Add(clear); header.Children.Add(searchRow);
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (key, label) in new[] { ("", "全部"), ("ai-models", "模型"), ("ai-products", "产品"), ("industry", "行业"), ("paper", "论文"), ("tip", "技巧") })
        {
            var tab = new Button { Content = label, FontSize = 11, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 4, 5) }; tab.Click += (_, _) => { searchDelay.Stop(); category = key; Filter(); listScroll.ScrollToTop(); }; filters[key] = tab; tabs.Children.Add(tab);
        }
        header.Children.Add(tabs); header.Children.Add(count); DockPanel.SetDock(header, Dock.Top); left.Children.Add(header);
        listScroll.Content = results; left.Children.Add(listScroll); columns.Children.Add(left);
        var reading = new DockPanel();
        var navigation = new Grid { Margin = new Thickness(16, 12, 16, 12) }; navigation.ColumnDefinitions.Add(new ColumnDefinition()); navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var paging = new StackPanel { Orientation = Orientation.Horizontal }; previous.Height = next.Height = 36; previous.Padding = next.Padding = new Thickness(10, 6, 10, 6); positionLabel.VerticalAlignment = VerticalAlignment.Center; positionLabel.Margin = new Thickness(12, 0, 12, 0); paging.Children.Add(previous); paging.Children.Add(positionLabel); paging.Children.Add(next); navigation.Children.Add(paging); Grid.SetColumn(articleActions, 1); navigation.Children.Add(articleActions);
        var footer = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = navigation }; DockPanel.SetDock(footer, Dock.Bottom); reading.Children.Add(footer);
        articleScroll.Content = detail; reading.Children.Add(articleScroll);
        var right = Ui.Card(reading, "#121C27", 0); right.Margin = new Thickness(0, 0, 26, 22); Grid.SetColumn(right, 1); columns.Children.Add(right); Ui.Shell(this, "YuMir的阅读室", root);
        previous.Click += (_, _) => MoveSelection(-1); next.Click += (_, _) => MoveSelection(1);
        void SearchChanged() { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; clear.Visibility = search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; searchDelay.Stop(); searchDelay.Start(); }
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); Filter(); listScroll.ScrollToTop(); };
        Closed += (_, _) => searchDelay.Stop(); search.TextChanged += (_, _) => SearchChanged(); SearchChanged(); searchDelay.Stop(); Filter();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Focus(); search.SelectAll(); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey is Key.Up or Key.Down) { MoveSelection(e.SystemKey == Key.Up ? -1 : 1); e.Handled = true; }
        };
    }
    internal void MoveSelection(int delta)
    {
        int i = visibleItems.FindIndex(n => n.Id == selected);
        if (i < 0 || i + delta < 0 || i + delta >= visibleItems.Count) return;
        Select(visibleItems[i + delta].Id);
    }
    public void UpdateItems(List<NewsItem> updated) { double offset = listScroll.VerticalOffset, articleOffset = articleScroll.VerticalOffset; string? oldSelection = selected; items = updated; Filter(); UpdateLayout(); listScroll.ScrollToVerticalOffset(offset); if (selected == oldSelection) articleScroll.ScrollToVerticalOffset(articleOffset); }
    public void UpdateStatus(string text) => feedStatus.Text = text;
    private void Filter()
    {
        smoothList.Cancel(); smoothArticle.Cancel();
        results.Children.Clear(); cards.Clear(); string q = search.Text.Trim();
        var filtered = items.Where(n => (category == "" || n.Category == category) && (n.Title + " " + n.Summary + " " + n.Source.Name).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList(); count.Text = filtered.Count == items.Count ? $"全部资讯 · {items.Count} 条" : $"找到 {filtered.Count} 条 · 共 {items.Count} 条";
        visibleItems = filtered;
        foreach (var pair in filters) { pair.Value.Background = Ui.Brush(pair.Key == category ? "#23493F" : "#1A2633"); pair.Value.Foreground = pair.Key == category ? Ui.Mint : Ui.Muted; }
        foreach (var n in filtered)
        {
            var stack = new StackPanel(); var meta = new Grid(); meta.ColumnDefinitions.Add(new ColumnDefinition()); meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            meta.Children.Add(Ui.Text(n.CategoryLabel, 10, Ui.Mint)); var date = Ui.Text((n.PublishedAt ?? n.DiscoveredAt).ToLocalTime().ToString("MM-dd HH:mm"), 9, Ui.Muted); Grid.SetColumn(date, 1); meta.Children.Add(date); stack.Children.Add(meta);
            var title = Ui.Text(n.Title, 13); title.FontWeight = FontWeights.Medium; title.LineHeight = 21; title.MaxHeight = 42; title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(0, 5, 0, 5); stack.Children.Add(title);
            var source = Ui.Text(n.Source.Name, 10, Ui.Muted); source.TextWrapping = TextWrapping.NoWrap; source.TextTrimming = TextTrimming.CharacterEllipsis; stack.Children.Add(source);
            var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 8, 6), BorderThickness = new Thickness(1) }; button.Click += (_, _) => Select(n.Id); cards[n.Id] = button; results.Children.Add(button);
        }
        if (filtered.Count == 0) { selected = null; positionLabel.Text = "0 / 0"; articleActions.Children.Clear(); previous.IsEnabled = next.IsEnabled = false; detail.Children.Clear(); detail.Children.Add(Ui.Text("暂时没有匹配的资讯", 24)); detail.Children.Add(Ui.Text("试试其他关键词或分类。", 14, Ui.Muted)); var reset = new Button { Content = "清除筛选", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) }; reset.Click += (_, _) => { category = ""; search.Clear(); searchDelay.Stop(); Filter(); }; detail.Children.Add(reset); }
        else Select(filtered.Any(n => n.Id == selected) ? selected! : filtered[0].Id);
    }
    public void Select(string id)
    {
        smoothArticle.Cancel();
        var n = items.FirstOrDefault(x => x.Id == id); if (n == null) return; bool changed = selected != id; selected = id;
        int position = visibleItems.FindIndex(x => x.Id == id); previous.IsEnabled = position > 0; next.IsEnabled = position >= 0 && position < visibleItems.Count - 1;
        positionLabel.Text = $"{position + 1} / {visibleItems.Count}";
        foreach (var pair in cards) { pair.Value.Background = Ui.Brush(pair.Key == id ? "#1C3538" : "#151F2C"); pair.Value.BorderBrush = Ui.Brush(pair.Key == id ? "#538F82" : "#263342"); }
        detail.Children.Clear(); detail.Children.Add(Ui.Badge(n.CategoryLabel + "  /  精选"));
        var title = Ui.Text(n.Title, 24); title.FontWeight = FontWeights.SemiBold; title.LineHeight = 35; title.Margin = new Thickness(0, 14, 0, 12); detail.Children.Add(title);
        detail.Children.Add(Ui.Text(n.Source.Name + "  ·  " + (n.PublishedAt ?? n.DiscoveredAt).ToLocalTime().ToString("MM-dd HH:mm"), 11, Ui.Muted)); detail.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 24, 0, 24) }); detail.Children.Add(Ui.Text("内容速览", 12, Ui.Muted));
        var summary = Ui.Text(string.IsNullOrWhiteSpace(n.Summary) ? "此条暂无摘要，可打开原文阅读。" : n.Summary, 15, Ui.Brush("#C7D2DE")); summary.LineHeight = 29; summary.Margin = new Thickness(0, 12, 0, 24); detail.Children.Add(summary);
        if (!string.IsNullOrWhiteSpace(n.Reason))
        {
            var reason = new StackPanel(); reason.Children.Add(Ui.Text("为什么值得关注", 12, Ui.Mint)); var text = Ui.Text(n.Reason, 13, Ui.Brush("#B6C9C8")); text.Margin = new Thickness(0, 9, 0, 0); reason.Children.Add(text);
            var box = Ui.Card(reason, "#162B2C", 18); box.BorderBrush = Ui.Brush("#2A4845"); box.Margin = new Thickness(0, 0, 0, 24); detail.Children.Add(box);
        }
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        var share = new Button { Content = "分享", Padding = new Thickness(15, 11, 15, 11), Margin = new Thickness(0, 0, 8, 0) }; share.Click += (_, _) => { new ShareWindow(n) { Owner = this }.ShowDialog(); }; links.Children.Add(share);
        var original = new Button { Content = "阅读原文  ↗", Foreground = Ui.Brush("#103B2E"), FontWeight = FontWeights.SemiBold, Background = Ui.Mint, Padding = new Thickness(20, 11, 20, 11), Margin = new Thickness(0, 0, 10, 0) }; original.Click += (_, _) => MainWindow.OpenUrl(n.Links.Original); links.Children.Add(original);
        original.Margin = new Thickness(0); articleActions.Children.Clear(); articleActions.Children.Add(links);
        detail.Children.Add(Ui.Text("资讯整理 · AIHOT", 10, Ui.Muted)); var credit = Ui.Text("桌面体验策划｜YuMir", 10, Ui.Brush("#637889")); credit.Margin = new Thickness(0, 6, 0, 0); detail.Children.Add(credit);
        if (changed) { articleScroll.ScrollToTop(); if (cards.TryGetValue(id, out var card)) card.BringIntoView(); }
    }
}
