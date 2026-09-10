using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiHot;
using YuMir.Cards;

static class ReportTests
{
    private static int checks;
    private sealed class CallbackProgress(Action<int> callback) : IProgress<int> { public void Report(int value) => callback(value); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static NewsArchive Read(string path) => JsonSerializer.Deserialize<NewsArchive>(File.ReadAllText(path))!;
    static void Initialize()
    {
        if (Application.Current == null) { var app = new AiHot.App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown; }
        typeof(NewsItem).Assembly.GetType("AiHot.Theme")!.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false]);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    }
    public static void Run(string output, string archivePath)
    {
        Initialize(); Directory.CreateDirectory(output);
        var archive = Read(archivePath);
        var anchor = ReportSnapshot.Date(archive.Items[0]);
        var selected = archive.Items.Take(3).Reverse().Select(ReportSnapshot.Copy).ToArray();
        for (int period = 1; period <= 3; period++)
        {
            var snapshot = Snapshot(archive, period, anchor);
            for (int style = 0; style < 5; style++)
            {
                var doc = new ReportDocument(snapshot, snapshot.Items.Take(3), "把这一期值得关注的 AI 动态整理在一起。", style);
                var image = doc.Render(0);
                Check(image.PixelWidth == 1080 && image.PixelHeight == 1440, "Report resolution");
                ReportDocument.Save(image, Path.Combine(output, $"cover-{period}-{style}.png"));
                if (doc.Pages.Count > 1) ReportDocument.Save(doc.Render(1), Path.Combine(output, $"article-{period}-{style}.png"));
                Check(!doc.Pages[0].Item?.Links.Original.Any() ?? true, "Cover has no fake QR link");
            }
        }
        var week = Snapshot(archive, 2, anchor);
        Check(week.Start.DayOfWeek == DayOfWeek.Monday && week.End == week.Start.AddDays(7), "Monday week boundaries");
        var month = Snapshot(archive, 3, new DateTime(2024, 2, 29));
        Check(month.Start == new DateTime(2024, 2, 1) && month.End == new DateTime(2024, 3, 1), "Leap month boundaries");
        var empty = new ReportSnapshot(1, anchor, anchor.AddDays(1), default, []);
        var emptyDocument = new ReportDocument(empty, [], "", 0);
        Check(emptyDocument.Pages.Count == 1, "Empty report has one cover");
        ReportDocument.Save(emptyDocument.Render(0), Path.Combine(output, "empty.png"));
        var reordered = new ReportDocument(week, selected, "", 0);
        Check(reordered.Pages[1].Item!.Id == selected[0].Id, "Selection order preserved");
        var longItem = ReportSnapshot.Copy(selected[0]);
        longItem.Title = "长摘要分页测试 · 完整保留限定与上下文";
        longItem.Summary = string.Concat(Enumerable.Repeat("这是带有中文与 English 的完整句子，包含 emoji 👩‍💻 和组合字符 e\u0301。\n但是，初步结论仍需结合原文理解。", 24));
        var parts = ReportCards.Split(longItem);
        Check(parts.Count > 2 && string.Concat(parts) == longItem.Summary, "Pagination preserves every text element and qualifier");
        var longDoc = new ReportDocument(week, [longItem], "", 0);
        for (int i = 0; i < longDoc.Pages.Count; i++) ReportDocument.Save(longDoc.Render(i), Path.Combine(output, $"long-{i:00}.png"));
        var noSummary = ReportSnapshot.Copy(selected[0]); noSummary.Summary = null;
        Check(ReportCards.Split(noSummary).Count == 1, "Missing summary still produces source card");
        var invalid = ReportSnapshot.Copy(selected[0]); invalid.Links.Original = "javascript:alert(1)";
        ReportDocument.Save(new ReportDocument(week, [invalid], "", 0).Render(1), Path.Combine(output, "invalid-link.png"));
        var manyCategories = new[] {"ai-models","ai-products","industry","paper","tip", "unknown"}.Select((c, i) => { var n = ReportSnapshot.Copy(selected[0]); n.Id = i.ToString(); n.Category = c; return n; }).ToArray();
        var full = new ReportSnapshot(2, week.Start, week.End, archive.StartedAt, manyCategories);
        try { new ReportDocument(full, manyCategories, new string('文', 181), 0); throw new Exception("Lead cap ignored"); }
        catch (InvalidOperationException) { checks++; }
        ReportDocument.Save(new ReportDocument(full, manyCategories, new string('文', 180), 0).Render(0), Path.Combine(output, "maximum-lead.png"));
        var exportRoot = Path.Combine(output, "exports"); Directory.CreateDirectory(exportRoot);
        var exported = Pump(() => reordered.ExportAsync(exportRoot));
        Check(Directory.GetFiles(exported, "*.png").Length == reordered.Pages.Count, "Export page count");
        using(var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(exported, "分享清单.json"))))
            Check(manifest.RootElement.GetProperty("pages")[1].GetProperty("id").GetString() == selected[0].Id, "Export manifest order");
        var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { Pump(() => reordered.ExportAsync(exportRoot, cancellation: cancel.Token)); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { checks++; }
        Check(!Directory.GetDirectories(exportRoot).Any(p => p.EndsWith(".partial")), "Canceled export cleanup");
        var midway = new CancellationTokenSource();
        try { Pump(() => reordered.ExportAsync(exportRoot, new CallbackProgress(n => { if(n == 1) midway.Cancel(); }), midway.Token)); throw new Exception("Mid-export cancellation ignored"); }
        catch (OperationCanceledException) { checks++; }
        Check(!Directory.GetDirectories(exportRoot).Any(p => p.EndsWith(".partial")), "Partial pages cleaned after cancellation");
        var all = new ReportDocument(Snapshot(archive, 3, anchor), Snapshot(archive, 3, anchor).Items, "", 0);
        foreach(var item in all.Selected)
            Check(string.Concat(all.Pages.Where(p => p.Item?.Id == item.Id).Select(p => p.Text)) == (item.Summary ?? ""), "Full month summaries preserved");
        var longSize = Pump(() => LongReportPng.SaveAsync(reordered, Path.Combine(output, "报告长图示例.png")));
        Check(longSize.Width == 1080 && longSize.Height > 1440, "Single long PNG includes multiple stories");
        File.WriteAllText(Path.Combine(output, "long-sections.json"), JsonSerializer.Serialize(reordered.Pages.Select((p,i) => new { height = ReportCards.RenderLongSection(reordered,i).PixelHeight, original = p.Item?.Links.Original })));
        var fullSize = Pump(() => LongReportPng.SaveAsync(all, Path.Combine(output, "完整月报长图.png")));
        Check(fullSize.Height > longSize.Height && all.Selected.Count == Snapshot(archive,3,anchor).Items.Count, "Entire month fits one streamed PNG");
        string keep = Path.Combine(output, "cancel-long.png"); File.WriteAllText(keep, "existing file");
        var cancelLong = new CancellationTokenSource();
        try { Pump(() => LongReportPng.SaveAsync(reordered, keep, new CallbackProgress(n => cancelLong.Cancel()), cancelLong.Token)); throw new Exception("Long export cancellation ignored"); }
        catch(OperationCanceledException) { checks++; }
        Check(File.ReadAllText(keep) == "existing file" && !Directory.GetFiles(output,"*.partial").Any(), "Canceled long PNG preserves original file and removes temporary file");
        // Exercise the patched real report panel and current-period snapshot.
        var assembly = typeof(NewsItem).Assembly;
        var storage = assembly.GetType("AiHot.Storage")!;
        string testProfile = Path.Combine(output, "profile"); Directory.CreateDirectory(testProfile);
        storage.GetField("Root", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, testProfile);
        File.WriteAllText(Path.Combine(testProfile, "history.json"), JsonSerializer.Serialize(archive));
        for(int period = 1; period <= 3; period++)
        {
            var panelType = assembly.GetType("AiHot.ReportPanel")!;
            var panel = (DockPanel)Activator.CreateInstance(panelType, BindingFlags.Instance | BindingFlags.NonPublic, null, [period], null)!;
            panelType.GetField("anchor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, anchor);
            panelType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(panel, null);
            var button = Find<Button>(panel).Single(b => Equals(b.Content, "分享报告"));
            Check(Find<Button>(panel).Count(b => Equals(b.Content, "分享报告")) == 1, "No duplicated share buttons");
            var window = OpenSilently(panel);
            var actual = (ReportSnapshot)typeof(ReportShareWindow).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Check(actual.Start == Snapshot(archive, period, anchor).Start && actual.Period == period, "Report share uses selected period");
            Capture(window, Path.Combine(output, $"editor-{period}.png"));
            if (period == 2)
            {
                var originalChoices = actual.Items.Select(n => new ReportPicker.Choice(n, true)).ToList();
                var picker = new ReportPicker(originalChoices);
                Capture(picker, Path.Combine(output, "picker.png"));
                Check(picker.VisibleChoices().Count() == actual.Items.Count, "Picker defaults all entries");
                var savedRow = Find<Button>(picker).First(b => b.Content is DockPanel);
                var query = Find<TextBox>(picker).Single(); query.Text = actual.Items[0].Title;
                Check(picker.VisibleChoices().Any() && picker.VisibleChoices().All(c => c.Item.Title.Contains(query.Text) || (c.Item.Summary ?? "").Contains(query.Text)), "Picker search");
                var matched = picker.VisibleChoices().ToArray();
                Find<Button>(picker).Single(b => Equals(b.Content, "取消当前结果")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(matched.All(c => !c.Selected) && picker.Choices.Except(matched).All(c => c.Selected), "Bulk cancel only affects results");
                Check(originalChoices.All(c => c.Selected), "Draft edits leave original selection unchanged");
                query.Clear();
                Check(Find<Button>(picker).Contains(savedRow), "Filtering reuses original row controls");
                var first = picker.Choices[0]; var third = picker.Choices[2]; picker.Reorder(first, third);
                Check(picker.Choices.IndexOf(first) == 2, "Drag reorder preserves explicit order");
                Find<Button>(picker).Single(b => b.Content is string t && t.StartsWith("已选资讯 (")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(picker.VisibleChoices().All(c => c.Selected), "Selected view only shows included entries");
                Capture(picker, Path.Combine(output, "picker-selected.png"));
                picker.Close();
                Check(!picker.Accepted, "Closing picker does not commit changes");
            }            window.Close();
            panelType.GetMethod("Shift", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, [-1]);
            panelType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(panel, null);
            window = OpenSilently(panel);
            var older = (ReportSnapshot)typeof(ReportShareWindow).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Check(older.Start < actual.Start && older.End <= actual.Start, "Previous-period share refreshes snapshot"); window.Close();
        }
        File.WriteAllText(Path.Combine(output, "report-verification.txt"), $"PASS: {checks} checks. Five styles × daily/weekly/monthly, selected period binding, pagination, order, empty report, export, cancellation.");
        Console.WriteLine(File.ReadAllText(Path.Combine(output, "report-verification.txt")));
    }
    static ReportSnapshot Snapshot(NewsArchive archive, int period, DateTime anchor)
    {
        var type = typeof(NewsItem).Assembly.GetType("AiHot.ReportData")!;
        var data = type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [archive.Items, period, anchor, DateTimeOffset.UtcNow])!;
        object Field(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(data)!;
        return new(period, (DateTime)Field("Start"), (DateTime)Field("End"), archive.StartedAt, ((List<NewsItem>)Field("Items")).Select(ReportSnapshot.Copy).ToArray());
    }
    static T Pump<T>(Func<Task<T>> action)
    {
        var frame = new DispatcherFrame(); T result = default!; Exception? error = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () => { try { result = await action(); } catch(Exception ex) { error = ex; } finally { frame.Continue = false; } });
        Dispatcher.PushFrame(frame); if(error != null) throw error; return result;
    }
    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root)) if(child is DependencyObject node) { if(node is T t) yield return t; foreach(var descendant in Find<T>(node)) yield return descendant; }
    }
    static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, window.Height)); content.Arrange(new Rect(0, 0, window.Width, window.Height)); content.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); image.Render(content); ReportDocument.Save(image, path);
    }
    static ReportShareWindow OpenSilently(DockPanel panel)
    {
        var states = typeof(ReportSharing).GetField("States", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object?[] args = [panel, null];
        Check((bool)states.GetType().GetMethod("TryGetValue")!.Invoke(states, args)!, "Report snapshot registered");
        var snapshot = (ReportSnapshot)args[1]!.GetType().GetField("Snapshot")!.GetValue(args[1])!;
        return new ReportShareWindow(snapshot) { ShowActivated = false, ShowInTaskbar = false };
    }
    public static void Show(string archivePath)
    {
        Initialize(); var archive = Read(archivePath); var window = new ReportShareWindow(Snapshot(archive, 2, ReportSnapshot.Date(archive.Items[0])));
        window.Show(); Application.Current.Run();
    }
}
