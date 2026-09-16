using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AiHot;
public partial class MainWindow : Window
{
    private readonly Settings settings = Storage.Read<Settings>("settings.json");
    private readonly NewsService service = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer systemTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch frameClock = new();
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private HwndSource? hwndSource;
    private ReaderWindow? reader;
    private Window? settingsWindow;
    private List<NewsItem> items = new();
    private int index;
    private bool paused, busy, userHidden, fullHidden, closing;
    private double dwell, scroll;
    private DateTimeOffset nextFetch;
    private int failures;
    private bool offline;
    private Point dragOrigin;
    private bool dragPending, suppressClick;
    private ContextMenu? quickMenu;
    private DateTime feedDate = NewsService.BeijingDate(DateTimeOffset.UtcNow);
    private readonly Stopwatch wheelClock = Stopwatch.StartNew();
    private string RangeLabel => settings.Window == "today" ? "今天优先，逐日向前补齐 100 条（北京时间）" : settings.Window == "24h" ? "最近 24 小时" : "最近 7 天";
    private readonly bool preview;
    public MainWindow(bool preview = false)
    {
        this.preview = preview;
        Theme.Apply(settings.LightTheme);
        InitializeComponent();
        Width = Math.Clamp(double.IsFinite(settings.Width) ? settings.Width : 960, 560, 1600);
        settings.Speed = Math.Clamp(double.IsFinite(settings.Speed) ? settings.Speed : 48, 20, 100);
        Opacity = Math.Clamp(double.IsFinite(settings.Opacity) ? settings.Opacity : .96, .45, 1);
        Topmost = settings.Topmost;
        UpdateLock();
        if (settings.Category is not ("" or "ai-models" or "ai-products" or "industry" or "paper" or "tip")) settings.Category = "";
        if (settings.FeedPreferenceVersion < 1) { settings.Window = "today"; settings.FeedPreferenceVersion = 1; }
        if (settings.Window is not ("today" or "24h" or "7d")) settings.Window = "today";
        Loaded += (_, _) =>
        {
            RestorePosition();
            if (preview) return;
            SetupTray();
            items = service.VisibleItems(settings); SavePosition();
            offline = items.Count > 0;
            SetHeadline();
            CompositionTarget.Rendering += OnFrame;
            frameClock.Start(); refreshTimer.Tick += async (_, _) => { if (DateTimeOffset.Now >= nextFetch) await Refresh(); }; refreshTimer.Start();
            systemTimer.Tick += (_, _) => WatchFullscreen(); systemTimer.Start();
            _ = Refresh();
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x80);
            if (!preview)
            {
                hwndSource = HwndSource.FromHwnd(handle);
                hwndSource?.AddHook(WindowMessage);
                Native.RegisterHotKey(handle, 1, 0x4000 | 0x0001 | 0x0002, 0x41);
            }
        };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            suppressClick = false;
            if (settings.PositionLocked) { dragPending = false; return; }
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            var node = e.OriginalSource as DependencyObject;
            while (node != null && node != this)
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase && !alt) return;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            dragOrigin = e.GetPosition(this); dragPending = true;
            if (alt) { e.Handled = true; MoveWidget(); }
        };
        PreviewMouseMove += (_, e) =>
        {
            if (!dragPending || e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(this);
            if (Math.Abs(p.X - dragOrigin.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - dragOrigin.Y) >= SystemParameters.MinimumVerticalDragDistance) { e.Handled = true; MoveWidget(); }
        };
        PreviewMouseLeftButtonUp += (_, e) => { dragPending = false; if (suppressClick) e.Handled = true; };
        TickerArea.MouseLeftButtonUp += (_, _) =>
        {
            if (suppressClick) return;
            var current = items.ElementAtOrDefault(index);
            if (current != null) OpenUrl(current.Links.Original);
        };
        ToolTipService.SetIsEnabled(TickerArea, false);
        TickerArea.MouseWheel += (_, e) =>
        {
            e.Handled = true;
            if (e.Delta == 0 || items.Count < 2 || wheelClock.ElapsedMilliseconds < 220) return;
            wheelClock.Restart(); StepNews(e.Delta > 0 ? -1 : 1);
        };
        AlignmentMenuButton.Click += (_, _) => ShowMenu(true);
        LockButton.Click += (_, _) => { settings.PositionLocked = !settings.PositionLocked; dragPending = false; UpdateLock(); SavePosition(); };
        MouseRightButtonUp += (_, _) => ShowMenu();
        TickerArea.SizeChanged += (_, _) => { scroll = 0; dwell = 0; };
        Closed += (_, _) =>
        {
            closing = true; SavePosition(); CompositionTarget.Rendering -= OnFrame;
            refreshTimer.Stop(); systemTimer.Stop(); tray?.Dispose(); trayIcon?.Dispose(); service.Dispose();
            Native.UnregisterHotKey(new WindowInteropHelper(this).Handle, 1);
            hwndSource?.RemoveHook(WindowMessage);
        };
    }
    private IntPtr WindowMessage(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x0312 && w.ToInt32() == 1) { if (IsVisible) HideWidget(); else ShowWidget(); handled = true; }
        if (message == 0x007E) Dispatcher.BeginInvoke(new Action(() => { RestorePosition(); SavePosition(); }));
        return IntPtr.Zero;
    }
    private void MoveWidget()
    {
        if (settings.PositionLocked) return;
        dragPending = false; suppressClick = true;
        try { DragMove(); ClampPosition(); SavePosition(); } catch (InvalidOperationException) { }
    }
    private void RestorePosition()
    {
        var area = SystemParameters.WorkArea;
        Left = double.IsFinite(settings.Left) ? settings.Left : -100000;
        Top = double.IsFinite(settings.Top) ? settings.Top : -100000;
        // Saved WPF coordinates are checked against monitor pixel bounds using this window's DPI.
        var dpi = VisualTreeHelper.GetDpi(this);
        bool visible = Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new System.Drawing.Rectangle((int)(Left * dpi.DpiScaleX), (int)(Top * dpi.DpiScaleY), (int)(Width * dpi.DpiScaleX), (int)(Height * dpi.DpiScaleY))));
        if (!visible) { Left = area.Left + Math.Max(0, (area.Width - Width) / 2); Top = area.Bottom - Height - 20; }
        ClampPosition();
    }
    private void ClampPosition()
    {
        var area = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double x = area.Left / dpi.DpiScaleX, y = area.Top / dpi.DpiScaleY;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width / dpi.DpiScaleX));
        Left = Math.Clamp(Left, x, Math.Max(x, (area.Right / dpi.DpiScaleX) - Width));
        Top = Math.Clamp(Top, y, Math.Max(y, (area.Bottom / dpi.DpiScaleY) - Height));
    }
    private void SavePosition()
    {
        if (preview) return;
        settings.LightTheme = Theme.IsLight;
        settings.Left = Left; settings.Top = Top; settings.Width = Width; settings.Opacity = Opacity;
        Storage.Save("settings.json", settings);
    }
    internal void AlignDesktop(int alignment)
    {
        if (alignment < 0 || alignment > 2) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (settings.PositionLocked) return;
        if (Native.AlignWindow(new WindowInteropHelper(this).Handle, alignment)) SavePosition();
    }
    private void UpdateLock()
    {
        LockGlyph.Text = settings.PositionLocked ? "\uE72E" : "\uE785";
        LockGlyph.Foreground = settings.PositionLocked ? Ui.Mint : Ui.Muted;
        LockButton.Background = settings.PositionLocked ? Ui.Brush("#254A40") : Brushes.Transparent;
        LockButton.ToolTip = settings.PositionLocked ? "位置已锁定 · 点击解锁" : "位置可移动 · 点击锁定";
        Grip.Cursor = settings.PositionLocked ? Cursors.Arrow : Cursors.SizeAll;
        Grip.ToolTip = settings.PositionLocked ? "位置已锁定，请点击右侧小锁解锁" : "拖动移动 · Alt + 拖动任意位置";
        AlignmentMenuButton.ToolTip = settings.PositionLocked ? "位置已锁定，请先点击小锁解锁" : "桌面左对齐 / 中对齐 / 右对齐";
    }
    public void ShowWidget()
    {
        userHidden = false;
        if (reader is { IsVisible: true } && reader.WindowState != WindowState.Minimized) { reader.Activate(); WatchFullscreen(); return; }
        if (settingsWindow is { IsVisible: true } && settingsWindow.WindowState != WindowState.Minimized) { settingsWindow.Activate(); WatchFullscreen(); return; }
        fullHidden = false; Show(); ClampPosition();
    }
    private void HideWidget() { userHidden = true; Hide(); }
    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示挂件", null, (_, _) => Dispatcher.Invoke(ShowWidget));
        menu.Items.Add("隐藏挂件", null, (_, _) => Dispatcher.Invoke(HideWidget));
        menu.Items.Add("阅读资讯", null, (_, _) => Dispatcher.Invoke(OpenReader));
        menu.Items.Add("暂停 / 继续", null, (_, _) => Dispatcher.Invoke(TogglePause));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => { Close(); Application.Current.Shutdown(); }));
        using (var bitmap = new System.Drawing.Bitmap(32, 32))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(18, 25, 35));
            using var fg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(85, 229, 188));
            graphics.FillEllipse(bg, 0, 0, 31, 31);
            using var font = new System.Drawing.Font("Segoe UI", 18, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            graphics.DrawString("A", font, fg, 7, 4);
            var iconHandle = bitmap.GetHicon();
            using var temporaryIcon = System.Drawing.Icon.FromHandle(iconHandle);
            trayIcon = (System.Drawing.Icon)temporaryIcon.Clone(); Native.DestroyIcon(iconHandle);
        }
        tray = new Forms.NotifyIcon { Text = "AIHOT · YuMir | Ctrl+Alt+A 显示/隐藏", Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
    }
    internal bool IsPaused => paused;
    private void TogglePause() { paused = !paused; }
    private void ShowMenu(bool alignmentOnly = false)
    {
        if (quickMenu?.IsOpen == true) { quickMenu.IsOpen = false; return; }
        quickMenu = alignmentOnly ? BuildAlignmentMenu() : BuildMenu(); quickMenu.PlacementTarget = Shell;
        quickMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
        quickMenu.CustomPopupPlacementCallback = (popup, target, offset) => new[] {
            new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 6), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
            new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - 6), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
        };
        quickMenu.IsOpen = true;
    }
    internal ContextMenu BuildAlignmentMenu()
    {
        var menu = new ContextMenu { MinWidth = 170 };
        string[] titles = { "左对齐", "中对齐", "右对齐" };
        for (int i = 0; i < titles.Length; i++)
        {
            int alignment = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(MenuIcons.Create(new[] { "left", "center", "right" }[i]));
            var label = Ui.Text(titles[i], 13); label.Margin = new Thickness(12, 0, 0, 0); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            var item = new MenuItem { Header = row, IsEnabled = !settings.PositionLocked };
            item.Click += (_, _) => AlignDesktop(alignment); menu.Items.Add(item);
        }
        return menu;
    }
    internal ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { MinWidth = 272 };
        void Add(string icon, string title, string hint, Action action, bool danger = false, bool enabled = true)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(MenuIcons.Create(title.Contains("左对齐") ? "left" : title.Contains("中对齐") ? "center" : title.Contains("右对齐") ? "right" : icon, danger));
            var label = Ui.Text(title, 13, danger ? Ui.Brush("#E8A29E") : Ui.Ink); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(label, 1); row.Children.Add(label);
            var value = Ui.Text(hint, 10, Ui.Muted); value.Margin = new Thickness(18, 0, 0, 0); value.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(value, 2); row.Children.Add(value);
            var item = new MenuItem { Header = row, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("≡", "打开阅读室", "摘要 / 原文", OpenReader);
        Add(paused ? "▶" : "Ⅱ", paused ? "继续播报" : "暂停播报", paused ? "已暂停" : "播放中", TogglePause);
        Add("→", "下一条资讯", "", Advance);
        menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        Add("◇", "保持置顶", settings.Topmost ? "已开启 ●" : "已关闭 ○", () => { settings.Topmost = !settings.Topmost; Topmost = settings.Topmost; SavePosition(); });
        Add("⚙", "偏好设置", "外观 / 内容", OpenSettings);
        Add("◐", Theme.IsLight ? "切换深色" : "切换浅色", "主题", Theme.Toggle);
        menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        Add("←", "桌面左对齐", settings.PositionLocked ? "已锁定" : "当前屏幕", () => AlignDesktop(0), enabled: !settings.PositionLocked);
        Add("↔", "桌面中对齐", settings.PositionLocked ? "已锁定" : "当前屏幕", () => AlignDesktop(1), enabled: !settings.PositionLocked);
        Add("→", "桌面右对齐", settings.PositionLocked ? "已锁定" : "当前屏幕", () => AlignDesktop(2), enabled: !settings.PositionLocked);
        menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        Add("−", "隐藏挂件", "Ctrl+Alt+A", HideWidget);
        Add("×", "退出应用", "", () => { Close(); Application.Current.Shutdown(); }, true);
        return menu;
    }
    private async System.Threading.Tasks.Task Refresh()
    {
        if (busy || closing) return;
        busy = true; Status.ToolTip = "正在更新…";
        try
        {
            string? current = items.ElementAtOrDefault(index)?.Id;
            bool changed = await service.Refresh(settings);
            if (closing) return;
            failures = 0; offline = false; nextFetch = DateTimeOffset.Now.AddMinutes(5);
            if (changed || items.Count == 0)
            {
                items = service.VisibleItems(settings);
                index = Math.Max(0, items.FindIndex(n => n.Id == current));
                SetHeadline(); reader?.UpdateItems(items);
            }
            reader?.UpdateStatus("AIHOT 精选 · " + DateTime.Now.ToString("HH:mm") + " 已更新");
            Status.ToolTip = $"精选 {items.Count} 条 · {DateTime.Now:HH:mm} 已更新\n每 5 分钟自动更新 · {RangeLabel} · 最多 100 条";
        }
        catch (Exception e)
        {
            if (closing) return;
            offline = true; failures++;
            var delay = TimeSpan.FromMinutes(Math.Min(60, 5 * Math.Pow(2, Math.Min(failures - 1, 4))));
            if (e is FeedException f && f.Retry > delay) delay = f.Retry.Value;
            nextFetch = DateTimeOffset.Now.Add(delay);
            reader?.UpdateStatus("离线缓存 · " + service.Cache.FetchedAt.LocalDateTime.ToString("MM-dd HH:mm"));
            Status.ToolTip = $"{e.Message}\n下次尝试 {nextFetch.LocalDateTime:HH:mm}；缓存时间 {service.Cache.FetchedAt.LocalDateTime:MM-dd HH:mm}";
            if (items.Count == 0) { Headline.Text = "连接暂不可用，将自动重试"; Subtitle.Text = "网络恢复后会继续播报 · 点击查看阅读面板"; }
        }
        finally { busy = false; }
    }
    private void SetHeadline()
    {
        scroll = dwell = 0; Shift.X = 0;
        if (items.Count == 0) { Headline.Text = "这个范围暂时没有精选资讯"; Subtitle.Text = "有新精选后自动更新 · " + RangeLabel; TickerArea.ToolTip = "有新精选后自动更新 · " + RangeLabel; return; }
        index %= items.Count;
        var n = items[index]; Headline.Text = n.Title;
        Subtitle.Text = $"{n.Meta}   /   " + (items.Count >= 100 ? "TOP" : $"精选 {items.Count} 条") + $" / {index + 1:00}";
        var tip = new StackPanel { MaxWidth = 440 };
        tip.Children.Add(Ui.Text(n.Title, 14));
        var meta = Ui.Text(n.Meta, 10, Ui.Muted); meta.Margin = new Thickness(0, 8, 0, 8); tip.Children.Add(meta);
        tip.Children.Add(Ui.Text("已暂停滚动 · 滚轮切换上下条 · 点击打开原文", 11, Ui.Mint));
        TickerArea.ToolTip = new ToolTip { Content = tip };
    }
    private void Advance() => StepNews(1);
    private void StepNews(int direction)
    {
        if (items.Count == 0) return;
        index = (index + direction + items.Count) % items.Count; SetHeadline();
        if (SystemParameters.ClientAreaAnimation)
            TickerArea.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
    }
    private void OnFrame(object? sender, EventArgs e)
    {
        double delta = Math.Min(frameClock.Elapsed.TotalSeconds, .1); frameClock.Restart();
        if (!IsVisible || paused || IsMouseOver || quickMenu?.IsOpen == true || items.Count == 0) return;
        TickPlayback(delta);
    }
    private void TickPlayback(double delta)
    {
        if (items.Count == 0) return;
        dwell += delta;
        double overflow = Math.Max(0, Headline.ActualWidth - Track.ActualWidth);
        if (overflow < 1) { if (dwell > Math.Clamp(Headline.Text.Length * .2, 6, 11)) Advance(); return; }
        if (dwell < 2) return;
        scroll += settings.Speed * delta * Math.Clamp((dwell - 2) / .7, 0, 1);
        Shift.X = -Math.Min(scroll, overflow);
        if (scroll > overflow + settings.Speed * 2.5) Advance();
    }
    internal void OpenReader()
    {
        if (reader != null) { if (reader.WindowState == WindowState.Minimized) reader.WindowState = WindowState.Normal; reader.Activate(); WatchFullscreen(); return; }
        reader = new ReaderWindow(items, offline ? $"离线缓存 · {RangeLabel}" : "AIHOT 精选 · " + RangeLabel);
        reader.Closed += (_, _) => { reader = null; WatchFullscreen(); };
        reader.StateChanged += (_, _) => WatchFullscreen();
        reader.Show();
        WatchFullscreen();
        if (items.Count > 0) reader.Select(items[index].Id);
    }
    private void OpenSettings()
    {
        if (settingsWindow != null) { if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate(); WatchFullscreen(); return; }
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        panel.Children.Add(Ui.Text("让信息，恰好在场。", 24));
        var hint = Ui.Text("调整你的桌面阅读节奏。拖动挂件任意非按钮区域即可移动。", 11, Ui.Muted); hint.Margin = new Thickness(0, 8, 0, 20); panel.Children.Add(hint);
        var appearance = new StackPanel(); appearance.Children.Add(Ui.Text("01  /  外观与节奏", 12, Ui.Mint));
        panel.Children.Add(Ui.Card(appearance));
        Slider Slider(string label, double value, double min, double max)
        {
            var row = new DockPanel { Margin = new Thickness(0, 18, 0, 8) };
            var number = Ui.Text("", 11, Ui.Mint); DockPanel.SetDock(number, Dock.Right); row.Children.Add(number); row.Children.Add(Ui.Text(label, 12)); appearance.Children.Add(row);
            var s = new Slider { Minimum = min, Maximum = max, Value = value, Foreground = Ui.Mint, Height = 22 };
            void Update() => number.Text = max <= 1 ? $"{s.Value:P0}" : $"{s.Value:0}" + (label.Contains("速度") ? " px/s" : " px");
            s.ValueChanged += (_, _) => Update(); Update(); appearance.Children.Add(s); return s;
        }
        var width = Slider("挂件宽度", Width, 560, 1600);
        var opacity = Slider("背景透明度", Opacity, .45, 1);
        var speed = Slider("滚动速度 · 慢 → 快", settings.Speed, 20, 100);
        var topmost = new CheckBox { Content = "保持在其他窗口上方", IsChecked = settings.Topmost, Margin = new Thickness(0, 18, 0, 14) }; appearance.Children.Add(topmost);
        var fullscreen = new CheckBox { Content = "全屏播放 / 游戏时自动隐藏", IsChecked = settings.HideFullscreen }; appearance.Children.Add(fullscreen);
        var feed = new StackPanel(); feed.Children.Add(Ui.Text("02  /  关注的内容", 12, Ui.Mint)); var feedCard = Ui.Card(feed); feedCard.Margin = new Thickness(0, 14, 0, 0); panel.Children.Add(feedCard);
        Func<int> Choice(string label, string[] options, int initial)
        {
            var caption = Ui.Text(label, 11, Ui.Muted); caption.Margin = new Thickness(0, 16, 0, 8); feed.Children.Add(caption);
            int selected = Math.Max(0, initial); var wrap = new WrapPanel(); var buttons = new List<Button>();
            void Paint() { for (int i = 0; i < buttons.Count; i++) { buttons[i].Background = Ui.Brush(i == selected ? "#254A40" : "#202D3D"); buttons[i].Foreground = i == selected ? Ui.Mint : Ui.Ink; } }
            for (int i = 0; i < options.Length; i++) { int index = i; var b = new Button { Content = options[i], FontSize = 11, Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(0, 0, 6, 6) }; b.Click += (_, _) => { selected = index; Paint(); }; buttons.Add(b); wrap.Children.Add(b); }
            Paint(); feed.Children.Add(wrap); return () => selected;
        }
        var window = Choice("播报范围", new[] { "今天优先 · 补齐100条", "最近 24 小时", "最近 7 天" }, settings.Window == "today" ? 0 : settings.Window == "24h" ? 1 : 2);
        var keys = new[] { "", "ai-models", "ai-products", "industry", "paper", "tip" };
        var category = Choice("资讯分类", new[] { "全部精选", "模型", "产品", "行业", "论文", "技巧" }, Array.IndexOf(keys, settings.Category));
        var save = new Button { Content = "保存我的偏好", Padding = new Thickness(16, 12, 16, 12), FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("#103B2E"), Margin = new Thickness(0, 18, 0, 12), Background = Ui.Mint }; panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = "数据来源：AIHOT · 每 5 分钟更新\n桌面体验策划｜YuMir", Foreground = Brushes.SlateGray, FontSize = 11, TextAlignment = TextAlignment.Center });
        settingsWindow = new Window { Title = "AIHOT · 设置", Width = 490, Height = 820, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        Ui.Shell(settingsWindow, "偏好设置", new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        settingsWindow.Closed += (_, _) => { settingsWindow = null; WatchFullscreen(); };
        settingsWindow.StateChanged += (_, _) => WatchFullscreen();
        save.Click += async (_, _) =>
        {
            string old = NewsService.Query(settings);
            Width = width.Value; Opacity = opacity.Value; settings.Speed = speed.Value;
            settings.Topmost = Topmost = topmost.IsChecked == true; settings.HideFullscreen = fullscreen.IsChecked == true;
            settings.Window = new[] { "today", "24h", "7d" }[window()]; settings.Category = keys[category()];
            ClampPosition(); SavePosition(); settingsWindow?.Close();
            if (old != NewsService.Query(settings))
            {
                // A running request belongs to the previous filter: wait before reloading.
                while (busy && !closing) await System.Threading.Tasks.Task.Delay(100);
                if (closing) return;
                items = new(); index = 0; SetHeadline(); reader?.UpdateItems(items);
                await Refresh();
            }
        };
        settingsWindow.Show();
        WatchFullscreen();
    }
    private void WatchFullscreen()
    {
        var today = NewsService.BeijingDate(DateTimeOffset.UtcNow);
        if (settings.Window == "today" && today != feedDate)
        {
            feedDate = today; items = service.VisibleItems(settings); index = 0;
            SetHeadline(); reader?.UpdateItems(items); nextFetch = DateTimeOffset.Now;
        }
        if (userHidden || closing) return;
        bool panelOpen = (reader is { IsVisible: true } && reader.WindowState != WindowState.Minimized) || (settingsWindow is { IsVisible: true } && settingsWindow.WindowState != WindowState.Minimized);
        bool hide = panelOpen || (settings.HideFullscreen && Native.IsFullscreen(new WindowInteropHelper(this).Handle));
        if (hide && !fullHidden) { fullHidden = true; Hide(); }
        else if (!hide && fullHidden) { fullHidden = false; Show(); }
    }
    public void RenderPreview()
    {
        items = service.VisibleItems(settings);
        if (items.Count == 0) items.Add(new NewsItem { Id = "preview", Title = "AI HOT · 让值得关注的新动态，轻轻经过桌面", Source = new NewsSource { Name = "界面示意，非真实资讯" }, Category = "industry", DiscoveredAt = DateTimeOffset.Now });
        SetHeadline(); Status.ToolTip = $"精选 {items.Count} 条 · {DateTime.Now:HH:mm}";
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "widget-preview.png")); encoder.Save(file);
        var menuPreview = BuildAlignmentMenu(); menuPreview.PlacementTarget = AlignmentMenuButton; menuPreview.IsOpen = true;
        menuPreview.ApplyTemplate(); menuPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); menuPreview.Arrange(new Rect(menuPreview.DesiredSize)); menuPreview.UpdateLayout();
        var menuBitmap = new RenderTargetBitmap((int)Math.Ceiling(menuPreview.ActualWidth), (int)Math.Ceiling(menuPreview.ActualHeight), 96, 96, PixelFormats.Pbgra32); menuBitmap.Render(menuPreview);
        var menuEncoder = new PngBitmapEncoder(); menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap));
        using var menuFile = File.Create(Path.Combine(AppContext.BaseDirectory, "menu-preview.png")); menuEncoder.Save(menuFile); menuPreview.IsOpen = false;
        var readerPreview = new ReaderWindow(items, "AIHOT 精选 · 阅读面板预览"); readerPreview.Show(); readerPreview.UpdateLayout();
        var readerBitmap = new RenderTargetBitmap((int)readerPreview.ActualWidth, (int)readerPreview.ActualHeight, 96, 96, PixelFormats.Pbgra32); readerBitmap.Render(readerPreview);
        var readerEncoder = new PngBitmapEncoder(); readerEncoder.Frames.Add(BitmapFrame.Create(readerBitmap));
        using var readerFile = File.Create(Path.Combine(AppContext.BaseDirectory, "reader-preview.png")); readerEncoder.Save(readerFile); readerPreview.Close();
        OpenSettings(); settingsWindow!.UpdateLayout();
        var settingsBitmap = new RenderTargetBitmap((int)settingsWindow.ActualWidth, (int)settingsWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32); settingsBitmap.Render(settingsWindow);
        var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
        using var settingsFile = File.Create(Path.Combine(AppContext.BaseDirectory, "settings-preview.png")); settingsEncoder.Save(settingsFile); settingsWindow.Close();
        Close();
    }
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show("无法打开浏览器：" + e.Message, "AIHOT"); }
    }
}
internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder text, int max);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    internal static bool AlignWindow(IntPtr window, int alignment)
    {
        if (!GetWindowRect(window, out var bounds)) return false;
        var area = Forms.Screen.FromHandle(window).WorkingArea;
        int available = Math.Max(0, area.Width - (bounds.Right - bounds.Left));
        int x = area.Left + (alignment == 0 ? 0 : alignment == 1 ? available / 2 : available);
        // Use physical monitor coordinates; preserve size, height, activation and Z order.
        return SetWindowPos(window, IntPtr.Zero, x, bounds.Top, 0, 0, 0x0015);
    }
    internal static bool IsFullscreen(IntPtr widget)
    {
        var h = GetForegroundWindow(); if (h == IntPtr.Zero || h == widget) return false;
        GetWindowThreadProcessId(h, out uint pid); if (pid == Environment.ProcessId) return false;
        var cls = new System.Text.StringBuilder(256); GetClassName(h, cls, 256);
        if (cls.ToString() is "Progman" or "WorkerW") return false;
        var screen = Forms.Screen.FromHandle(h);
        if (screen.DeviceName != Forms.Screen.FromHandle(widget).DeviceName || !GetWindowRect(h, out var r)) return false;
        var b = screen.Bounds;
        return Math.Abs(r.Left - b.Left) <= 2 && Math.Abs(r.Top - b.Top) <= 2 && Math.Abs(r.Right - b.Right) <= 2 && Math.Abs(r.Bottom - b.Bottom) <= 2;
    }
}
