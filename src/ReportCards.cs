using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiHot;
using QRCoder;

namespace YuMir.Cards;

public sealed record ReportSnapshot(int Period, DateTime Start, DateTime End, DateTimeOffset StartedAt, IReadOnlyList<NewsItem> Items)
{
    public string Name => Period switch { 1 => "日报", 2 => "周报", 3 => "月报", _ => throw new ArgumentOutOfRangeException(nameof(Period)) };
    public string Range => Period == 1 ? Start.ToString("yyyy.MM.dd") : $"{Start:yyyy.MM.dd} — {End.AddDays(-1):yyyy.MM.dd}";
    public string Coverage => Items.Count == 0 ? "本期暂无已存档资讯" :
        $"实际收录 {Items.Min(n => Date(n)):yyyy.MM.dd} — {Items.Max(n => Date(n)):yyyy.MM.dd}";
    public static DateTime Date(NewsItem item) => (item.PublishedAt ?? item.DiscoveredAt).ToOffset(TimeSpan.FromHours(8)).Date;
    public static NewsItem Copy(NewsItem n) => new() { Id = n.Id, Title = n.Title, Summary = n.Summary, Category = n.Category,
        Reason = n.Reason, Source = new NewsSource { Name = n.Source?.Name ?? "未知来源" },
        Links = new NewsLinks { Original = n.Links?.Original ?? "", Aihot = n.Links?.Aihot ?? "" }, PublishedAt = n.PublishedAt, DiscoveredAt = n.DiscoveredAt };
}

public sealed record ReportPage(NewsItem? Item, string Text, int ItemIndex, int Part, int Parts);

public sealed class ReportDocument
{
    public ReportSnapshot Snapshot { get; }
    public IReadOnlyList<NewsItem> Selected { get; }
    public string Lead { get; }
    public int Style { get; }
    public IReadOnlyList<ReportPage> Pages { get; }
    public ReportDocument(ReportSnapshot snapshot, IEnumerable<NewsItem> selected, string lead, int style)
    {
        if (style is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(style));
        if (lead.Length > 180) throw new InvalidOperationException("封面导读请控制在 180 字以内。");
        Snapshot = snapshot; Selected = selected.Select(ReportSnapshot.Copy).ToArray(); Lead = lead.Trim(); Style = style;
        var pages = new List<ReportPage> { new(null, "", 0, 0, 0) };
        for (int i = 0; i < Selected.Count; i++)
        {
            var item = Selected[i];
            var parts = ReportCards.Split(item);
            for (int j = 0; j < parts.Count; j++) pages.Add(new(item, parts[j], i + 1, j + 1, parts.Count));
        }
        Pages = pages;
    }
    private ReportDocument(ReportDocument source, int style)
    {
        Snapshot = source.Snapshot; Selected = source.Selected; Lead = source.Lead;
        Pages = source.Pages; Style = style;
    }
    public ReportDocument WithStyle(int style) => style is >= 0 and <= 4 ? new ReportDocument(this, style) : throw new ArgumentOutOfRangeException(nameof(style));
    public BitmapSource Render(int index) => ReportCards.Render(this, index);
    public static void Save(BitmapSource image, string path)
    {
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }
    public string PageName(int index) => index == 0 ? "001-报告封面.png" : $"{index + 1:000}-资讯{Pages[index].ItemIndex:000}-{Pages[index].Part:00}.png";
    public async Task<string> ExportAsync(string parent, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        // Write a unique partial folder, then rename only after all pages and the manifest succeed.
        string stem = $"YuMir-AI{Snapshot.Name}-{Snapshot.Start:yyyyMMdd}-{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        parent = Path.GetFullPath(parent);
        string partial = Path.GetFullPath(Path.Combine(parent, "." + stem + ".partial")), final = Path.Combine(parent, stem);
        Directory.CreateDirectory(partial);
        try
        {
            for (int i = 0; i < Pages.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                Save(Render(i), Path.Combine(partial, PageName(i)));
                progress?.Report(i + 1);
                await Task.Yield();
            }
            File.WriteAllText(Path.Combine(partial, "分享清单.json"), JsonSerializer.Serialize(new {
                report = Snapshot.Name, start = Snapshot.Start, endExclusive = Snapshot.End, archiveStartedAt = Snapshot.StartedAt,
                coverage = Snapshot.Coverage, archiveCount = Snapshot.Items.Count, selectedCount = Selected.Count,
                note = "仅汇总本机存档，可能不完整；分类统计按本期全部存档计算。", lead = Lead,
                style = EditorialCard.Names[Style], pages = Pages.Select((p, i) => new { file = PageName(i), id = p.Item?.Id, title = p.Item?.Title, original = p.Item?.Links.Original, p.Part, p.Parts })
            }, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Move(partial, final); return final;
        }
        catch
        {
            string boundary = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
            if (partial.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(partial) == "." + stem + ".partial"
                && Directory.Exists(partial) && (File.GetAttributes(partial) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(partial, true);
            throw;
        }
    }
}

public static class ReportCards
{
    private static SolidColorBrush B(string s) => new((Color)ColorConverter.ConvertFromString(s));
    private static FormattedText Text(string s, double size, Brush ink, double width = 616, bool bold = false, double line = 1.5) =>
        new(s, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, ink, 1.5)
        { MaxTextWidth = width, LineHeight = size * line };
    private static FormattedText Heading(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("有一条资讯缺少标题，无法生成卡片。");
        for (double size = 38; size >= 26; size -= 2)
        {
            var text = Text(title, size, Brushes.Black, 616, true, 1.35);
            if (text.Height <= 300) return text;
        }
        throw new InvalidOperationException("有一条资讯标题过长，请取消勾选后重试。");
    }
    public static IReadOnlyList<string> Split(NewsItem item)
    {
        double available = 742 - (190 + Heading(item.Title).Height + 52);
        string text = item.Summary ?? "";
        if (text.Length == 0) return new[] { "" };
        var parts = new List<string>(); int start = 0;
        int[] boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        while (start < text.Length)
        {
            int low = Array.BinarySearch(boundaries, start) + 1, high = boundaries.Length - 1, best = start;
            while (low <= high)
            {
                int mid = (low + high) / 2, end = boundaries[mid];
                if (Text(text[start..end], 24, Brushes.Black, 616, false, 1.65).Height <= available)
                { best = end; low = mid + 1; } else high = mid - 1;
            }
            if (best <= start) throw new InvalidOperationException("这条资讯的标题占用空间过多，无法排版。");
            if (best < text.Length)
            {
                int minimum = start + (int)((best - start) * .65);
                for (int k = best - 1; k >= minimum; k--)
                {
                    if ("。！？；\n.!?".Contains(text[k]) && Array.BinarySearch(boundaries, k + 1) >= 0)
                    { best = k + 1; break; }
                }
            }
            parts.Add(text[start..best]); start = best;
        }
        return parts;
    }
    public static BitmapSource Render(ReportDocument doc, int index)
    {
        var p = EditorialCard.GetPalette(doc.Style);
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(B(p.Paper), null, new Rect(0, 0, 720, 960));
            var ink = B(p.Ink); var muted = B(p.Muted); var accent = B(p.Accent); var line = new Pen(B(p.Line), 1);
            EditorialCard.DrawMasthead(dc, doc.Style);
            if (index == 0) { dc.PushTransform(new TranslateTransform(0,26)); Cover(dc, doc, ink, muted, accent, line); dc.Pop(); }
            else Article(dc, doc, index, ink, muted, accent, line);
            dc.DrawLine(line, new Point(52, 910), new Point(668, 910));
            dc.DrawText(Text("YUMIR / 每天一点AI新知", 11, muted), new Point(52, 924));
            Right(dc, $"{index + 1:00} / {doc.Pages.Count:00}", 12, muted, 668, 923);
        }
        var image = new RenderTargetBitmap(1080, 1440, 144, 144, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
    private static void Cover(DrawingContext dc, ReportDocument doc, Brush ink, Brush muted, Brush accent, Pen line)
    {
        dc.DrawText(Text($"AI {doc.Snapshot.Name}", 66, ink, 616, true, 1.2), new Point(48, 133));
        dc.DrawText(Text(doc.Snapshot.Range, 20, muted), new Point(52, 233));
        var values = new[] { doc.Snapshot.Items.Count, doc.Selected.Count, doc.Snapshot.Items.Select(n => n.Source.Name).Distinct().Count() };
        string[] labels = ["本期收录", "本次分享", "资讯来源"];
        for (int i = 0; i < 3; i++)
        {
            double x = 52 + i * 214;
            dc.DrawText(Text(values[i].ToString(), 43, accent, 180, true, 1.2), new Point(x, 293));
            dc.DrawText(Text(labels[i], 14, muted, 180), new Point(x, 354));
        }
        dc.DrawLine(line, new Point(52, 402), new Point(668, 402));
        double y = 425;
        var categories = doc.Snapshot.Items.GroupBy(n => n.CategoryLabel).OrderByDescending(g => g.Count()).ToArray();
        if (doc.Lead.Length > 0)
        {
            var lead = Text(doc.Lead, 21, ink, 616, false, 1.6);
            double available = 816 - y - 25 - 35 - Math.Max(1, categories.Length) * 32;
            for (double size = 20.5; lead.Height > available && size >= 16; size -= .5)
                lead = Text(doc.Lead, size, ink, 616, false, 1.6);
            if (lead.Height > available) throw new InvalidOperationException("封面导读换行较多，请减少换行或精简后重新生成。");
            dc.DrawText(lead, new Point(52, y)); y += lead.Height + 25;
        }
        dc.DrawText(Text("本期分类 / 全部收录", 14, muted), new Point(52, y)); y += 35;
        if (categories.Length == 0) { dc.DrawText(Text("本期暂无已存档资讯", 20, ink), new Point(52, y)); y += 42; }
        foreach (var category in categories)
        {
            var categoryText = Text(category.Key, 14, ink, 110);
                var bounds = categoryText.BuildGeometry(new Point(52, y)).Bounds;
                double centerY = bounds.Top + bounds.Height / 2;
                dc.DrawText(categoryText, new Point(52, y));
            dc.DrawRectangle(line.Brush, null, new Rect(165, centerY - 3.5, 430, 7));
            dc.DrawRectangle(accent, null, new Rect(165, centerY - 3.5, 430d * category.Count() / Math.Max(1, doc.Snapshot.Items.Count), 7));
            var number = Text(category.Count().ToString(), 14, ink, 110);
                var numberBounds = number.BuildGeometry(new Point(0,0)).Bounds;
                dc.DrawText(number, new Point(668 - number.Width, centerY - numberBounds.Top - numberBounds.Height / 2)); y += 32;
        }
        if (y > 816) throw new InvalidOperationException("封面导读较长，请精简后重新生成。");
        if (doc.Selected.Count > 0 && y < 717)
        {
            y += 18;
            dc.DrawText(Text("从这一条开始", 13, muted), new Point(52, y));
            var first = Text(doc.Selected[0].Title, 21, ink, 616, true, 1.45);
            first.MaxLineCount = 2; first.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(first, new Point(52, y + 28));
        }



    }
    private static void Article(DrawingContext dc, ReportDocument doc, int index, Brush ink, Brush muted, Brush accent, Pen line)
    {
        var page = doc.Pages[index]; var item = page.Item!;
        dc.DrawText(Text($"{page.ItemIndex:00} / {item.CategoryLabel}" + (page.Parts > 1 ? $"  ·  {page.Part}/{page.Parts} 续页" : ""), 13, accent), new Point(52, 149));
        Right(dc, doc.Snapshot.Period == 1 ? doc.Snapshot.Range : $"{doc.Snapshot.Start:MM.dd} — {doc.Snapshot.End.AddDays(-1):MM.dd}", 13, muted, 668, 149);
        var heading = Heading(item.Title); heading.SetForegroundBrush(ink);
        dc.DrawText(heading, new Point(52, 190));
        double bodyY = 190 + heading.Height + 52;
        dc.DrawText(Text(page.Part == 1 ? "资讯摘要" : "摘要 / 接上页", 13, muted), new Point(52, bodyY - 32));
        dc.DrawText(Text(page.Text.Length > 0 ? page.Text : "此条暂无摘要，请阅读原文。", 24, ink, 616, false, 1.65), new Point(52, bodyY));
        SourceFooter(dc, item, ink, muted, line);
    }
    private static void SourceFooter(DrawingContext dc, NewsItem item, Brush ink, Brush muted, Pen line, bool hasManifest = true)
    {
        dc.DrawLine(line, new Point(52, 763), new Point(668, 763));
        string sourceName = item.Source.Name.Trim();
        if (sourceName.StartsWith("X:", StringComparison.OrdinalIgnoreCase) || sourceName.StartsWith("X：", StringComparison.OrdinalIgnoreCase)) sourceName = sourceName[2..].TrimStart();
        bool valid = Uri.TryCreate(item.Links.Original, UriKind.Absolute, out var uri) && (uri.Scheme is "https" or "http") && uri.UserInfo.Length == 0;
        bool hasQr = false;
        if (valid)
        {
            using var data = QRCodeGenerator.GenerateQrCode(item.Links.Original, QRCodeGenerator.ECCLevel.Q);
            int modules = data.ModuleMatrix.Count;
            if (modules <= 69)
            {
                int scale = Math.Max(3, 165 / modules); double size = modules * scale / 1.5;
                using var png = new PngByteQRCode(data); using var stream = new MemoryStream(png.GetGraphic(scale));
                var qr = new BitmapImage(); qr.BeginInit(); qr.CacheOption = BitmapCacheOption.OnLoad; qr.StreamSource = stream; qr.EndInit(); qr.Freeze();
                dc.DrawImage(qr, new Rect(668 - size, Math.Min(782, 906 - size), size, size)); hasQr = true;
            }
        }
        dc.DrawText(Text(hasQr ? "阅读完整内容" : "原文入口", 23, ink, 430, true), new Point(52, 786));
        var source = Text(sourceName, 13, muted, 425); source.MaxLineCount = 2; source.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(source, new Point(52, 829));
        var linkNote = Text(hasQr ? uri!.Host : valid ? (hasManifest ? "链接较长，原文地址见导出清单" : "原文链接过长，二维码暂不可用") : "暂无有效原文链接", 11, muted, 430);
        linkNote.MaxLineCount = 1; linkNote.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(linkNote, new Point(52, 875));
    }
    public static BitmapSource RenderLongSection(ReportDocument doc, int index)
    {
        var p = EditorialCard.GetPalette(doc.Style);
        var ink = B(p.Ink); var muted = B(p.Muted); var accent = B(p.Accent); var line = new Pen(B(p.Line), 1);
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        double height;
        if (index == 0)
        {
            var categories = doc.Snapshot.Items.GroupBy(n => n.CategoryLabel).OrderByDescending(g => g.Count()).ToArray();
            var lead = Text(doc.Lead, 21, ink, 616, false, 1.6);
            double categoryY = 425 + (doc.Lead.Length > 0 ? lead.Height + 25 : 0);
            height = categoryY + 35 + Math.Max(1, categories.Length) * 32 + 52;
            if (doc.Pages.Count == 1) height += 32;
            using var dc = visual.RenderOpen();
            dc.DrawRectangle(B(p.Paper), null, new Rect(0, 0, 720, height + 1));
            EditorialCard.DrawMasthead(dc, doc.Style);
            dc.PushTransform(new TranslateTransform(0,26));
            dc.DrawText(Text($"AI {doc.Snapshot.Name}", 66, ink, 616, true, 1.2), new Point(48, 133));
            dc.DrawText(Text(doc.Snapshot.Range, 20, muted), new Point(52, 233));
            int[] values = [doc.Snapshot.Items.Count, doc.Selected.Count, doc.Snapshot.Items.Select(n => n.Source.Name).Distinct().Count()];
            string[] labels = ["本期收录", "本次分享", "资讯来源"];
            for (int i = 0; i < 3; i++)
            {
                double x = 52 + i * 214;
                dc.DrawText(Text(values[i].ToString(), 43, accent, 180, true, 1.2), new Point(x, 293));
                dc.DrawText(Text(labels[i], 14, muted, 180), new Point(x, 354));
            }
            dc.DrawLine(line, new Point(52, 402), new Point(668, 402));
            if (doc.Lead.Length > 0) dc.DrawText(lead, new Point(52, 425));
            dc.DrawText(Text("本期分类 / 全部收录", 14, muted), new Point(52, categoryY));
            double y = categoryY + 35;
            foreach (var category in categories)
            {
                var categoryText = Text(category.Key, 14, ink, 110);
                var bounds = categoryText.BuildGeometry(new Point(52, y)).Bounds;
                double centerY = bounds.Top + bounds.Height / 2;
                dc.DrawText(categoryText, new Point(52, y));
                dc.DrawRectangle(line.Brush, null, new Rect(165, centerY - 3.5, 430, 7));
                dc.DrawRectangle(accent, null, new Rect(165, centerY - 3.5, 430d * category.Count() / Math.Max(1, doc.Snapshot.Items.Count), 7));
                var number = Text(category.Count().ToString(), 14, ink, 110);
                var numberBounds = number.BuildGeometry(new Point(0,0)).Bounds;
                dc.DrawText(number, new Point(668 - number.Width, centerY - numberBounds.Top - numberBounds.Height / 2)); y += 32;
            }
            if (categories.Length == 0) dc.DrawText(Text("本期暂无已存档资讯", 20, ink), new Point(52, y));
            dc.Pop();
            if (doc.Pages.Count == 1) LongCoverage(dc, doc, muted, height - 40);
            dc.DrawLine(line, new Point(52, height - 1), new Point(668, height - 1));
        }
        else
        {
            var page = doc.Pages[index]; var item = page.Item!;
            var heading = Heading(item.Title); heading.SetForegroundBrush(ink);
            var summary = Text(page.Text.Length > 0 ? page.Text : "此条暂无摘要，请阅读原文。", 24, ink, 616, false, 1.65);
            double bodyY = 81 + heading.Height + 24;
            double footerY = bodyY + summary.Height + 28;
            height = footerY + 175 + (index == doc.Pages.Count - 1 ? 32 : 0);
            using var dc = visual.RenderOpen();
            dc.DrawRectangle(B(p.Paper), null, new Rect(0, 0, 720, height + 1));
            dc.DrawText(Text($"{page.ItemIndex:00}  /  {item.CategoryLabel}" + (page.Parts > 1 ? $" · {page.Part}/{page.Parts}" : ""), 14, accent), new Point(52, 35));
            Right(dc, ReportSnapshot.Date(item).ToString("yyyy.MM.dd"), 13, muted, 668, 35);
            dc.DrawText(heading, new Point(52, 81)); dc.DrawText(summary, new Point(52, bodyY));
            dc.PushTransform(new TranslateTransform(0, footerY - 763)); SourceFooter(dc, item, ink, muted, line, false); dc.Pop();
            if (index == doc.Pages.Count - 1)
            {
                LongCoverage(dc, doc, muted, height - 48);
                dc.DrawText(Text("YUMIR / 全文完", 11, muted), new Point(52, height - 23));
            }
        }
        var bitmap = new RenderTargetBitmap(1080, (int)Math.Ceiling(height * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    private static void LongCoverage(DrawingContext dc, ReportDocument doc, Brush muted, double y)
    {
        dc.DrawText(Text("本机存档 · 可能不完整 · 北京时间", 11, muted), new Point(52, y));
    }
    private static void Right(DrawingContext dc, string s, double size, Brush ink, double x, double y)
    { var text = Text(s, size, ink, 350); dc.DrawText(text, new Point(x - text.Width, y)); }
}

