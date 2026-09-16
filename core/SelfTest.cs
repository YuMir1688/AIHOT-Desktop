using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace AiHot;
internal static class SelfTest
{
    internal static async Task<int> Run()
    {
        Storage.Root = Path.Combine(Path.GetTempPath(), "AIHOT-tests-" + Guid.NewGuid().ToString("N"));
        int passed = 0;
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
        try
        {
            var config = new Settings();
            double smoothPosition = 0;
            for (int frame = 0; frame < 120; frame++) { double nextPosition = SmoothScroll.Next(smoothPosition, 500, 1.0 / 60); Assert(nextPosition >= smoothPosition && nextPosition <= 500, "smooth scroll monotonic no overshoot " + frame); smoothPosition = nextPosition; }
            Assert(Math.Abs(smoothPosition - 500) < .35, "smooth scroll settles");
            Assert(SmoothScroll.Next(400, 100, 1.0 / 60) < 400, "smooth scroll reverses immediately");
            Assert(SmoothScroll.Next(0, 500, 1) < 500, "slow frame cannot jump to destination");
            var handler = new Handler();
            using var service = new NewsService(handler);
            Assert(await service.Refresh(config), "200 refresh");
            Assert(service.Cache.Items.Count == 1, "deduplication and malformed item filtering");
            Assert(service.Cache.Items[0].Links.Original == "https://example.com/story", "v1 nested links");
            Assert(!await service.Refresh(config) && handler.SawETag, "304 conditional fetch");
            config.Category = "paper"; Assert(await service.Refresh(config) && handler.CleanQuery, "query change clears ETag");
            try { await service.Refresh(config); throw new Exception("Expected 429"); }
            catch (FeedException e) { Assert(e.Retry == TimeSpan.FromMinutes(10), "Retry-After"); }
            Assert(service.Cache.Items.Count == 1, "error preserves cached feed");
            using var restored = new NewsService(new Handler());
            Assert(restored.Cache.Items.Count == 1, "disk cache restores");
            config.Speed = 65; config.Category = ""; config.HideFullscreen = false; Storage.Save("settings.json", config); Assert(Storage.Read<Settings>("settings.json").Speed == 65, "settings roundtrip");
            var day = DateTimeOffset.Parse("2026-09-09T12:00:00+08:00");
            var dayItems = new[] { new NewsItem { Id = "yesterday", PublishedAt = day.Date.AddSeconds(-1) - TimeSpan.FromHours(8) }, new NewsItem { Id = "today", PublishedAt = DateTimeOffset.Parse("2026-09-08T16:00:00Z") }, new NewsItem { Id = "future", PublishedAt = day.AddHours(1) } };
            Assert(NewsService.FilterToday(dayItems, day).Select(n => n.Id).SequenceEqual(new[] { "today" }), "Beijing midnight boundary and future exclusion");
            Assert(NewsService.FilterToday(dayItems, DateTimeOffset.Parse("2026-09-10T00:00:00+08:00")).Count == 0, "next day expires old cache");
            Assert(NewsService.Query(new Settings()).Contains("window=7d&by=published"), "backfill query covers yesterday and orders by publication");
            var yesterday = new NewsItem { Id = "yesterday", Title = "Yesterday", PublishedAt = DateTimeOffset.Parse("2026-09-08T00:00:00+08:00") };
            var older = new NewsItem { Id = "older", Title = "Older", PublishedAt = DateTimeOffset.Parse("2026-09-07T23:59:59+08:00") };
            var todayItem = new NewsItem { Id = "today", Title = "Today", PublishedAt = day.AddHours(-1) };
            Assert(NewsService.LatestHundred(new[] { older, yesterday, todayItem, todayItem, dayItems[2] }, day).Select(n => n.Id).SequenceEqual(new[] { "today", "yesterday", "older" }), "today first; older days backfill; duplicates and future excluded");
            var hundred = Enumerable.Range(0, 100).Select(i => new NewsItem { Id = "today-" + i, Title = "News " + i, PublishedAt = day.AddMinutes(-i) }).ToList();
            Assert(NewsService.LatestHundred(hundred.Append(yesterday), day).All(n => n.Id.StartsWith("today-")), "100 today excludes yesterday");
            Assert(NewsService.LatestHundred(hundred.Take(99).Append(yesterday), day).Count == 100, "99 today plus yesterday fills 100");
            Assert(NewsService.LatestHundred(new[] { yesterday, todayItem }, day.AddDays(1)).Count == 2, "midnight retains older backfill");
            Assert(NewsService.LatestHundred(Array.Empty<NewsItem>(), day).Count == 0, "empty feed");
            var future = new NewsItem { Id = "future", Title = "Future", PublishedAt = day.AddSeconds(1) };
            var fallback = new NewsItem { Id = "fallback", Title = "Fallback", DiscoveredAt = day };
            Assert(NewsService.LatestHundred(new[] { future, fallback, new NewsItem() }, day).Single().Id == "fallback", "future, malformed and missing publication date");
            var stressClock = System.Diagnostics.Stopwatch.StartNew();
            var large = Enumerable.Range(0, 100000).Select(i => new NewsItem { Id = "id-" + (i % 10000), Title = "Stress " + i, PublishedAt = day.AddMinutes(-i) }).Reverse().ToArray();
            for (int run = 0; run < 30; run++)
                Assert(NewsService.LatestHundred(large, day).Select(n => n.Id).SequenceEqual(Enumerable.Range(0, 100).Select(i => "id-" + i)), "100k feed sort/dedup stress " + run);
            long feedStressMs = stressClock.ElapsedMilliseconds;
            using var live = new NewsService();
            var reportDay = ReportData.Create(new[] { todayItem, yesterday, older, todayItem }, 1, day.Date, day);
            Assert(reportDay.Items.Count == 1 && reportDay.Start == day.Date, "daily Beijing date selection and dedup");
            var reportWeek = ReportData.Create(new[] { todayItem, yesterday, older }, 2, day.Date, day);
            Assert(reportWeek.Start == new DateTime(2026, 9, 7) && reportWeek.End == new DateTime(2026, 9, 14) && reportWeek.Items.Count == 3, "Monday based calendar week");
            var reportMonth = ReportData.Create(new[] { todayItem }, 3, new DateTime(2026, 12, 31), day);
            Assert(reportMonth.Start == new DateTime(2026, 12, 1) && reportMonth.End == new DateTime(2027, 1, 1) && reportMonth.Items.Count == 0, "month boundary across year and empty month");
            Assert(ReportData.Create(Array.Empty<NewsItem>(), 3, new DateTime(2024, 2, 15), day).End == new DateTime(2024, 3, 1), "leap month range");
            NewsArchive.Record(new[] { todayItem, yesterday }); NewsArchive.Record(new[] { todayItem, older });
            Assert(NewsArchive.Read().Items.Count(n => n.Id == "today") == 1 && NewsArchive.Read().Items.Any(n => n.Id == "older"), "history persists and merges without duplicates");
            await live.Refresh(new Settings()); Assert(live.Cache.Items.Count > 0, "live v1 endpoint");
            var liveVisible = live.VisibleItems(new Settings());
            Assert(SharePoster.SuggestedLead(null) == "" && SharePoster.SuggestedLead(" 短摘要。 ") == "短摘要。", "lead handles empty and short text");
            Assert(SharePoster.SuggestedLead("完整句子。" + new string('文', 190)) == "完整句子。", "lead keeps whole sentence");
            Assert(SharePoster.SuggestedLead("他说：“完整句子。”" + new string('文', 190)) == "他说：“完整句子。”", "lead retains closing quote");
            string qualified = "初步结果。" + "但是" + new string('文', 190);
            Assert(SharePoster.SuggestedLead(qualified) == qualified, "lead preserves following qualifier");
            Assert(SharePoster.SuggestedLead(new string('文', 190)).Length == 190, "lead never truncates incomplete sentence");
            var excerptItem = new NewsItem { Title = "分享卡片：先看懂，再深入阅读", Summary = "这张卡片优先呈现核心信息，并保留完整句子供读者快速浏览。" + new string('文', 190), Source = new() { Name = "排版测试" }, Links = new() { Original = "https://example.com/article?a=1&b=2" }, PublishedAt = DateTimeOffset.Now };
            SharePoster.Save(SharePoster.Render(excerptItem, excerptItem.Title, SharePoster.SuggestedLead(excerptItem.Summary), 0), Path.Combine(AppContext.BaseDirectory, "share-excerpt.png"));
            Assert(!SharePoster.ValidLink("javascript:alert(1)") && !SharePoster.ValidLink("file:///test") && SharePoster.ValidLink("https://example.com/article?a=1&b=2"), "share URL validation");
            var shareFixture = new NewsItem { Title = "AI 新进展：让重要的信息，值得被分享", Summary = "一张清晰的资讯卡片，保留标题、摘要与来源。扫码直达原文，无需跳转中间页面。", Source = new() { Name = "AIHOT · 分享功能测试" }, Links = new() { Original = "https://example.com/article?a=1&b=2" }, Category = "ai-products", PublishedAt = DateTimeOffset.Now };
            foreach (bool darkPoster in new[] { false, true }) { var poster = SharePoster.Render(shareFixture, shareFixture.Title, shareFixture.Summary, darkPoster); Assert(poster.PixelWidth == 720 && poster.PixelHeight == 960, "fixed 3:4 share poster"); SharePoster.Save(poster, Path.Combine(AppContext.BaseDirectory, darkPoster ? "share-dark.png" : "share-light.png")); }
            for (int style = 0; style < SharePoster.Names.Length; style++) {
                var regression = SharePoster.Render(shareFixture, "GPT-6 Astra 上线 Microsoft Foundry，早期客户已在 Azure 上使用", "Satya Nadella 发文表示，早期客户已开始使用 Azure 上的 Astra。", style);
                Assert(regression.PixelWidth == 720 && regression.PixelHeight == 960, "mixed language screenshot regression " + style);
                SharePoster.Save(regression, Path.Combine(AppContext.BaseDirectory, "regression-" + style + ".png"));
                var styled = SharePoster.Render(shareFixture, shareFixture.Title, shareFixture.Summary, style); Assert(styled.PixelWidth == 720 && styled.PixelHeight == 960, "fixed 3:4 style " + style); SharePoster.Save(styled, Path.Combine(AppContext.BaseDirectory, "style-" + style + ".png"));
                try { SharePoster.Render(shareFixture, new string('长', 220), new string('文', 600), style); throw new Exception("Expected layout overflow"); } catch (InvalidOperationException e) { Assert(e.Message.Contains("3:4"), "overflow requests editing without cropping " + style); }
                var emptyStyled = SharePoster.Render(shareFixture, "简讯", "", style); Assert(emptyStyled.PixelWidth == 720 && emptyStyled.PixelHeight == 960, "empty summary preserves ratio " + style);
            }
            var livePoster = SharePoster.Render(liveVisible[0], "原文直达 · 实际资讯链接测试", "真实原文二维码测试", false); SharePoster.Save(livePoster, Path.Combine(AppContext.BaseDirectory, "poster-live.png")); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "poster-live-url.txt"), liveVisible[0].Links.Original);
            try { SharePoster.Render(shareFixture, "Title", new string('文', 601), false); throw new Exception("expected share length rejection"); } catch (InvalidOperationException) { Assert(true, "oversized summary requires editing"); }
            Assert(liveVisible.Count == 100 && liveVisible.Select(n => n.Id).Distinct().Count() == 100, "live eligible feed has 100 unique stories");
            var widget = new MainWindow(true); widget.Show(); widget.UpdateLayout();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var itemsField = typeof(MainWindow).GetField("items", flags)!;
            var indexField = typeof(MainWindow).GetField("index", flags)!;
            var step = typeof(MainWindow).GetMethod("StepNews", flags)!;
            var tick = typeof(MainWindow).GetMethod("TickPlayback", flags)!;
            var headline = typeof(MainWindow).GetMethod("SetHeadline", flags)!;
            itemsField.SetValue(widget, liveVisible); headline.Invoke(widget, null);
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 100; i++) { seen.Add(liveVisible[(int)indexField.GetValue(widget)!].Id); step.Invoke(widget, new object[] { 1 }); }
            Assert(seen.Count == 100 && (int)indexField.GetValue(widget)! == 0, "full live playback cycle covers all 100 before repeating");
            stressClock.Restart();
            for (int i = 0; i < 10000; i++) step.Invoke(widget, new object[] { 1 });
            Assert((int)indexField.GetValue(widget)! == 0, "10000 forward switches wrap correctly");
            for (int i = 0; i < 1000; i++) step.Invoke(widget, new object[] { -1 });
            Assert((int)indexField.GetValue(widget)! == 0, "1000 reverse switches wrap correctly");
            long switchStressMs = stressClock.ElapsedMilliseconds;
            itemsField.SetValue(widget, hundred); headline.Invoke(widget, null); widget.UpdateLayout();
            for (int i = 0; i < 100; i++) { for (int frame = 0; frame < 120; frame++) tick.Invoke(widget, new object[] { .1 }); Assert((int)indexField.GetValue(widget)! == (i + 1) % 100, "accelerated automatic playback " + i); headline.Invoke(widget, null); }
            Assert(!System.Windows.Controls.ToolTipService.GetIsEnabled((System.Windows.DependencyObject)widget.FindName("TickerArea")), "news hover popup stays disabled");
            itemsField.SetValue(widget, liveVisible); indexField.SetValue(widget, 0); headline.Invoke(widget, null);
            var windowHandle = new System.Windows.Interop.WindowInteropHelper(widget).Handle;
            Native.GetWindowRect(windowHandle, out var beforeAlignment);
            var desktopArea = System.Windows.Forms.Screen.FromHandle(windowHandle).WorkingArea;
            for (int align = 0; align < 3; align++)
            {
                var alignmentMenu = widget.BuildAlignmentMenu();
                Assert(alignmentMenu.Items.Count == 3, "alignment popup has only three commands");
                ((System.Windows.Controls.MenuItem)alignmentMenu.Items[align]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); Native.GetWindowRect(windowHandle, out var aligned);
                int remaining = Math.Max(0, desktopArea.Width - (aligned.Right - aligned.Left));
                int expectedX = desktopArea.Left + (align == 0 ? 0 : align == 1 ? remaining / 2 : remaining);
                Assert(aligned.Left == expectedX && aligned.Top == beforeAlignment.Top, "desktop alignment " + align + " preserves height");
            }
            var positionLock = (System.Windows.Controls.Button)widget.FindName("LockButton");
            Native.GetWindowRect(windowHandle, out var beforeLock);
            positionLock.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            widget.AlignDesktop(0); Native.GetWindowRect(windowHandle, out var lockedBounds);
            Assert(lockedBounds.Left == beforeLock.Left, "position lock blocks alignment");
            Assert(widget.BuildMenu().Items.OfType<System.Windows.Controls.MenuItem>().Count(n => !n.IsEnabled) == 3, "locked alignment commands disabled");
            positionLock.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            widget.AlignDesktop(0); Native.GetWindowRect(windowHandle, out var unlockedBounds);
            Assert(unlockedBounds.Left == desktopArea.Left, "unlock restores alignment");
            ((System.Windows.Controls.MenuItem)widget.BuildMenu().Items[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert(widget.IsPaused, "pause menu");
            ((System.Windows.Controls.MenuItem)widget.BuildMenu().Items[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert(!widget.IsPaused, "resume menu");
            widget.OpenReader();
            var openedReader = System.Windows.Application.Current.Windows.OfType<ReaderWindow>().Single();
            Assert(!widget.IsVisible && openedReader.IsVisible, "reader hides overlay");
            openedReader.UpdateLayout();
            Assert(Ui.CanStartDrag(openedReader.InputHitTest(new System.Windows.Point(100, 110)) as System.Windows.DependencyObject, false), "reader heading hit area starts drag");
            Assert(!Ui.CanStartDrag(openedReader.InputHitTest(new System.Windows.Point(100, 160)) as System.Windows.DependencyObject, false), "search hit area preserves input");
            openedReader.WindowState = System.Windows.WindowState.Minimized;
            Assert(widget.IsVisible, "minimize restores overlay");
            openedReader.WindowState = System.Windows.WindowState.Normal;
            Assert(!widget.IsVisible, "restore reader hides overlay");
            openedReader.Close(); Assert(widget.IsVisible, "close reader restores overlay");
            var navigation = new ReaderWindow(new System.Collections.Generic.List<NewsItem> { new() { Id = "one", Title = "First" }, new() { Id = "two", Title = "Second" } }, "test");
            navigation.MoveSelection(1); Assert(navigation.SelectedId == "two", "next article");
            navigation.MoveSelection(1); Assert(navigation.SelectedId == "two", "last article boundary");
            navigation.MoveSelection(-1); Assert(navigation.SelectedId == "one", "previous article");
            var readerFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var progress = (System.Windows.Controls.TextBlock)typeof(ReaderWindow).GetField("positionLabel", readerFlags)!.GetValue(navigation)!;
            Assert(progress.Text == "1 / 2", "reader displays current position");
            var readerSearch = (System.Windows.Controls.TextBox)typeof(ReaderWindow).GetField("search", readerFlags)!.GetValue(navigation)!;
            var applyFilter = typeof(ReaderWindow).GetMethod("Filter", readerFlags)!;
            readerSearch.Text = "unmatched"; applyFilter.Invoke(navigation, null);
            Assert(navigation.SelectedId == null && progress.Text == "0 / 0", "empty search clears selection and progress");
            readerSearch.Text = "Second"; applyFilter.Invoke(navigation, null);
            Assert(navigation.SelectedId == "two" && progress.Text == "1 / 1", "search selects matching article");
            readerSearch.Clear(); applyFilter.Invoke(navigation, null);
            Assert(navigation.SelectedId == "two" && progress.Text == "2 / 2", "clearing search preserves selection");
            navigation.UpdateItems(new() { new() { Id = "one", Title = "First" }, new() { Id = "two", Title = "Second updated" } });
            Assert(navigation.SelectedId == "two", "refresh preserves current article"); navigation.Close();
            widget.RenderPreview();
            var reportWindow = new System.Windows.Window { Width = 1100, Height = 740 };
            Ui.Shell(reportWindow, "YuMir的阅读室 · 周报", new ReportPanel(2)); reportWindow.Show(); reportWindow.UpdateLayout();
            Assert(reportWindow.IsVisible && reportWindow.ActualWidth > 0, "report panel renders");
            var reportBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)reportWindow.ActualWidth, (int)reportWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); reportBitmap.Render(reportWindow);
            var reportEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); reportEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(reportBitmap));
            using (var reportFile = File.Create(Path.Combine(AppContext.BaseDirectory, "report-preview.png"))) reportEncoder.Save(reportFile);
            for (int cycle = 0; cycle < 20; cycle++) { Theme.Apply(true); Assert(Ui.Brush("#EBF1F6").Color == System.Windows.Media.Color.FromRgb(28, 48, 59), "light theme text color " + cycle); Theme.Apply(false); Assert(Ui.Brush("#EBF1F6").Color == System.Windows.Media.Color.FromRgb(235, 241, 246), "dark theme restore " + cycle); }
            Theme.Toggle(); Assert(Storage.Read<Settings>("settings.json").LightTheme, "theme choice persists");
            reportWindow.UpdateLayout();
            var lightBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)reportWindow.ActualWidth, (int)reportWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); lightBitmap.Render(reportWindow);
            var lightEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); lightEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(lightBitmap)); using (var lightFile = File.Create(Path.Combine(AppContext.BaseDirectory, "light-report-preview.png"))) lightEncoder.Save(lightFile);
            Theme.Toggle(); reportWindow.Close();
            var shareWindow = new ShareWindow(shareFixture); shareWindow.Show(); shareWindow.UpdateLayout();
            Assert(shareWindow.ActualWidth > 0 && shareWindow.IsVisible, "share editor renders");
            var editorBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)shareWindow.ActualWidth, (int)shareWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); editorBitmap.Render(shareWindow);
            var editorEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); editorEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(editorBitmap)); using (var editorFile = File.Create(Path.Combine(AppContext.BaseDirectory, "share-editor-preview.png"))) editorEncoder.Save(editorFile); shareWindow.Close();
            Assert(File.Exists(Path.Combine(AppContext.BaseDirectory, "reader-preview.png")), "WPF widget and reader rendering");
            Storage.Save("test-result.json", new { passed, liveItems = live.Cache.Items.Count, result = "PASS" });
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test-result.txt"), $"PASS: {passed} checks; live items={live.Cache.Items.Count}; playable unique={liveVisible.Count}; 30 x 100000 input rows={feedStressMs}ms; 11000 switches={switchStressMs}ms; 100 automatic transitions; profile={Storage.Root}");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test-result.txt"), $"FAIL after {passed}: {e}"); return 1; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        private int call;
        public bool SawETag, CleanQuery;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            call++;
            if (call == 2) { SawETag = request.Headers.Contains("If-None-Match"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)); }
            if (call == 3) CleanQuery = !request.Headers.Contains("If-None-Match") && request.RequestUri!.Query.Contains("category=paper");
            if (call == 4) { var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests); limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10)); return Task.FromResult(limited); }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"schemaVersion\":1,\"items\":[{\"id\":\"a\",\"title\":\"测试\",\"links\":{\"original\":\"https://example.com/story\"}},{\"id\":\"a\",\"title\":\"重复\"},{\"id\":\"\",\"title\":\"错误\"}]}") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"test\""); return Task.FromResult(response);
        }
    }
}
