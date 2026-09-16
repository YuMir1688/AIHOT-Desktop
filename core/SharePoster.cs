using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
namespace AiHot;
internal static class SharePoster
{
    internal static readonly string[] Names = { "薄荷白", "暖纸米白", "冰川蓝", "柔和米灰", "薰衣草灰" };
    private sealed record Palette(string Outer, string Paper, string Ink, string Muted, string Accent, string Badge, string Line, double Radius);
    private static readonly Palette[] Palettes = {
        new("#F2F5EF", "#FFFFFF", "#17362F", "#526D65", "#087C61", "#EAF4EE", "#DCE6DF", 30),
        new("#EDE5D9", "#FFFBF3", "#40352E", "#75665B", "#A36449", "#F4E7D9", "#D8C9B8", 16),
        new("#E3EEF5", "#F8FCFF", "#203C55", "#536E83", "#3C779F", "#E5F0F8", "#CADCE9", 24),
        new("#EAE9E5", "#FFFDF8", "#292928", "#62625D", "#363633", "#EFEDE6", "#BAB9B1", 4),
        new("#ECE8F0", "#FCFAFE", "#453B54", "#73677F", "#886483", "#F0E8F2", "#DFD4E4", 32)
    };
    internal static bool ValidLink(string link) => Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    internal static string SuggestedLead(string? original)
    {
        string text = (original ?? "").Trim();
        if (text.Length <= 180) return text;
        int end = 0;
        // Keep whole sentences and attached quotation marks; never cut at a character limit.
        for (int i = 0; i < text.Length && i < 180; i++)
            if (text[i] is '。' or '！' or '？') { int stop = i + 1; while (stop < text.Length && text[stop] is '”' or '’' or '」' or '』') stop++; if (stop <= 180) end = stop; }
        if (end == 0) return text;
        string remaining = text.Substring(end).TrimStart();
        foreach (string qualifier in new[] { "但", "然而", "不过", "尚", "目前", "需要注意", "值得注意" }) if (remaining.StartsWith(qualifier, StringComparison.Ordinal)) return text;
        return text.Substring(0, end);
    }
    internal static BitmapSource Render(NewsItem item, string title, string summary, bool dark)
        => Render(item, title, summary, dark ? 1 : 0);
    internal static BitmapSource Render(NewsItem item, string title, string summary, int style)
    {
        if (style < 0 || style >= Palettes.Length) throw new ArgumentOutOfRangeException(nameof(style));
        var palette = Palettes[style];
        if (!ValidLink(item.Links.Original)) throw new InvalidOperationException("这条资讯没有有效的原文链接，无法生成二维码。");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 220 || summary.Length > 600) throw new InvalidOperationException("请将标题控制在 220 字以内、摘要控制在 600 字以内，再生成海报。");
        using var data = QRCodeGenerator.GenerateQrCode(item.Links.Original, QRCodeGenerator.ECCLevel.Q);
        int scale = Math.Max(3, 220 / data.ModuleMatrix.Count);
        using var qr = new PngByteQRCode(data);
        var qrImage = new BitmapImage(); using (var stream = new MemoryStream(qr.GetGraphic(scale))) { qrImage.BeginInit(); qrImage.CacheOption = BitmapCacheOption.OnLoad; qrImage.StreamSource = stream; qrImage.EndInit(); qrImage.Freeze(); }
        if (qrImage.PixelWidth > 300) throw new InvalidOperationException("原文链接过长，二维码过密，暂不适合生成此尺寸海报。");
        Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        var ink = B(palette.Ink); var muted = B(palette.Muted); var mint = B(palette.Accent);
        FormattedText T(string value, double size, Brush brush, double width, bool bold = false) => new(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, brush, 1) { MaxTextWidth = width, LineHeight = size * 1.5 };
        double textX = 78, textWidth = 544;
        double headingY = 210;
        double fontSize = title.Length > 65 ? 44 : 48;
        var heading = T(title.Trim(), fontSize, ink, textWidth, true); heading.LineHeight = fontSize * 1.4;
        // Balance short multi-line headings without rewriting text or reducing readability.
        double headingX = textX, fullHeadingHeight = heading.Height;
        if (!title.Contains('\n') && fullHeadingHeight > heading.LineHeight * 1.2 && fullHeadingHeight < heading.LineHeight * 3.2) {
            double low = textWidth * .72, high = textWidth;
            for (int pass = 0; pass < 10; pass++) { double candidate = (low + high) / 2; heading.MaxTextWidth = candidate; if (heading.Height > fullHeadingHeight + .5) low = candidate; else high = candidate; }
            heading.MaxTextWidth = Math.Min(textWidth, high + 8);
        }
        var description = T(summary.Trim(), 30, muted, textWidth); description.LineHeight = 48;
        double summaryY = headingY + heading.Height + 64;
        double contentBottom = summary.Trim().Length > 0 ? summaryY + description.Height : headingY + heading.Height + 0;
        double qrSize = qrImage.PixelWidth, footerHeight = Math.Max(220, qrSize + 28);
        const int height = 960;
        double footerY = height - footerHeight - 110;
        // Fit ordinary mixed Chinese/English titles before rejecting genuinely excessive copy.
        for (int fit = 0; contentBottom + 32 > footerY && fit < 9; fit++) {
            heading.MaxTextWidth = textWidth; headingX = textX;
            double titleSize = Math.Max(32, fontSize - (fit + 1) * 2);
            heading.SetFontSize(titleSize); heading.LineHeight = titleSize * 1.35;
            double bodySize = Math.Max(24, 30 - fit);
            description.SetFontSize(bodySize); description.LineHeight = bodySize * 1.5;
            summaryY = headingY + heading.Height + 48;
            contentBottom = summary.Trim().Length > 0 ? summaryY + description.Height : headingY + heading.Height + 0;
        }
        if (contentBottom + 32 > footerY) throw new InvalidOperationException("内容超出 3:4 卡片可用空间，请精简标题或摘要后重新生成。不会裁切正文或缩小到难以阅读。");
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(B(palette.Outer), null, new Rect(0, 0, 720, height));
            dc.PushTransform(new TranslateTransform(10, 0));
            var paperRect = new Rect(6, 16, 688, height - 32);
            dc.DrawRoundedRectangle(B(palette.Paper), new Pen(B(palette.Line), 1), paperRect, 24, 24);
            dc.PushClip(new RectangleGeometry(paperRect, 24, 24));
            dc.PushOpacity(.035);
            var texturePen = new Pen(mint, .65);
            if (style == 2) { for (int x = 40; x < 660; x += 30) dc.DrawLine(texturePen, new Point(x, 40), new Point(x, height - 40)); for (int y = 40; y < height - 40; y += 30) dc.DrawLine(texturePen, new Point(40, y), new Point(660, y)); }
            else if (style == 4) { for (int y = -700; y < height; y += 20) dc.DrawLine(texturePen, new Point(40, y), new Point(660, y + 620)); }
            else {
                var random = new Random(2026 + style);
                for (int i = 0; i < height * 5; i++) { double x = 40 + random.NextDouble() * 620, y = 40 + random.NextDouble() * (height - 80); if (style == 1) dc.DrawLine(texturePen, new Point(x, y), new Point(x + 2 + random.NextDouble() * 5, y + 1)); else dc.DrawEllipse(mint, null, new Point(x, y), .55, .55); }
            }
            dc.Pop(); dc.Pop();
            dc.DrawRoundedRectangle(mint, null, new Rect(78, 87, 44, 44), 12, 12);
            var monogram = T("A", 28, B("#FFFFFF"), 44, true);
            dc.DrawText(monogram, new Point(78 + (44 - monogram.WidthIncludingTrailingWhitespace) / 2, 87 + (44 - monogram.Height) / 2));
            var brandName = T("YuMir的阅读室", 23, ink, 460, true);
            dc.DrawText(brandName, new Point(138, 87 + (44 - brandName.Height) / 2));
            var category = T(item.CategoryLabel, 20, mint, 400); category.LineHeight = 28;
            var date = T(NewsService.BeijingDate(item.PublishedAt ?? item.DiscoveredAt).ToString("yyyy.MM.dd"), 20, muted, 250);
            dc.DrawText(category, new Point(78, 156));
            dc.DrawText(date, new Point(622 - date.WidthIncludingTrailingWhitespace, 156));
            dc.DrawText(heading, new Point(headingX, headingY));
            if (summary.Trim().Length > 0)
            {
                bool excerpt = !string.IsNullOrWhiteSpace(item.Summary) && item.Summary.Trim().Length > summary.Trim().Length && item.Summary.Trim().StartsWith(summary.Trim(), StringComparison.Ordinal);
                dc.DrawText(T(excerpt ? "摘要节选 · 完整内容见原文" : "内容导读", 18, mint, textWidth), new Point(textX, summaryY - 34));
                dc.DrawText(description, new Point(textX, summaryY));
            }
            dc.DrawLine(new Pen(B(palette.Line), 1), new Point(78, footerY), new Point(622, footerY));
            double qrX = 622 - qrSize, qrY = footerY + 20;
            dc.DrawRectangle(Brushes.White, null, new Rect(qrX, qrY, qrSize, qrSize)); dc.DrawImage(qrImage, new Rect(qrX, qrY, qrSize, qrSize));
            double captionX = 78;
            double captionWidth = qrX - 118;
            var callout = T("扫码阅读原文", 26, ink, captionWidth, true);
            var source = T("来源 · " + item.Source.Name, 20, muted, captionWidth); source.MaxTextHeight = 90; source.Trimming = TextTrimming.CharacterEllipsis;
            var host = T(new Uri(item.Links.Original).Host, 18, muted, captionWidth); host.MaxTextHeight = 48; host.Trimming = TextTrimming.CharacterEllipsis;
            double footerTextY = qrY + Math.Max(0, (qrSize - callout.Height - source.Height - host.Height - 28) / 2);
            dc.DrawText(callout, new Point(captionX, footerTextY));
            dc.DrawText(source, new Point(captionX, footerTextY + callout.Height + 14));
            dc.DrawText(host, new Point(captionX, footerTextY + callout.Height + source.Height + 28));
            dc.DrawText(T("分享卡片整理｜YuMir", 18, muted, 544), new Point(78, height - 86));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(720, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static void Save(BitmapSource image, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
}
internal sealed class ShareWindow : Window
{
    internal ShareWindow(NewsItem item)
    {
        Title = "分享海报"; Width = 1080; Height = 860; MinWidth = 780; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(22) }; root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        var preview = new Image { Stretch = Stretch.Uniform, MaxWidth = 350, HorizontalAlignment = HorizontalAlignment.Center }; var previewPanel = new DockPanel(); var previewLabel = Ui.Text("窄版海报预览 · 700px", 11, Ui.Muted); previewLabel.Margin = new Thickness(0, 0, 0, 12); DockPanel.SetDock(previewLabel, Dock.Top); previewPanel.Children.Add(previewLabel); previewPanel.Children.Add(preview); var previewCard = Ui.Card(previewPanel, "#151E2A", 18); previewCard.Margin = new Thickness(0, 0, 20, 0); root.Children.Add(previewCard);
        previewLabel.Text = "海报预览 · 3:4 · 720 × 960";
        var controls = new StackPanel { Margin = new Thickness(0, 0, 6, 0) }; var controlScroll = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetColumn(controlScroll, 1); root.Children.Add(controlScroll);
        controls.Children.Add(Ui.Text("制作分享卡片", 22)); var intro = Ui.Text("编辑内容，生成一张值得分享的资讯卡。", 11, Ui.Muted); intro.Margin = new Thickness(0, 3, 0, 16); controls.Children.Add(intro);
        var edit = new StackPanel(); var editCard = Ui.Card(edit, "#151E2A", 16); controls.Children.Add(editCard);
        var titleCount = Ui.Text("", 10, Ui.Muted); var summaryCount = Ui.Text("", 10, Ui.Muted);
        void Caption(string label, TextBlock count) { var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; count.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(count, Dock.Right); row.Children.Add(count); row.Children.Add(Ui.Text(label, 12)); edit.Children.Add(row); }
        Caption("标题", titleCount);
        var title = new TextBox { Text = item.Title, FontSize = 13, Padding = new Thickness(12, 10, 12, 10), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 16) }; TextBlock.SetLineHeight(title, 21); edit.Children.Add(title);
        Caption("摘要", summaryCount);
        var summary = new TextBox { Text = SharePoster.SuggestedLead(item.Summary), FontSize = 12, Padding = new Thickness(12, 10, 12, 10), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 166, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; TextBlock.SetLineHeight(summary, 21); edit.Children.Add(summary);
        var editHint = Ui.Text("建议导读不超过180字。节选不是完整结论，请核对上下文；不修改原资讯。", 10, Ui.Muted); editHint.Margin = new Thickness(0, 8, 0, 0); edit.Children.Add(editHint);
        var restoreSummary = new Button { Content = "恢复完整摘要", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 4, 0, 0), FontSize = 10, Background = Brushes.Transparent, Foreground = Ui.Mint }; restoreSummary.Click += (_, _) => summary.Text = item.Summary ?? ""; edit.Children.Add(restoreSummary);
        void Counts() { titleCount.Text = $"{title.Text.Length} / 220"; summaryCount.Text = $"{summary.Text.Length} / 600"; titleCount.Foreground = title.Text.Length > 220 ? Ui.Brush("#E8A29E") : Ui.Muted; summaryCount.Foreground = summary.Text.Length > 600 ? Ui.Brush("#E8A29E") : Ui.Muted; } Counts();
        int selectedStyle = 0; BitmapSource? poster = null;
        var styleCaption = Ui.Text("选择版式 · 固定 3:4 · 720 × 960", 12); styleCaption.Margin = new Thickness(0, 16, 0, 8); controls.Children.Add(styleCaption);
        var styles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5 }; var styleButtons = new System.Collections.Generic.List<Button>(); controls.Children.Add(styles);
        void PaintStyle() { for (int i = 0; i < styleButtons.Count; i++) { styleButtons[i].Background = Ui.Brush(i == selectedStyle ? "#23493F" : "#202A38"); styleButtons[i].BorderBrush = i == selectedStyle ? Ui.Mint : Ui.Line; } }
        var generate = new Button { Content = "更新预览", Height = 36, Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = Ui.Line, Margin = new Thickness(0, 12, 0, 12) }; controls.Children.Add(generate);
        var exports = new Grid(); exports.ColumnDefinitions.Add(new ColumnDefinition()); exports.ColumnDefinitions.Add(new ColumnDefinition());
        var copy = new Button { Content = "复制图片", Height = 42, IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) }; var save = new Button { Content = "保存 PNG", Height = 42, IsEnabled = false, Background = Ui.Mint, Foreground = Ui.Brush("#103B2E"), FontWeight = FontWeights.SemiBold }; Grid.SetColumn(save, 1); exports.Children.Add(copy); exports.Children.Add(save); controls.Children.Add(exports);
        var status = Ui.Text("", 11, Ui.Muted); status.Margin = new Thickness(0, 12, 0, 0); controls.Children.Add(status);
        void Generate() { try { poster = SharePoster.Render(item, title.Text, summary.Text, selectedStyle); preview.Source = poster; previewLabel.Text = "海报预览 · 3:4 · 720 × 960"; copy.IsEnabled = save.IsEnabled = true; status.Text = $"{SharePoster.Names[selectedStyle]} · {poster.PixelWidth} × {poster.PixelHeight}" + (title.Text.Length > 65 ? "\n标题较长，建议精简，保留主体与关键限定。" : "") + (summary.Text.Length > 180 ? "\n导读较长，建议人工精简后分享。" : ""); } catch (Exception e) { poster = null; preview.Source = null; copy.IsEnabled = save.IsEnabled = false; status.Text = e.Message; previewLabel.Text = "暂时无法生成预览\n" + e.Message; } }
        void Dirty() { Counts(); copy.IsEnabled = save.IsEnabled = false; status.Text = "内容已修改，请更新预览后保存。"; }
        title.TextChanged += (_, _) => Dirty(); summary.TextChanged += (_, _) => Dirty();
        for (int i = 0; i < SharePoster.Names.Length; i++) {
            int choice = i; var tile = new StackPanel();
            try { tile.Children.Add(new Image { Source = SharePoster.Render(item, "值得分享的 AI 动态", "资讯摘要与原文入口", i), Width = 42, Height = 58, Stretch = Stretch.Uniform }); } catch { }
            var label = Ui.Text(SharePoster.Names[i], 9); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 5, 0, 0); tile.Children.Add(label);
            var button = new Button { Content = tile, Padding = new Thickness(3, 7, 3, 7), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 3, 0), ToolTip = SharePoster.Names[i] };
            button.Click += (_, _) => { selectedStyle = choice; PaintStyle(); Generate(); }; styleButtons.Add(button); styles.Children.Add(button);
        } PaintStyle(); generate.Click += (_, _) => Generate();
        copy.Click += (_, _) => { try { if (poster != null) { Clipboard.SetImage(poster); status.Text = "图片已复制，可粘贴分享。"; } } catch { status.Text = "剪贴板暂时被占用，请重试或保存 PNG。"; } };
        save.Click += (_, _) => { var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = "YuMir-AIHOT-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", DefaultExt = ".png" }; if (dialog.ShowDialog(this) == true && poster != null) { try { SharePoster.Save(poster, dialog.FileName); status.Text = "已保存：" + dialog.FileName; } catch (Exception e) { status.Text = "保存失败：" + e.Message; } } };
        Ui.Shell(this, "分享海报", root); Generate();
    }
}
