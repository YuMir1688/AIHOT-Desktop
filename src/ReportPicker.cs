using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AiHot;
namespace YuMir.Cards;

public sealed class ReportPicker : Window
{
    public sealed class Choice(NewsItem item, bool selected) { public NewsItem Item = item; public bool Selected = selected; }
    public List<Choice> Choices { get; }
    public bool Accepted { get; private set; }
    private readonly TextBox search = new() { Padding = new Thickness(12), Margin = new Thickness(0, 10, 10, 10), MinWidth = 280, Height = 42 };
    private readonly ComboBox category = new() { MinWidth = 150, Height = 42, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
    private readonly StackPanel rows = new();
    private readonly TextBlock count = Text("", 13);
    private readonly TextBlock hint = Text("点击整行勾选；切换到「已选」可拖动左侧手柄排序。", 12);
    private readonly Button allTab = Button("全部资讯"), chosenTab = Button("已选资讯");
    private readonly ScrollViewer viewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Dictionary<Choice, Button> cachedRows = new();
    private bool chosen;
    private Point dragStart;
    private static Brush Color(string s) => ReportSharing.Brush(s);
    private static TextBlock Text(string s, double size) => new() { Text = s, FontSize = size, Foreground = Color("#DCE7EF"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static Button Button(string s) => new() { Content = s, Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 8, 0) };
    public ReportPicker(IEnumerable<Choice> choices)
    {

        Choices = choices.Select(c => new Choice(c.Item, c.Selected)).ToList();
        Title = "挑选分享资讯"; Width = 1020; Height = 820; MinWidth = 760; MinHeight = 580;
        var body = new DockPanel { Margin = new Thickness(24) }; body.Resources.MergedDictionaries.Add(PickerStyles.Create());
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        top.Children.Add(Text("挑选分享资讯", 24));
        hint.Margin = new Thickness(0, 6, 0, 12); top.Children.Add(hint);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal }; tabs.Children.Add(allTab); tabs.Children.Add(chosenTab); top.Children.Add(tabs);
        var filterLabel = Text("搜索资讯", 12); filterLabel.Margin = new Thickness(0); search.ToolTip = "搜索标题、来源或摘要";
        var filters = new DockPanel(); DockPanel.SetDock(category, Dock.Right); filters.Children.Add(category); var searchBox = new Grid(); searchBox.Children.Add(search); var placeholder = Text("搜索标题、来源或摘要", 12); placeholder.Margin = new Thickness(13, 0, 16, 0); placeholder.Foreground = Color("#8FA5B8"); placeholder.IsHitTestVisible = false; searchBox.Children.Add(placeholder); search.TextChanged += (_, _) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; filters.Children.Add(searchBox); top.Children.Add(filters);
        search.ToolTip = "搜索标题、来源或摘要"; System.Windows.Automation.AutomationProperties.SetName(search, "搜索标题、来源或摘要");
        category.Items.Add("全部分类"); foreach (var name in Choices.Select(c => c.Item.CategoryLabel).Distinct()) category.Items.Add(name); category.SelectedIndex = 0;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var select = Button("选中当前结果"); var clear = Button("取消当前结果"); var reset = Button("重置筛选");
        actions.Children.Add(select); actions.Children.Add(clear); actions.Children.Add(reset); top.Children.Add(actions);
        select.Click += (_, _) => Bulk(true); clear.Click += (_, _) => Bulk(false); reset.Click += (_, _) => { search.Clear(); category.SelectedIndex = 0; };
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var done = Button("完成挑选"); done.Background = Color("#73E5C1"); done.Foreground = Color("#103B2E");
        var cancel = Button("取消"); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(cancel); buttons.Children.Add(done); DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(count);
        done.Click += (_, _) => { Accepted = true; Close(); }; cancel.Click += (_, _) => Close();
        viewport.Content = rows; body.Children.Add(viewport);
        ReportSharing.Shell(this, body);
        search.Background = category.Background = Color("#202D3B"); search.Foreground = Color("#EBF1F6"); category.Foreground = Color("#DCE7EF");
        search.TextChanged += (_, _) => Render(); category.SelectionChanged += (_, _) => Render();
        allTab.Click += (_, _) => { chosen = false; Render(); }; chosenTab.Click += (_, _) => { chosen = true; Render(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Render();
    }
    public IEnumerable<Choice> VisibleChoices() => Choices.Where(c => (!chosen || c.Selected) && (category.SelectedIndex <= 0 || c.Item.CategoryLabel == category.SelectedItem?.ToString()) && (string.IsNullOrWhiteSpace(search.Text) || $"{c.Item.Title} {c.Item.Source.Name} {c.Item.Summary}".Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)));
    private void Bulk(bool value) { foreach (var c in VisibleChoices().ToArray()) c.Selected = value; Render(); }
    private void Counts()
    {
        count.Text = $"已选 {Choices.Count(c => c.Selected)} / {Choices.Count} 条 · 当前显示 {VisibleChoices().Count()} 条";
        chosenTab.Content = $"已选资讯 ({Choices.Count(c => c.Selected)})";
    }
    public void Reorder(Choice from, Choice to)
    {
        if (from == to) return;
        int index = Choices.IndexOf(to); Choices.Remove(from); Choices.Insert(index, from); Render();
    }
    private void Render()
    {
        double offset = viewport.VerticalOffset; Counts();
        foreach (var old in rows.Children.OfType<Button>()) old.Visibility = Visibility.Collapsed;
        foreach (var empty in rows.Children.OfType<TextBlock>().ToArray()) rows.Children.Remove(empty);
        allTab.Background = Color(chosen ? "#202D3B" : "#23493F"); chosenTab.Background = Color(chosen ? "#23493F" : "#202D3B");
        foreach (var c in VisibleChoices().ToArray())
        {
            if (cachedRows.TryGetValue(c, out var existing)) { existing.Visibility = Visibility.Visible; ((Action)existing.Tag)(); continue; }
            var content = new DockPanel();
            var mark = Text(c.Selected ? "✓" : "", 15); mark.HorizontalAlignment = HorizontalAlignment.Center; mark.Foreground = Color("#103B2E");
            var checkFrame = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Color(c.Selected ? "#73E5C1" : "#698093"), Background = c.Selected ? Color("#73E5C1") : Brushes.Transparent, Child = mark, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(checkFrame, Dock.Left); content.Children.Add(checkFrame);
            TextBlock? gripControl = null;
            {
                var grip = Text("⠿", 24); gripControl = grip; grip.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed; grip.Width = 32; grip.Cursor = Cursors.SizeAll; grip.ToolTip = "拖动排序";
                DockPanel.SetDock(grip, Dock.Left); content.Children.Add(grip);
                grip.PreviewMouseLeftButtonDown += (_, e) => { dragStart = e.GetPosition(grip); e.Handled = true; };
                grip.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(grip) - dragStart).Length > 5) { DragDrop.DoDragDrop(grip, new DataObject(typeof(Choice), c), DragDropEffects.Move); e.Handled = true; } };
            }
            var words = new StackPanel(); var title = Text(c.Item.Title, 15); title.LineHeight = 23; words.Children.Add(title);
            var meta = Text(c.Item.CategoryLabel + " · " + c.Item.Source.Name, 11); meta.Foreground = Color("#8FA5B8"); meta.Margin = new Thickness(0, 7, 0, 0); words.Children.Add(meta); content.Children.Add(words);
            var row = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 6, 6), Background = Color(c.Selected ? "#1D3637" : "#182431"), BorderBrush = Color(c.Selected ? "#365E58" : "#293A4A"), BorderThickness = new Thickness(1), AllowDrop = chosen };
            System.Windows.Automation.AutomationProperties.SetName(row, (c.Selected ? "已选：" : "未选：") + c.Item.Title);
            Action paint = () => {
                mark.Text = c.Selected ? "✓" : "";
                checkFrame.Background = c.Selected ? Color("#73E5C1") : Brushes.Transparent;
                checkFrame.BorderBrush = Color(c.Selected ? "#73E5C1" : "#698093");
                row.Background = Color(c.Selected ? "#1B3033" : "#182431");
                row.BorderBrush = Color(c.Selected ? "#365E58" : "#293A4A");
                row.AllowDrop = chosen; if (gripControl != null) gripControl.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
                System.Windows.Automation.AutomationProperties.SetName(row, (c.Selected ? "已选：" : "未选：") + c.Item.Title);
            };
            row.Tag = paint; cachedRows[c] = row; paint();
            row.Click += (_, _) => { c.Selected = !c.Selected; paint(); if (chosen) Render(); else Counts(); };
            row.DragEnter += (_, e) => { if (e.Data.GetDataPresent(typeof(Choice))) row.BorderBrush = Color("#73E5C1"); };
            row.DragLeave += (_, _) => paint();            row.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(Choice)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            row.Drop += (_, e) => { if (e.Data.GetData(typeof(Choice)) is Choice from && Choices.Contains(from)) Reorder(from, c); e.Handled = true; };
            rows.Children.Add(row);
        }
        var ordered = Choices.Where(c => cachedRows.ContainsKey(c)).Select(c => cachedRows[c]).ToArray();
        for (int i = 0; i < ordered.Length; i++) { if (rows.Children.IndexOf(ordered[i]) != i) { rows.Children.Remove(ordered[i]); rows.Children.Insert(i, ordered[i]); } }
        viewport.UpdateLayout(); viewport.ScrollToVerticalOffset(offset);
        if (!VisibleChoices().Any()) rows.Children.Add(Text(chosen ? "这里还没有符合条件的已选资讯。可切换到全部资讯继续挑选。" : "没有找到匹配资讯，试试其他关键词或重置筛选。", 15));
    }
}
