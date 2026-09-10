using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiHot;
using QRCoder;

namespace YuMir.Cards;

public static class EditorialCard
{
    public static readonly string[] Names = ["编辑精选", "午夜薄荷", "朱红刊物", "蓝调简报", "暖纸书签"];
    internal sealed record Palette(string Paper, string Ink, string Muted, string Accent, string Line, string Panel);
    internal static Palette GetPalette(int style) => Palettes[style];
    private static readonly Palette[] Palettes = [
        new("#F6F3EC", "#242721", "#70726B", "#C94F35", "#D8D6CC", "#EDEAE1"),
        new("#132D29", "#F0F4E9", "#B1C7BD", "#A9E7C8", "#37534B", "#1C3832"),
        new("#FAF8F3", "#262725", "#77756D", "#BD3429", "#D8D5CD", "#F0EBE2"),
        new("#EDF2F8", "#173653", "#60768A", "#285CCB", "#CBD7E6", "#E0E8F3"),
        new("#F4EADB", "#3D352C", "#81715E", "#946331", "#D8C9B5", "#EBDFCC")
    ];

    public static BitmapSource Render(NewsItem item, string title, string summary, int style)
    {
        if (style < 0 || style >= Palettes.Length) throw new ArgumentOutOfRangeException(nameof(style));
        if (!Uri.TryCreate(item.Links.Original, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("这条资讯缺少有效的原文链接，暂时无法制作分享卡片。");
        title = title.Trim(); summary = summary.Trim();
        if (title.Length == 0 || title.Length > 220 || summary.Length > 600)
            throw new InvalidOperationException("请填写标题，并将标题控制在 220 字、摘要控制在 600 字以内。");

        var p = Palettes[style];
        Brush ink = B(p.Ink), muted = B(p.Muted), accent = B(p.Accent);
        const double left = 52, width = 616, titleY = 200, footerY = 752;
        double titleSize = title.Length <= 38 ? 48 : 43;
        double bodySize = 25;
        FormattedText heading = T(title, titleSize, ink, width, true, 1.32);
        FormattedText body = T(summary, bodySize, ink, width, false, 1.62);
        double bodyY = 0, bodyEnd = 0;
        bool fits = false;
        for (int step = 0; step < 12; step++)
        {
            heading = T(title, Math.Max(32, titleSize - step), ink, width, true, 1.32);
            body = T(summary, Math.Max(22, bodySize - step * .35), ink, width, false, 1.62);
            bodyY = titleY + heading.Height + 57;
            bodyEnd = summary.Length == 0 ? titleY + heading.Height : bodyY + body.Height;
            if (bodyEnd <= footerY - 32) { fits = true; break; }
        }
        if (!fits) throw new InvalidOperationException("文字较多，当前 3:4 卡片放不下。请精简标题或摘要后再试，正文不会被裁切。");

        var qr = Qr(item.Links.Original, out double qrSize);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(B(p.Paper), null, new Rect(0, 0, 720, 960));
            // Each edition has a distinct masthead; content and source keep the same reading order.
            if (style == 0)
            {
                dc.DrawRectangle(accent, null, new Rect(52, 0, 44, 9));
                var masthead = T("AI BRIEF", 48, ink, 390, true, 1.0, "Arial");
                dc.DrawText(masthead, new Point(left - 2, 46));
                double creditY = AlignedCredit(dc, masthead, 46, ink);
                Right(dc, "读一条，知新事。", 12, muted, 668, creditY + 26);
            }
            else if (style == 1)
            {
                dc.DrawEllipse(accent, null, new Point(70, 73), 18, 18);
                dc.DrawEllipse(B(p.Paper), null, new Point(79, 66), 16, 16);
                dc.DrawText(T("AFTER HOURS", 31, ink, 400, true, 1.0, "Arial"), new Point(109, 54));
                dc.DrawText(T("YuMir 的阅读室 / 夜读精选", 13, muted, 400), new Point(110, 93));
                Right(dc, "AI HOT", 13, accent, 668, 61);
            }
            else if (style == 2)
            {
                dc.DrawRectangle(accent, null, new Rect(0, 0, 720, 129));
                var masthead = T("AI HOT.", 56, Brushes.White, 400, true, 1, "Arial");
                dc.DrawText(masthead, new Point(left - 3, 39));
                double creditY = AlignedCredit(dc, masthead, 39, Brushes.White);
                Right(dc, "THE AI EDIT", 12, Brushes.White, 668, creditY + 26);
            }
            else if (style == 3)
            {
                dc.DrawRectangle(accent, null, new Rect(left, 48, 5, 58));
                dc.DrawText(T("AI / BRIEFING", 38, ink, 450, true, 1, "Arial"), new Point(76, 44));
                dc.DrawText(T("YuMir 的阅读室 · 科技简报", 13, muted, 400), new Point(78, 93));
                for (int i = 0; i < 4; i++) dc.DrawRectangle(accent, null, new Rect(612 + i * 14, 61 + i * 7, 5, 34 - i * 7));
            }
            else
            {
                dc.DrawText(T("阅 / 知新", 38, ink, 400, true, 1.0, "Microsoft YaHei UI"), new Point(left, 44));
                dc.DrawText(T("YuMir 的阅读室", 13, muted, 400), new Point(left + 2, 98));
                dc.DrawRectangle(accent, null, new Rect(617, 0, 51, 106));
                dc.DrawText(T("AI", 23, B(p.Paper), 45, true, 1, "Arial"), new Point(628, 54));
            }
            if (style != 2) dc.DrawLine(new Pen(B(p.Line), 1), new Point(left, 132), new Point(668, 132));
            dc.DrawEllipse(accent, null, new Point(left + 3, 164), 3, 3);
            dc.DrawText(T(item.CategoryLabel, 14, accent, 300, true), new Point(left + 16, 152));
            var date = (item.PublishedAt ?? item.DiscoveredAt).ToOffset(TimeSpan.FromHours(8));
            Right(dc, date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture), 14, muted, 668, 152);
            dc.DrawText(heading, new Point(left, titleY));
            if (summary.Length > 0)
            {
                double labelY = bodyY - 34;

                bool excerpt = !string.IsNullOrEmpty(item.Summary) && item.Summary.Trim().Length > summary.Length && item.Summary.Trim().StartsWith(summary, StringComparison.Ordinal);
                var label = T(excerpt ? "摘要节选 / 完整内容见原文" : "内容导读", 13, muted, width - 40);
                var labelOrigin = new Point(left + 34, labelY);
                var glyphBounds = label.BuildGeometry(labelOrigin).Bounds;
                dc.DrawRectangle(accent, null, new Rect(left, glyphBounds.Top + glyphBounds.Height / 2 - 1, 23, 2));
                dc.DrawText(label, labelOrigin);
                dc.DrawText(body, new Point(left, bodyY));
            }
            // The QR bitmap includes its quiet zone and uses exactly 3 physical pixels per module.
            dc.DrawLine(new Pen(B(p.Line), 1), new Point(left, footerY), new Point(668, footerY));
            double qrX = 668 - qrSize, qrY = footerY + 24;
            dc.DrawRectangle(Brushes.White, null, new Rect(qrX, qrY, qrSize, qrSize));
            dc.DrawImage(qr, new Rect(qrX, qrY, qrSize, qrSize));
            double sourceWidth = qrX - left - 30;
            dc.DrawText(T("阅读完整内容", 24, ink, sourceWidth, true), new Point(left, footerY + 30));
            string sourceName = item.Source.Name.Trim();
            if (sourceName.StartsWith("X:", StringComparison.OrdinalIgnoreCase) ||
                sourceName.StartsWith("X：", StringComparison.OrdinalIgnoreCase))
                sourceName = sourceName[2..].TrimStart();
            var source = T(sourceName, 14, muted, sourceWidth);
            source.MaxLineCount = 2; source.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(source, new Point(left, footerY + 77));
            var host = T(uri.Host, 12, muted, sourceWidth, false, 1.3, "Arial");
            host.MaxLineCount = 1; host.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(host, new Point(left, footerY + 125));
            dc.DrawText(T("YUMIR  /  每天一点 AI 新知", 11, muted, 410), new Point(left, 921));
            Right(dc, "扫码阅读原文 ↗", 11, muted, 668, 921);
        }
        var image = new RenderTargetBitmap(1080, 1440, 144, 144, PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze();
        return image;
    }

    private static BitmapSource Qr(string link, out double logicalSize)
    {
        using var data = QRCodeGenerator.GenerateQrCode(link, QRCodeGenerator.ECCLevel.Q);
        int modules = data.ModuleMatrix.Count;
        if (modules > 69) throw new InvalidOperationException("原文链接较长，二维码过密，暂不适合此卡片尺寸。");
        int pixelsPerModule = Math.Max(3, 180 / modules);
        logicalSize = modules * pixelsPerModule / 1.5;
        using var png = new PngByteQRCode(data);
        using var stream = new MemoryStream(png.GetGraphic(pixelsPerModule));
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
    private static SolidColorBrush B(string value) => new((Color)ColorConverter.ConvertFromString(value));
    private static FormattedText T(string text, double size, Brush color, double width, bool bold = false,
        double line = 1.5, string family = "Microsoft YaHei UI") => new(text, CultureInfo.GetCultureInfo("zh-CN"),
        FlowDirection.LeftToRight, new Typeface(new FontFamily(family), FontStyles.Normal,
        bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, color, 1.5)
        { MaxTextWidth = width, LineHeight = size * line };
    private static void Right(DrawingContext dc, string text, double size, Brush color, double right, double y)
    { var value = T(text, size, color, 350); dc.DrawText(value, new Point(right - value.Width, y)); }
    private static double AlignedCredit(DrawingContext dc, FormattedText masthead, double mastheadY, Brush color)
    {
        var credit = T("YuMir 的阅读室", 16, color, 350);
        double y = mastheadY + masthead.BuildGeometry(new Point()).Bounds.Top
            - credit.BuildGeometry(new Point()).Bounds.Top;
        dc.DrawText(credit, new Point(668 - credit.Width, y));
        return y;
    }
}
