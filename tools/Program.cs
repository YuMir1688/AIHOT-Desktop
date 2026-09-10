using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using AiHot;
using Mono.Cecil;
using Mono.Cecil.Cil;
using YuMir.Cards;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args[0] == "telegram-test")
        {
            var state = new TelegramPush.State { Seen = new HashSet<string> { "old" } };
            var input = new[] { new NewsItem { Id = "old" }, new NewsItem { Id = "new" }, new NewsItem { Id = "new" } };
            Require(TelegramPush.Discover(state, input) == 1 && state.Pending.Count == 1, "Baseline and duplicate IDs excluded");
            var restored = JsonSerializer.Deserialize<TelegramPush.State>(JsonSerializer.Serialize(state))!;
            Require(TelegramPush.Discover(restored, input) == 0 && restored.Pending.Count == 1, "Restart preserves pending and deduplication");
            Console.WriteLine("PASS Telegram baseline, deduplication, persistent pending queue"); return;
        }
        if (args[0] == "reader-test")
        {
            typeof(ReportTests).GetMethod("Initialize", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
            var type = typeof(NewsItem).Assembly.GetType("AiHot.ReaderWindow")!;
            var footerItem = new NewsItem { Id = "footer-test", Title = "固定署名验证", Summary = "正文", Source = new NewsSource { Name = "测试" }, Links = new NewsLinks { Original = "https://example.com" }, DiscoveredAt = DateTimeOffset.Now };
            var window = (System.Windows.Window)Activator.CreateInstance(type, new object[] { new List<NewsItem> { footerItem }, "测试" })!;
            type.GetMethod("Select")!.Invoke(window, new object[] { footerItem.Id });
            type.GetMethod("Select")!.Invoke(window, new object[] { footerItem.Id });
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var detail = (System.Windows.Controls.StackPanel)type.GetField("detail", flags)!.GetValue(window)!;
            var scroll = (System.Windows.Controls.ScrollViewer)type.GetField("articleScroll", flags)!.GetValue(window)!;
            var host = (System.Windows.Controls.DockPanel)scroll.Parent;
            var footer = host.Children.OfType<System.Windows.Controls.StackPanel>().Single(p => Equals(p.Tag, "YuMir.FixedAttribution"));
            Require(footer.Children.Count == 2 && System.Windows.Controls.DockPanel.GetDock(footer) == System.Windows.Controls.Dock.Bottom, "Fixed footer exists once");
            Require(!detail.Children.OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text.Contains("资讯整理")), "Attribution removed from scrolling content");
            typeof(ReportTests).GetMethod("Capture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { window, args[1] });
            window.Close(); Console.WriteLine("PASS fixed footer, repeated selection, scroll separation and sidebar layout"); return;
        }
        if (args[0] == "patch") { Patch(args[1], args[2], args[3]); return; }
        if (args[0] == "verify") { Verify(args[1]); return; }
        if (args[0] == "report-test") { ReportTests.Run(args[1], args[2]); return; }
        if (args[0] == "report-ui") { ReportTests.Show(args[1]); return; }
        if (args[0] == "test-host")
        {
            using var testModule = ModuleDefinition.ReadModule(args[1]);
            var startup = testModule.Types.Single(t => t.FullName == "AiHot.App").Methods.Single(m => m.Name == "OnStartup");
            startup.Body = new MethodBody(startup); startup.Body.GetILProcessor().Emit(OpCodes.Ret);
            testModule.Write(args[2]); return;
        }
        Directory.CreateDirectory(args[1]);
        using var cache = JsonDocument.Parse(File.ReadAllText(args[2]));
        var items = JsonSerializer.Deserialize<List<NewsItem>>(cache.RootElement.GetProperty("Items").GetRawText())!;
        var item = items.FirstOrDefault(n => n.Title.Contains("GPT Image 2.5")) ?? items[0];
        File.WriteAllText(Path.Combine(args[1], "expected-url.txt"), item.Links.Original);
        var tests = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            Save(EditorialCard.Render(item, item.Title, item.Summary ?? "", i), Path.Combine(args[1], $"card-{i}.png"));
            var shortCard = EditorialCard.Render(item, "AI 新知，每天一点。", "", i);
            Require(shortCard.PixelWidth == 1080 && shortCard.PixelHeight == 1440, "3:4 dimensions");
            Save(shortCard, Path.Combine(args[1], $"short-{i}.png"));
            try { EditorialCard.Render(item, new string('长', 220), new string('文', 600), i); throw new Exception("Overflow was not rejected"); }
            catch (InvalidOperationException ex) { Require(ex.Message.Contains("精简"), "Overflow explains next step"); }
            tests.Add($"PASS style {i}: real article, empty summary, dimensions, overflow rejection");
        }
        int rendered = 0, needsEditing = 0, otherErrors = 0;
        foreach (var news in items)
        {
            string lead = (string)typeof(NewsItem).Assembly.GetType("AiHot.SharePoster")!.GetMethod("SuggestedLead", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [news.Summary])!;
            try { EditorialCard.Render(news, news.Title, lead, 0); rendered++; }
            catch (InvalidOperationException ex) { if (ex.Message.Contains("精简") || ex.Message.Contains("二维码过密")) needsEditing++; else { otherErrors++; tests.Add(ex.Message); } }
        }
        Require(otherErrors == 0, "No unexpected render errors");
        tests.Add($"Real cache: {rendered} renderable / {needsEditing} request manual editing / {otherErrors} unexpected errors");
        foreach (string invalid in new[] {"file:///C:/x", "javascript:alert(1)", "https://user:pass@example.com"})
        {
            var bad = new NewsItem { Links = new NewsLinks { Original = invalid } };
            try { EditorialCard.Render(bad, "Title", "", 0); throw new Exception("Invalid URL accepted"); }
            catch (InvalidOperationException) { }
        }
        File.WriteAllLines(Path.Combine(args[1], "verification.txt"), tests);
        Console.WriteLine(string.Join(Environment.NewLine, tests));
    }
    static void Save(BitmapSource bitmap, string path)
    { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file); }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var nested in All(type.NestedTypes)) yield return nested; } }
    static void Patch(string original, string cards, string output)
    {
        using var module = ModuleDefinition.ReadModule(original);
        using var newModule = ModuleDefinition.ReadModule(cards);
        var destination = newModule.Types.Single(t => t.FullName == "YuMir.Cards.EditorialCard").Methods.Single(m => m.Name == "Render");
        var poster = module.Types.Single(t => t.FullName == "AiHot.SharePoster");
        var render = poster.Methods.Single(m => m.Name == "Render" && m.Parameters[3].ParameterType.FullName == "System.Int32");
        render.Body = new MethodBody(render);
        var il = render.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, module.ImportReference(destination)); il.Emit(OpCodes.Ret);
        string[] oldNames = ["薄荷白", "暖纸米白", "冰川蓝", "柔和米灰", "薰衣草灰"];
        foreach (var type in All(module.Types).Where(t => t.FullName.StartsWith("AiHot.SharePoster") || t.FullName.StartsWith("AiHot.ShareWindow") || t.FullName.StartsWith("AiHot.SelfTest")))
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string text)
            {
                int index = Array.IndexOf(oldNames, text);
                if (index >= 0) instruction.Operand = EditorialCard.Names[index];
                else if (text.Contains("720 × 960")) instruction.Operand = text.Replace("720 × 960", "1080 × 1440");
            }
            if (type.FullName.StartsWith("AiHot.ShareWindow") && instruction.OpCode == OpCodes.Ldc_R8 && (double)instruction.Operand == 350d)
                instruction.Operand = 510d;
            // The existing self-test checks the old export resolution in its async body.
            if (type.FullName.StartsWith("AiHot.SelfTest") && instruction.OpCode == OpCodes.Ldc_I4)
            {
                if ((int)instruction.Operand == 720) instruction.Operand = 1080;
                if ((int)instruction.Operand == 960) instruction.Operand = 1440;
            }
        }
        var panel = module.Types.Single(t => t.FullName == "AiHot.ReportPanel");
        var reportRender = panel.Methods.Single(m => m.Name == "Render");
        var hook = newModule.Types.Single(t => t.FullName == "YuMir.Cards.ReportSharing").Methods.Single(m => m.Name == "Update");
        if (!reportRender.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName.Contains("YuMir.Cards.ReportSharing::Update")))
        {
            var dataLocal = reportRender.Body.Variables.Single(v => v.VariableType.FullName == "AiHot.ReportData");
            var archiveLocal = reportRender.Body.Variables.Single(v => v.VariableType.FullName == "AiHot.NewsArchive");
            var dataType = module.Types.Single(t => t.FullName == "AiHot.ReportData");
            var archiveType = module.Types.Single(t => t.FullName == "AiHot.NewsArchive");
            var reportIl = reportRender.Body.GetILProcessor();
            var end = reportRender.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
            var added = new[] {
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, panel.Fields.Single(f => f.Name == "period")),
                Instruction.Create(OpCodes.Ldloc, dataLocal), Instruction.Create(OpCodes.Ldfld, dataType.Fields.Single(f => f.Name == "Start")),
                Instruction.Create(OpCodes.Ldloc, dataLocal), Instruction.Create(OpCodes.Ldfld, dataType.Fields.Single(f => f.Name == "End")),
                Instruction.Create(OpCodes.Ldloc, dataLocal), Instruction.Create(OpCodes.Ldfld, dataType.Fields.Single(f => f.Name == "Items")),
                Instruction.Create(OpCodes.Ldloc, archiveLocal), Instruction.Create(OpCodes.Callvirt, archiveType.Methods.Single(m => m.Name == "get_StartedAt")),
                Instruction.Create(OpCodes.Call, module.ImportReference(hook)) };
            foreach (var instruction in added) reportIl.InsertBefore(end, instruction);
            // Expand short branches so inserted IL cannot overflow their one-byte offsets.
            foreach (var instruction in reportRender.Body.Instructions)
            {
                if (instruction.Operand == end) instruction.Operand = added[0];
                instruction.OpCode = instruction.OpCode.Code switch {
                    Code.Br_S => OpCodes.Br, Code.Brfalse_S => OpCodes.Brfalse, Code.Brtrue_S => OpCodes.Brtrue,
                    Code.Beq_S => OpCodes.Beq, Code.Bge_S => OpCodes.Bge, Code.Bgt_S => OpCodes.Bgt,
                    Code.Ble_S => OpCodes.Ble, Code.Blt_S => OpCodes.Blt, Code.Bne_Un_S => OpCodes.Bne_Un,
                    Code.Bge_Un_S => OpCodes.Bge_Un, Code.Bgt_Un_S => OpCodes.Bgt_Un, Code.Ble_Un_S => OpCodes.Ble_Un,
                    Code.Blt_Un_S => OpCodes.Blt_Un, Code.Leave_S => OpCodes.Leave, _ => instruction.OpCode };
            }
        }
        var reader = module.Types.Single(t => t.FullName == "AiHot.ReaderWindow");
        var select = reader.Methods.Single(m => m.Name == "Select");
        var fix = newModule.Types.Single(t => t.FullName == "YuMir.Cards.ReaderAttribution").Methods.Single(m => m.Name == "Fix");
        if (!select.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName.Contains("ReaderAttribution::Fix")))
        {
            var processor = select.Body.GetILProcessor();
            var end = select.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
            var first = Instruction.Create(OpCodes.Ldarg_0);
            foreach (var i in select.Body.Instructions) if (i.Operand == end) i.Operand = first;
            foreach (var i in new[] { first, Instruction.Create(OpCodes.Ldfld, reader.Fields.Single(f => f.Name == "detail")), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, reader.Fields.Single(f => f.Name == "articleScroll")), Instruction.Create(OpCodes.Call, module.ImportReference(fix)) }) processor.InsertBefore(end, i);
        }
        var main = module.Types.Single(t => t.FullName == "AiHot.MainWindow");
        var tick = main.Methods.Single(m => m.Name == "TickPlayback");
        tick.Body = new MethodBody(tick);
        var tickIl = tick.Body.GetILProcessor();
        tickIl.Emit(OpCodes.Ldarg_0); tickIl.Emit(OpCodes.Ldarg_1);
        tickIl.Emit(OpCodes.Call, module.ImportReference(newModule.Types.Single(t => t.FullName == "YuMir.Cards.TickerMotion").Methods.Single(m => m.Name == "Tick"))); tickIl.Emit(OpCodes.Ret);
        var headlineMethod = main.Methods.Single(m => m.Name == "SetHeadline");
        if (!headlineMethod.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName.Contains("TelegramPush::Start")))
            headlineMethod.Body.GetILProcessor().InsertBefore(headlineMethod.Body.Instructions[0], Instruction.Create(OpCodes.Call, module.ImportReference(newModule.Types.Single(t => t.FullName == "YuMir.Cards.TelegramPush").Methods.Single(m => m.Name == "Start"))));
        var filterMethod = reader.Methods.Single(m => m.Name == "Filter");
        if (!filterMethod.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName.Contains("ReaderSidebar::Apply")))
        {
            var filterIl = filterMethod.Body.GetILProcessor();
            var endFilter = filterMethod.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
            var loadReader = Instruction.Create(OpCodes.Ldarg_0);
            foreach(var i in filterMethod.Body.Instructions) if(i.Operand == endFilter) i.Operand = loadReader;
            filterIl.InsertBefore(endFilter, loadReader);
            filterIl.InsertBefore(endFilter, Instruction.Create(OpCodes.Call, module.ImportReference(newModule.Types.Single(t => t.FullName == "YuMir.Cards.ReaderSidebar").Methods.Single(m => m.Name == "Apply"))));
        }
        var shareWindow = module.Types.Single(t => t.FullName == "AiHot.ShareWindow");
        foreach (var method in shareWindow.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
            if (instruction.OpCode == OpCodes.Ldstr && Equals(instruction.Operand, "编辑内容，生成一张值得分享的资讯卡。"))
            {
                var size = instruction.Next;
                if (size.OpCode != OpCodes.Ldc_R8) throw new InvalidOperationException("Unexpected share hint font instruction");
                size.Operand = 12d;
            }
        module.Write(output);
        Console.WriteLine("Patched share rendering and report sharing hook; existing report calculation preserved.");
    }
    static void Verify(string path)
    {
        var assembly = System.Reflection.Assembly.LoadFrom(path);
        var render = assembly.GetType("AiHot.SharePoster")!.GetMethod("Render", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, [typeof(NewsItem), typeof(string), typeof(string), typeof(int)])!;
        var news = new NewsItem { Title = "连接验证", Source = new NewsSource { Name = "YuMir" }, Links = new NewsLinks { Original = "https://aihot.news" }, DiscoveredAt = DateTimeOffset.Now };
        var image = (BitmapSource)render.Invoke(null, [news, news.Title, "新版分享卡片已接入。", 0])!;
        Require(image.PixelWidth == 1080 && image.PixelHeight == 1440, "Patched renderer dimensions");
        Console.WriteLine("PASS patched SharePoster dispatch: 1080 × 1440");
    }
}
