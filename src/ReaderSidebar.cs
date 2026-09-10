using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AiHot;
namespace YuMir.Cards;
public static class ReaderSidebar
{
    private static ScrollBar? FindBar(DependencyObject root)
    {
        if (root is ScrollBar bar && bar.Orientation == Orientation.Vertical) return bar;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = FindBar(VisualTreeHelper.GetChild(root, i)); if (found != null) return found; }
        return null;
    }
    private static void RoundThumb(ScrollBar bar)
    {
        bar.Width = 10;
        ScrollbarAppearance.Apply(bar);
    }
    public static void Apply(object? reader)
    {
        if (reader == null) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        object Get(string name) => reader.GetType().GetField(name, flags)!.GetValue(reader)!;
        var scroll = (ScrollViewer)Get("listScroll");
        if (scroll.Parent is not DockPanel sidebar || sidebar.Parent is not Grid columns) return;
        var position = (TextBlock)Get("positionLabel");
        position.Width = 76; position.TextAlignment = TextAlignment.Center;
        position.Margin = new Thickness(8,0,8,0);
        foreach (var name in new[] { "previous", "next" }) {
            var navigation = (Button)Get(name); navigation.Width = 84; navigation.MinWidth = 84; navigation.MaxWidth = 84;
        }
        var count = (TextBlock)Get("count");
        if (count.Text.StartsWith("全部资讯")) count.Text = "今日精选 TOP100";
        var results = (StackPanel)Get("results");
        var selected = Get("selected") as string;
        var cards = (Dictionary<string, Button>)Get("cards");
        if (!Equals(sidebar.Tag, "AlignedReaderSidebar"))
        {
            if (columns.Parent is Grid readerBody)
            foreach (var row in readerBody.Children.OfType<Grid>())
            foreach (var heading in row.Children.OfType<StackPanel>())
            foreach (var title in heading.Children.OfType<TextBlock>().Where(t => t.Text == "你的 AI 信息视野"))
            {
                title.FontFamily = new FontFamily("Microsoft YaHei");
                title.FontSize = 18;
                title.FontWeight = FontWeights.Medium;
                title.LineHeight = 28;
                title.TextWrapping = TextWrapping.NoWrap;
                title.VerticalAlignment = VerticalAlignment.Center;
            }
            sidebar.Tag = "AlignedReaderSidebar";
            sidebar.Margin = new Thickness(24, 0, 16, 24);
            sidebar.Resources.MergedDictionaries.Add(PickerStyles.Create());
            columns.ColumnDefinitions[0].Width = new GridLength(380);
            columns.ColumnDefinitions[0].MinWidth = 360;
            columns.ColumnDefinitions[0].MaxWidth = 540;
            columns.ColumnDefinitions[1].MinWidth = 430;
            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.CurrentAndNext, ToolTip = "拖动调整资讯列表宽度", Margin = new Thickness(0,0,0,24) };
            columns.Children.Add(splitter);
            var search = (TextBox)Get("search"); search.Height = 42; search.Background = ReportSharing.Brush("#182431"); search.Foreground = ReportSharing.Brush("#DCE7EF");
            if (sidebar.Children[0] is StackPanel header)
            {
                if (header.Children[0] is TextBlock label) label.Visibility = Visibility.Collapsed;
                if (search.Parent is Grid searchGrid) {
                    searchGrid.Margin = new Thickness(0,0,0,10);
                    foreach (var hint in searchGrid.Children.OfType<TextBlock>()) hint.Text = "搜索标题、来源或关键词…  Ctrl+F";
                }
                var wrap = header.Children.OfType<WrapPanel>().FirstOrDefault();
                if (wrap != null)
                {
                    int index = header.Children.IndexOf(wrap);
                    var categories = new UniformGrid { Columns = 6, Margin = new Thickness(-2,0,-2,12) };
                    foreach (var button in wrap.Children.OfType<Button>().ToArray()) { wrap.Children.Remove(button); button.Margin = new Thickness(2,0,2,0); button.Padding = new Thickness(0,6,0,6); button.MinHeight = 32; categories.Children.Add(button); }
                    header.Children.Remove(wrap); header.Children.Insert(index, categories);
                }
            }
            scroll.Margin = new Thickness(0,10,0,0);
            scroll.Padding = new Thickness(0); scroll.BorderThickness = new Thickness(0);
            scroll.LayoutUpdated += (_, _) => {
                var bar = FindBar(scroll);
                if (bar != null) RoundThumb(bar);
                double gutter = bar?.Visibility == Visibility.Visible ? bar.ActualWidth : 0;
                if (Math.Abs(scroll.Margin.Right + gutter) > .1) scroll.Margin = new Thickness(0,10,-gutter,0);
            };
        }
        foreach (var pair in cards)
        {
            var button = pair.Value; bool active = pair.Key == selected;
            button.Background = ReportSharing.Brush(active ? "#1C3538" : "#141F2A");
            button.BorderBrush = ReportSharing.Brush(active ? "#73E5C1" : "#263442");
            button.BorderThickness = new Thickness(active ? 3 : 1, 1, 1, 1);
            button.Padding = new Thickness(active ? 12 : 14, 12, 14, 12);
            button.Margin = new Thickness(0,0,0,8);
            if (button.Content is not StackPanel content || content.Children.Count < 3) continue;
            if (content.Children[1] is TextBlock title) { title.FontSize = 14; title.LineHeight = 22; title.MaxHeight = 66; title.Margin = new Thickness(0,6,0,6); title.ToolTip = title.Text; }
            if (content.Children[2] is TextBlock source) { source.MaxHeight = 18; source.ToolTip = source.Text; }
        }
    }
}
