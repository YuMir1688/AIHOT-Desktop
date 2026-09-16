using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
namespace AiHot;
internal static class Ui
{
    private static readonly DependencyProperty DragRegionProperty = DependencyProperty.RegisterAttached("DragRegion", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    internal static void MakeDraggable(UIElement region)
    {
        region.SetValue(DragRegionProperty, true);
        if (region is FrameworkElement element) { element.Cursor = Cursors.SizeAll; element.ToolTip = "按住鼠标左键拖动窗口 · Alt + 拖动任意位置"; }
    }
    internal static bool CanStartDrag(DependencyObject? source, bool alt)
    {
        if (alt) return true;
        bool region = false;
        while (source != null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.RangeBase) return false;
            region |= (bool)source.GetValue(DragRegionProperty);
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return region;
    }
    internal static SolidColorBrush Brush(string hex) => Theme.Brush(hex);
    internal static readonly Brush Ink = Brush("#EBF1F6"), Muted = Brush("#8495A9"), Mint = Brush("#73E5C1"), Line = Brush("#283443");
    internal static TextBlock Text(string text, double size = 12, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.65 };
    internal static Border Card(UIElement child, string background = "#151E2A", double padding = 20) => new() { Child = child, Background = Brush(background), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(padding) };
    internal static Border Badge(string text) => new() { Child = Text(text, 10, Mint), Background = Brush("#1A3031"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), HorizontalAlignment = HorizontalAlignment.Left };
    internal static void Shell(Window window, string label, UIElement body)
    {
        window.FontFamily = new FontFamily("Microsoft YaHei UI"); window.Foreground = Ink; window.Background = Brush("#0E141D"); window.WindowStyle = WindowStyle.None;
        window.Width = Math.Min(window.Width, Math.Max(window.MinWidth, SystemParameters.WorkArea.Width - 36));
        window.Height = Math.Min(window.Height, Math.Max(window.MinHeight, SystemParameters.WorkArea.Height - 36));
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 58, ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(14) });
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Background = Brushes.Transparent, Margin = new Thickness(22, 0, 14, 0) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Mint, Child = new TextBlock { Text = "A", FontWeight = FontWeights.Bold, FontSize = 18, Foreground = Brush("#102C26"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        brand.Children.Add(new TextBlock { Text = "AI HOT", FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(10, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = " /  " + label, Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }); header.Children.Add(brand);
        MakeDraggable(header);
        window.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!CanStartDrag(e.OriginalSource as DependencyObject, (Keyboard.Modifiers & ModifierKeys.Alt) != 0)) return;
            e.Handled = true;
            try { window.DragMove(); } catch (InvalidOperationException) { }
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var theme = new Button { Content = "◐", FontSize = 20, Width = 34, Height = 30, Padding = new Thickness(0), Background = Brushes.Transparent, ToolTip = "切换深色 / 浅色" }; theme.Click += (_, _) => Theme.Toggle(); controls.Children.Add(theme);
        WindowChrome.SetIsHitTestVisibleInChrome(controls, true);
        var minimize = new Button { Content = "—", Width = 32, Height = 30, Padding = new Thickness(0), Background = Brushes.Transparent, ToolTip = "最小化" }; minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;
        var close = new Button { Content = "×", Width = 32, Height = 30, Padding = new Thickness(0), FontSize = 20, Background = Brushes.Transparent, ToolTip = "关闭" }; close.Click += (_, _) => window.Close(); controls.Children.Add(minimize); controls.Children.Add(close); Grid.SetColumn(controls, 1); header.Children.Add(controls); root.Children.Add(header);
        var content = new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = body }; Grid.SetRow(content, 1); root.Children.Add(content);
        window.Content = new Border { BorderBrush = Brush("#354254"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = root };
        window.PreviewKeyDown += (_, e) => { if (e.Key != Key.Escape) return; if (Keyboard.FocusedElement is TextBox text && text.Text.Length > 0) text.Clear(); else window.Close(); e.Handled = true; };
    }
}
