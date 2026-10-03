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
    public static readonly string[] Names = ["编辑精选", "午夜薄荷", "朱红刊物", "蓝调简报", "暖纸书签", "极客终端", "星轨紫笺", "柠檬放映", "黑白报刊", "珊瑚便签"];
    internal sealed record Palette(string Paper, string Ink, string Muted, string Accent, string Line, string Panel);
    internal static Palette GetPalette(int style) => Palettes[style];
    internal static void DrawMasthead(DrawingContext dc, int style)
    {
        var p = Palettes[style]; const double left = 52;
        Brush ink = B(p.Ink), muted = B(p.Muted), accent = B(p.Accent);
            // Each edition has a distinct masthead; content and source keep the same reading order.
            if (style == 0)
            {
                var masthead = T("AI BRIEF", 44, ink, 390, true, 1.0, "Arial");
                dc.DrawText(masthead, new Point(left, 48));
                double creditY = AlignedCredit(dc, masthead, 48, ink);
            }
            else if (style == 1)
            {
                dc.DrawEllipse(accent, null, new Point(75, 72), 20, 20);
                dc.DrawEllipse(B(p.Paper), null, new Point(84, 64), 18, 18);
                dc.DrawText(T("AFTER HOURS", 32, ink, 400, true, 1.0, "Arial"), new Point(116, 48));
                dc.DrawText(T("YuMir 的阅读室 / 夜读精选", 13, muted, 400), new Point(117, 92));
                Right(dc, "NIGHT EDIT", 11, accent, 668, 60);
            }
            else if (style == 2)
            {
                dc.DrawRectangle(accent, null, new Rect(0, 0, 720, 132));
                var masthead = T("AI HOT.", 50, Brushes.White, 400, true, 1, "Arial");
                dc.DrawText(masthead, new Point(left, 42));
                double creditY = AlignedCredit(dc, masthead, 42, Brushes.White);
                Right(dc, "THE AI EDIT", 12, Brushes.White, 668, creditY + 26);
            }
            else if (style == 3)
            {
                dc.DrawRectangle(accent, null, new Rect(left, 46, 4, 60));
                dc.DrawText(T("AI / BRIEFING", 34, ink, 450, true, 1, "Arial"), new Point(72, 47));
                dc.DrawText(T("YuMir 的阅读室 · 科技简报", 13, muted, 400), new Point(73, 92));
                for (int i = 0; i < 4; i++) dc.DrawRectangle(accent, null, new Rect(610 + i * 14, 76 - i * 6, 5, 18 + i * 6));
            }
            else if (style == 4)
            {
                dc.DrawText(T("阅 / 知新", 38, ink, 400, true, 1.0, "Microsoft YaHei UI"), new Point(left, 46));
                dc.DrawText(T("YuMir 的阅读室", 13, muted, 400), new Point(left + 2, 96));
                var ribbon=new StreamGeometry();using(var path=ribbon.Open()){path.BeginFigure(new Point(617,0),true,true);path.LineTo(new Point(668,0),true,false);path.LineTo(new Point(668,112),true,false);path.LineTo(new Point(642.5,99),true,false);path.LineTo(new Point(617,112),true,false);}dc.DrawGeometry(accent,null,ribbon);
                dc.DrawText(T("AI", 23, B(p.Paper), 45, true, 1, "Arial"), new Point(628, 50));
            }
            else if(style==5)
            {
                dc.DrawRoundedRectangle(B(p.Panel),new Pen(B(p.Line),1),new Rect(left,38,616,78),8,8);
                dc.DrawText(T(">_ AI LOG",32,accent,370,true,1,"Consolas"),new Point(left+18,47));
                Right(dc,"YuMir / 阅读室",12,muted,648,65);
                dc.DrawText(T("SYSTEM / KNOWLEDGE FEED",9,muted,300,false,1,"Consolas"),new Point(left+20,92));
            }
            else if(style==6)
            {
                dc.DrawEllipse(null,new Pen(accent,2),new Point(83,74),27,27);
                dc.DrawEllipse(accent,null,new Point(105,56),5,5);
                dc.DrawText(T("ORBIT",38,ink,400,true,1,"Arial"),new Point(128,46));
                dc.DrawText(T("YuMir 的阅读室 · 灵感环游",13,muted,440),new Point(128,94));
                dc.DrawLine(new Pen(accent,1.5),new Point(651,53),new Point(651,77));dc.DrawLine(new Pen(accent,1.5),new Point(639,65),new Point(663,65));
            }
            else if(style==7)
            {
                dc.DrawRoundedRectangle(B("#E6CE42"),null,new Rect(left,43,64,64),32,32);
                dc.DrawText(T("AI",28,ink,62,true,1,"Arial"),new Point(left+13,56));
                dc.DrawText(T("FRESH TAKE",35,ink,490,true,1,"Arial"),new Point(136,46));
                dc.DrawText(T("YuMir / 新鲜视角 · 每日上映",13,muted,490),new Point(138,92));
            }
            else if(style==8)
            {
                var group=StackedMasthead(dc,"新知观察",35,ink,muted,126,48);
                var mark=new Rect(left,group.Top+(group.Height-58)/2,58,58);
                dc.DrawRectangle(ink,null,mark);
                Centered(dc,T("AI",28,Brushes.White,54,true,1,"Arial"),mark);
                Right(dc,"阅读 / 记录",11,muted,668,58);
                dc.DrawLine(new Pen(ink,3),new Point(left,130),new Point(668,130));
                dc.DrawLine(new Pen(ink,1),new Point(left,137),new Point(668,137));
            }
            else
            {
                var group=StackedMasthead(dc,"新知手记",35,ink,muted,72,48);
                dc.DrawRoundedRectangle(accent,null,new Rect(left,group.Top,3,group.Height),1.5,1.5);
                dc.DrawEllipse(B(p.Panel),null,new Point(643,72),25,25);
                Centered(dc,T("AI",20,accent,45,true,1,"Arial"),new Rect(618,47,50,50));
            }
            if (style != 2 && style != 8) dc.DrawLine(new Pen(B(p.Line), 1), new Point(left, 132), new Point(668, 132));
    }
    private static readonly Palette[] Palettes = [
        new("#F6F3EC", "#242721", "#70726B", "#C94F35", "#D8D6CC", "#EDEAE1"),
        new("#132D29", "#F0F4E9", "#B1C7BD", "#A9E7C8", "#37534B", "#1C3832"),
        new("#FAF8F3", "#262725", "#77756D", "#BD3429", "#D8D5CD", "#F0EBE2"),
        new("#EDF2F8", "#173653", "#60768A", "#285CCB", "#CBD7E6", "#E0E8F3"),
        new("#F4EADB", "#3D352C", "#81715E", "#946331", "#D8C9B5", "#EBDFCC"),
        new("#101B20", "#E3F3ED", "#9CB3AA", "#9EF0A3", "#31443D", "#192A25"),
        new("#F1ECFA", "#322947", "#786C8E", "#8058B5", "#D9CCE9", "#E6DCF3"),
        new("#FFFCEB", "#292C20", "#787A61", "#887323", "#E4E1C8", "#F5F0CB"),
        new("#FAFAF8", "#202220", "#666963", "#202220", "#CACEC5", "#EEEFE9"),
        new("#FFF8F3", "#46332F", "#856C63", "#BF654F", "#ECDAD0", "#F9E4DB")
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
        const double left = 52, titleY = 200;
        double textLeft=style == 5 ? 72 : style==6 ? 66 : left;
        double width=668-textLeft-(style == 5 ? 20 : 0);
        double footerY=752;
        double titleSize=(title.Length<=38?48:43)+(style==7?2:style is 5 or 8?-2:0);
        double bodySize=style==8?24:25;
        double titleLine=style is 2 or 8?1.24:1.32;
        double bodyLine=style==8?1.5:style is 1 or 6?1.7:1.62;
        double gap=style is 5 or 9?54:style==8?44:52;
        FormattedText heading = T(title, titleSize, ink, width, true, titleLine);
        FormattedText body = T(summary, bodySize, ink, width, false, bodyLine);
        double bodyY = 0, bodyEnd = 0;
        bool fits = false;
        for (int step = 0; step < 12; step++)
        {
            heading = T(title, Math.Max(32, titleSize - step), ink, width, true, titleLine);
            body = T(summary, Math.Max(22, bodySize - step * .35), ink, width, false, bodyLine);
            bodyY = titleY + heading.Height + gap;
            bodyEnd = summary.Length == 0 ? titleY + heading.Height : bodyY + body.Height;
            if (bodyEnd <= footerY - 32) { fits = true; break; }
        }
        if (!fits) throw new InvalidOperationException("文字较多，当前 3:4 卡片放不下。请精简标题或摘要后再试，正文不会被裁切。");

        footerY=Math.Clamp(bodyEnd+64,704,752);
        var qr = Qr(item.Links.Original, out double qrSize);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(B(p.Paper), null, new Rect(0, 0, 720, 960));
            DrawMasthead(dc, style);
            dc.DrawEllipse(accent, null, new Point(left + 3, 164), 3, 3);
            dc.DrawText(T(item.CategoryLabel, 14, accent, 300, true), new Point(left + 16, 152));
            var date = (item.PublishedAt ?? item.DiscoveredAt).ToOffset(TimeSpan.FromHours(8));
            Right(dc, date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture), 14, muted, 668, 152);
            if(style==5)dc.DrawRoundedRectangle(B(p.Panel),null,new Rect(left,titleY-16,616,Math.Max(100,bodyEnd-titleY+40)),style==5?6:12,style==5?6:12);
            if(style==6)dc.DrawLine(new Pen(B(p.Line),2),new Point(left,titleY+5),new Point(left,bodyEnd));
            if(style==9)dc.DrawLine(new Pen(B(p.Line),1),new Point(left,bodyEnd+24),new Point(left+100,bodyEnd+24));
            dc.DrawText(heading, new Point(textLeft, titleY));
            if (summary.Length > 0)
            {
                double labelY = bodyY - 34;

                bool excerpt = !string.IsNullOrEmpty(item.Summary) && item.Summary.Trim().Length > summary.Length && item.Summary.Trim().StartsWith(summary, StringComparison.Ordinal);
                var label = T(excerpt ? "摘要节选" : "内容导读", 13, muted, width - 34);
                var labelOrigin = new Point(textLeft + 28, labelY);
                var glyphBounds = label.BuildGeometry(labelOrigin).Bounds;
                dc.DrawRectangle(accent, null, new Rect(textLeft, glyphBounds.Top + glyphBounds.Height / 2 - 1, 18, 2));
                dc.DrawText(label, labelOrigin);
                dc.DrawText(body, new Point(textLeft, bodyY));
            }
            // The QR bitmap includes its quiet zone and uses exactly 3 physical pixels per module.
            dc.DrawLine(new Pen(B(p.Line), 1), new Point(left, footerY), new Point(668, footerY));
            double qrX = 668 - qrSize, qrY = footerY + 24;
            dc.DrawRectangle(Brushes.White, null, new Rect(qrX, qrY, qrSize, qrSize));
            dc.DrawImage(qr, new Rect(qrX, qrY, qrSize, qrSize));
            double sourceWidth = qrX - left - 30;
            dc.DrawText(T("扫码阅读原文", 21, ink, sourceWidth, true), new Point(left, footerY + 24));
            string sourceName = item.Source.Name.Trim();
            if (sourceName.StartsWith("X:", StringComparison.OrdinalIgnoreCase) ||
                sourceName.StartsWith("X：", StringComparison.OrdinalIgnoreCase))
                sourceName = sourceName[2..].TrimStart();
            var source = T(sourceName, 14, muted, sourceWidth);
            source.MaxLineCount = 2; source.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(source, new Point(left, footerY + 62));
            var host = T(uri.Host, 12, muted, sourceWidth, false, 1.3, "Arial");
            host.MaxLineCount = 1; host.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(host, new Point(left, footerY + 107));
            dc.DrawText(T("YuMir 的阅读室", 12, muted, 410), new Point(left, 921));
            Right(dc, Names[style], 11, muted, 668, 921);
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
    private static Rect StackedMasthead(DrawingContext dc,string title,double size,Brush ink,Brush muted,double x,double top)
    {
        var heading=T(title,size,ink,390,true);var h=heading.BuildGeometry(new Point()).Bounds;
        var credit=T("YuMir 的阅读室",12,muted,390);var c=credit.BuildGeometry(new Point()).Bounds;
        dc.DrawText(heading,new Point(x-h.X,top-h.Y));
        double creditTop=top+h.Height+10;
        dc.DrawText(credit,new Point(x-c.X,creditTop-c.Y));
        return new Rect(x,top,Math.Max(h.Width,c.Width),creditTop+c.Height-top);
    }
    private static void Centered(DrawingContext dc,FormattedText text,Rect frame)
    {
        var bounds=text.BuildGeometry(new Point()).Bounds;
        dc.DrawText(text,new Point(frame.X+(frame.Width-bounds.Width)/2-bounds.X,frame.Y+(frame.Height-bounds.Height)/2-bounds.Y));
    }
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



