using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;
using AiHot;
using Microsoft.Win32;

namespace YuMir.Cards;

public static class ReportSharing
{
    private sealed class State { public ReportSnapshot Snapshot = null!; public Button Button = null!; }
    private static readonly ConditionalWeakTable<DockPanel, State> States = new();
    // Called at the end of the original report's Render method, using its exact computed period and items.
    public static void Update(DockPanel panel, int period, DateTime start, DateTime end, List<NewsItem> items, DateTimeOffset startedAt)
    {
        if (!States.TryGetValue(panel, out var state))
        {
            state = new State(); States.Add(panel, state);
            var button = new Button { Content = "分享报告", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(12, 0, 0, 0),
                ToolTip = "制作本期报告封面与资讯卡片" };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "ShareReport");
            state.Button = button;
            var header = panel.Children.OfType<DockPanel>().First();
            var actions = header.Children.OfType<StackPanel>().First();
            actions.Children.Add(button);
            var current = state;
            button.Click += (_, _) =>
            {
                try
                {
                    var window = new ReportShareWindow(current.Snapshot) { Owner = Window.GetWindow(panel), WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    window.Show();
                }
                catch (Exception ex) { MessageBox.Show(Window.GetWindow(panel), "暂时无法打开分享窗口：" + ex.Message, "分享报告"); }
            };
        }
        state.Snapshot = new ReportSnapshot(period, start, end, startedAt, items.Select(ReportSnapshot.Copy).ToArray());
        state.Button.ToolTip = $"分享{state.Snapshot.Name} · {state.Snapshot.Range}";
    }
    internal static Brush Brush(string hex)
    {
        var method = typeof(NewsItem).Assembly.GetType("AiHot.Theme")?.GetMethod("Brush", BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, [hex]) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
    internal static void Shell(Window window, UIElement body)
    {
        typeof(NewsItem).Assembly.GetType("AiHot.Ui")!.GetMethod("Shell", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [window, "分享报告", body]);
    }
}

public sealed class ReportShareWindow : Window
{
    private sealed class Entry(NewsItem item, bool selected) { public NewsItem Item = item; public bool Selected = selected; }
    private readonly ReportSnapshot snapshot;
    private readonly List<Entry> entries;
    private readonly Image preview = new() { Stretch = Stretch.Uniform, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ListBox longPreview = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private sealed class LongPart(ReportDocument doc, int index) { public System.Windows.Media.Imaging.BitmapSource Source => ReportCards.RenderLongSection(doc, index); }
    private readonly Button longMode = ActionButton("一张长图"), cardsMode = ActionButton("多张卡片");
    private bool isLong = true;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly StackPanel navigation = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly WrapPanel cardActions = new();
    private readonly Expander selection = new() { IsExpanded = false, Foreground = ReportSharing.Brush("#EBF1F6") };
    private readonly TextBlock pageLabel = Label("正在生成封面…", 12);
    private readonly TextBlock status = Label("", 11);
    private readonly TextBlock selectionLabel = Label("", 12);
    private readonly TextBox lead = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 91, Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ListBox list = new() { Height = 242, HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderThickness = new Thickness(0) };
    private readonly UniformGrid styles = new() { Columns = 5 };
    private readonly List<Button> styleButtons = new();
    private int selectedStyle;
    private readonly Button previous = ActionButton("← 上一张"), next = ActionButton("下一张 →");
    private readonly Button copyCover = ActionButton("复制封面"), copyPage = ActionButton("复制当前页"), save = ActionButton("保存当前页"), export = ActionButton("导出整组 PNG");
    private readonly Button cancel = ActionButton("取消导出");
    private readonly Button selectAll = ActionButton("全选");
    private readonly Button saveLong = ActionButton("保存长图");
    private bool updatingSelection;
    private readonly StackPanel editing = new();
    private ReportDocument? document;
    private int page;
    private bool initializing = true;
    private CancellationTokenSource? exporting;
    public ReportShareWindow(ReportSnapshot snapshot)
    {
        this.snapshot = snapshot;
        entries = snapshot.Items.Select(item => new Entry(ReportSnapshot.Copy(item), true)).ToList();
        lead.Background = list.Background = ReportSharing.Brush("#182431");
        lead.Foreground = list.Foreground = ReportSharing.Brush("#EBF1F6");
        lead.BorderBrush = ReportSharing.Brush("#304053");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        for (int i = 0; i < EditorialCard.Names.Length; i++)
        {
            int choice = i;
            var content = new StackPanel();
            content.Children.Add(new Border { Height = 7, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(EditorialCard.GetPalette(i).Accent)), Margin = new Thickness(3, 2, 3, 7) });
            var name = Label(EditorialCard.Names[i], 10); name.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(name);
            var button = new Button { Content = content, Padding = new Thickness(3, 5, 3, 2), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1) };
            System.Windows.Automation.AutomationProperties.SetName(button, EditorialCard.Names[i]);
            button.Click += (_, _) => { selectedStyle = choice; PaintStyles(); Dirty(); };
            styleButtons.Add(button); styles.Children.Add(button);
        }
        PaintStyles();
        Title = "分享报告 · " + snapshot.Name; Width = 1180; Height = 910; MinWidth = 920; MinHeight = 660;
        var root = new Grid { Margin = new Thickness(22) };
        root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(405) });
        var canvas = new DockPanel { Margin = new Thickness(0, 0, 20, 0), LastChildFill = true };
        navigation.Margin = new Thickness(0, 12, 0, 0);
        navigation.Children.Add(previous); navigation.Children.Add(next);
        DockPanel.SetDock(navigation, Dock.Bottom); canvas.Children.Add(navigation);
        pageLabel.Margin = new Thickness(0, 0, 0, 12); DockPanel.SetDock(pageLabel, Dock.Top); canvas.Children.Add(pageLabel);
        var imageFactory = new FrameworkElementFactory(typeof(Image));
        imageFactory.SetBinding(Image.SourceProperty, new Binding("Source"));
        imageFactory.SetValue(Image.StretchProperty, Stretch.Uniform);
        imageFactory.SetBinding(Image.WidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBox), 1), Converter = new PreviewWidth() });
        longPreview.ItemTemplate = new DataTemplate { VisualTree = imageFactory };
        var containerStyle = new Style(typeof(ListBoxItem));
        containerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        containerStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        containerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        containerStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = contentFactory }));
        longPreview.ItemContainerStyle = containerStyle;
        VirtualizingPanel.SetIsVirtualizing(longPreview, true); VirtualizingPanel.SetVirtualizationMode(longPreview, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(longPreview, ScrollUnit.Pixel);
        ScrollViewer.SetHorizontalScrollBarVisibility(longPreview, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(longPreview, ScrollBarVisibility.Auto);
        var images = new Grid(); images.Children.Add(preview); images.Children.Add(longPreview); canvas.Children.Add(images);
        root.Children.Add(new Border { Background = ReportSharing.Brush("#151E2A"), Padding = new Thickness(16), CornerRadius = new CornerRadius(14), Child = canvas, Margin = new Thickness(0, 0, 20, 0) });
        var controls = new StackPanel();
        controls.Children.Add(Label("分享这期 " + snapshot.Name, 24, true));
        controls.Children.Add(Label(snapshot.Range, 12));
        controls.Children.Add(Label($"默认包含整期 {entries.Count} 条资讯，可按需挑选。", 11));
        controls.Children.Add(editing);
        var modes = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 12) };
        modes.Children.Add(longMode); modes.Children.Add(cardsMode); editing.Children.Add(modes);
        longMode.Click += (_, _) => SetMode(true); cardsMode.Click += (_, _) => SetMode(false);
        var intro = new Expander { Foreground = ReportSharing.Brush("#EBF1F6"), Header = "添加导读（可选，最多 180 字）", Content = lead, Margin = new Thickness(0, 12, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(lead, "ReportLead");
        editing.Children.Add(Section("选择版式")); editing.Children.Add(styles);
        editing.Children.Add(intro);
        var adjust = ActionButton("调整分享资讯");
        adjust.Click += (_, _) => {
            var picker = new ReportPicker(entries.Select(e => new ReportPicker.Choice(e.Item, e.Selected))) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            picker.ShowDialog();
            if (!picker.Accepted) return;
            entries.Clear(); entries.AddRange(picker.Choices.Select(c => new Entry(c.Item, c.Selected)));
            Counts(); Dirty();
        };
        selectionLabel.Margin = new Thickness(0, 18, 0, 4);
        editing.Children.Add(selectionLabel); editing.Children.Add(adjust);        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        cardActions.Children.Add(copyCover); cardActions.Children.Add(copyPage); cardActions.Children.Add(save); footer.Children.Add(cardActions);
        saveLong.ToolTip = "将勾选资讯连续排成一张长图；点全选可包含整期内容。";
        saveLong.Background = ReportSharing.Brush("#73E5C1"); saveLong.Foreground = ReportSharing.Brush("#103B2E");
        saveLong.FontWeight = FontWeights.SemiBold; saveLong.Height = 46; footer.Children.Add(saveLong);
        export.Background = ReportSharing.Brush("#73E5C1"); export.Foreground = ReportSharing.Brush("#103B2E");
        export.Content = "导出全部卡片"; export.Height = 46;
        export.FontWeight = FontWeights.SemiBold; export.Margin = new Thickness(0, 8, 0, 0); footer.Children.Add(export);
        cancel.Visibility = Visibility.Collapsed; footer.Children.Add(cancel);
        status.MaxHeight = 66; status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status);
        var scroller = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var right = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); right.Children.Add(footer); right.Children.Add(scroller);
        Grid.SetColumn(right, 1); root.Children.Add(right);
        ReportSharing.Shell(this, root);
        Counts();
        lead.TextChanged += (_, _) => Dirty();
        previous.Click += (_, _) => { if (document != null && page > 0) { page--; ShowPage(); } };
        next.Click += (_, _) => { if (document != null && page + 1 < document.Pages.Count) { page++; ShowPage(); } };
        refresh.Tick += (_, _) => Generate();
        copyCover.Click += (_, _) => Copy(0); copyPage.Click += (_, _) => Copy(page);
        save.Click += (_, _) => SavePage(); export.Click += async (_, _) => await Export();
        saveLong.Click += async (_, _) => await SaveLong();
        cancel.Click += (_, _) => exporting?.Cancel();
        Closing += (_, e) => { if (exporting != null) { exporting.Cancel(); e.Cancel = true; status.Text = "正在取消导出，完成后即可关闭。"; } };
        Closed += (_, _) => { refresh.Stop(); longPreview.ItemsSource = null; preview.Source = null; };
        initializing = false; SetMode(true);
    }
    private void BuildList()
    {
        int current = list.SelectedIndex; list.Items.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var check = new CheckBox { IsChecked = entry.Selected, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 8, 0) };
            var row = new DockPanel { Margin = new Thickness(3, 5, 3, 5) }; DockPanel.SetDock(check, Dock.Left); row.Children.Add(check);
            var text = Label(entry.Item.Title, 12); text.MaxHeight = 50; text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.ToolTip = entry.Item.Title + "\n" + entry.Item.Source.Name;
            row.Children.Add(text);
            var item = new ListBoxItem { Content = row, Tag = entry, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            check.Checked += (_, _) => { entry.Selected = true; if (!updatingSelection) { list.SelectedItem = item; Dirty(); } };
            check.Unchecked += (_, _) => { entry.Selected = false; if (!updatingSelection) { list.SelectedItem = item; Dirty(); } };
            System.Windows.Automation.AutomationProperties.SetName(check, entry.Item.Title);
            list.Items.Add(item);
        }
        list.SelectedIndex = current < entries.Count ? current : -1; Counts();
    }
    private void SelectAll(bool value)
    {
        updatingSelection = true;
        try
        {
            foreach (var entry in entries) entry.Selected = value;
            foreach (ListBoxItem row in list.Items)
                ((CheckBox)((DockPanel)row.Content).Children[0]).IsChecked = value;
        }
        finally { updatingSelection = false; }
        Dirty();
    }
    private void Move(int direction)
    {
        int from = list.SelectedIndex, to = from + direction;
        if (from < 0 || to < 0 || to >= entries.Count) return;
        (entries[from], entries[to]) = (entries[to], entries[from]);
        var row = list.Items[from]; list.Items.RemoveAt(from); list.Items.Insert(to, row);
        list.SelectedIndex = to; list.ScrollIntoView(list.SelectedItem); Dirty();
    }
    private void Counts()
    {
        int count = entries.Count(e => e.Selected);
        selectionLabel.Text = $"已选 {count} / {entries.Count} 条" + (count > 0 ? " · 长摘要自动续页" : " · 仅生成概览封面");
        selectAll.Content = entries.Count > 0 && count == entries.Count ? "取消全选" : "全选";
        selectAll.IsEnabled = entries.Count > 0;
        selection.Header = $"挑选资讯 · 已选 {count} / {entries.Count} 条";
    }
    private void Dirty()
    {
        Counts(); if (initializing) return; document = null;
        pageLabel.Text = "正在更新预览…"; status.Text = "更改后自动更新，无需手动刷新。"; Buttons();
        refresh.Stop(); refresh.Start();
    }
    private void Generate()
    {
        refresh.Stop();
        try
        {
            document = new ReportDocument(snapshot, entries.Where(e => e.Selected).Select(e => e.Item), lead.Text, selectedStyle);
            page = 0; ShowPage();
            if (document != null) status.Text = isLong ? $"共 {document.Selected.Count} 条 · 向下滚动查看完整长图" : $"封面 1 张 + 资讯 {document.Pages.Count - 1} 张 · 1080 × 1440";
        }
        catch (Exception ex) { document = null; pageLabel.Text = "生成未完成 · 当前为上次预览"; status.Text = "生成失败：" + ex.Message; }
        Buttons();
    }
    private void ShowPage()
    {
        try
        {
            if (document == null) return;
            if (isLong)
            {
                // Real export strips are rendered on demand by the virtualized scrolling preview.
                _ = ReportCards.RenderLongSection(document, 0);
                longPreview.ItemsSource = Enumerable.Range(0, document.Pages.Count).Select(i => new LongPart(document, i)).ToArray();
                pageLabel.Text = $"长图预览 · {document.Selected.Count} 条资讯 · 向下滚动";
            }
            else
            {
                preview.Source = document.Render(page);
                pageLabel.Text = $"{(page == 0 ? "概览封面" : "资讯卡片")} · {page + 1} / {document.Pages.Count} · 1080 × 1440";
            }
        }
        catch (Exception ex) { document = null; pageLabel.Text = "生成未完成 · 当前为上次预览"; status.Text = "预览失败：" + ex.Message; }
        Buttons();
    }
    private void Buttons()
    {
        bool ready = document != null && exporting == null;
        copyCover.IsEnabled = copyPage.IsEnabled = save.IsEnabled = export.IsEnabled = saveLong.IsEnabled = ready;
        previous.IsEnabled = ready && page > 0; next.IsEnabled = ready && page + 1 < document!.Pages.Count;
    }
    private void PaintStyles()
    {
        for (int i = 0; i < styleButtons.Count; i++)
        {
            styleButtons[i].BorderBrush = ReportSharing.Brush(i == selectedStyle ? "#73E5C1" : "#304053");
            styleButtons[i].Background = ReportSharing.Brush(i == selectedStyle ? "#23493F" : "#202A38");
        }
    }
    private void SetMode(bool value)
    {
        if (exporting != null) return;
        isLong = value;
        longPreview.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        preview.Visibility = navigation.Visibility = cardActions.Visibility = export.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        saveLong.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        longMode.Background = ReportSharing.Brush(value ? "#23493F" : "#202A38");
        cardsMode.Background = ReportSharing.Brush(value ? "#202A38" : "#23493F");
        Generate();
    }
    private sealed class PreviewWidth : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Math.Max(120, Math.Min(520, (double)value - 24));
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private void Copy(int index)
    {
        try { if (document != null) { Clipboard.SetImage(document.Render(index)); status.Text = index == 0 ? "封面已复制，可粘贴分享。" : "当前页已复制，可粘贴分享。"; } }
        catch (Exception) { status.Text = "剪贴板暂时被占用，请重试或保存 PNG。"; }
    }
    private void SavePage()
    {
        if (document == null) return;
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", DefaultExt = ".png", FileName = $"YuMir-{snapshot.Name}-{snapshot.Start:yyyyMMdd}-{document.PageName(page)}" };
        if (dialog.ShowDialog(this) != true) return;
        try { ReportDocument.Save(document.Render(page), dialog.FileName); status.Text = "已保存：" + dialog.FileName; }
        catch (Exception ex) { status.Text = "保存失败：" + ex.Message; }
    }
    private async Task Export()
    {
        if (document == null) return;
        var dialog = new OpenFolderDialog { Title = "选择保存位置，将自动新建报告文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var snapshotDocument = document;
        exporting = new CancellationTokenSource(); editing.IsEnabled = false; cancel.Visibility = Visibility.Visible; Buttons();
        try
        {
            var folder = await snapshotDocument.ExportAsync(dialog.FolderName, new Progress<int>(n => status.Text = $"正在导出 {n} / {snapshotDocument.Pages.Count} 张…"), exporting.Token);
            status.Text = "已导出整组图片：" + folder;
        }
        catch (OperationCanceledException) { status.Text = "已取消导出，未保留未完成的文件。"; }
        catch (Exception ex) { status.Text = "导出失败：" + ex.Message; }
        finally { exporting.Dispose(); exporting = null; editing.IsEnabled = true; cancel.Visibility = Visibility.Collapsed; Buttons(); }
    }
    private async Task SaveLong()
    {
        if (document == null) return;
        var dialog = new SaveFileDialog { Filter = "PNG 长图|*.png", DefaultExt = ".png", FileName = $"YuMir-{snapshot.Name}-{snapshot.Start:yyyyMMdd}-长图.png" };
        if (dialog.ShowDialog(this) != true) return;
        var current = document;
        exporting = new CancellationTokenSource(); editing.IsEnabled = false; cancel.Visibility = Visibility.Visible; Buttons();
        status.Text = $"正在排版 {current.Selected.Count} 条资讯…";
        try
        {
            var size = await LongReportPng.SaveAsync(current, dialog.FileName,
                new Progress<int>(n => status.Text = $"正在保存长图 {n} / {current.Pages.Count}…"), exporting.Token);
            status.Text = $"已保存长图 · {current.Selected.Count} 条 · {size.Width} × {size.Height}\n{dialog.FileName}";
        }
        catch (OperationCanceledException) { status.Text = "已取消，未覆盖原文件。"; }
        catch (Exception ex) { status.Text = "长图保存失败：" + ex.Message; }
        finally { exporting.Dispose(); exporting = null; editing.IsEnabled = true; cancel.Visibility = Visibility.Collapsed; Buttons(); }
    }
    private static TextBlock Label(string text, double size = 12, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = ReportSharing.Brush("#EBF1F6"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6), LineHeight = size * 1.6 };
    private static TextBlock Section(string text) { var label = Label(text, 12, true); label.Margin = new Thickness(0, 12, 0, 6); return label; }
    private static Button ActionButton(string text) => new() { Content = text, Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(0, 5, 6, 5) };
}
