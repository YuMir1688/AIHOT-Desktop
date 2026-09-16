using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AiHot;
public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? wake;
    private RegisteredWaitHandle? listener;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            int code = await SelfTest.Run();
            Shutdown(code); return;
        }
        if (e.Args.Contains("--render-preview"))
        {
            Storage.Root = Path.Combine(AppContext.BaseDirectory, "preview-profile");
            var preview = new MainWindow(true);
            MainWindow = preview;
            preview.Show();
            preview.RenderPreview();
            Shutdown(); return;
        }
        mutex = new Mutex(true, "Local\\YuMir.AIHOT.Desktop", out bool created);
        wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\YuMir.AIHOT.Desktop.Show");
        if (!created) { wake.Set(); Shutdown(); return; }
        var main = new MainWindow(); MainWindow = main;
        listener = ThreadPool.RegisterWaitForSingleObject(wake, (_, _) => Dispatcher.BeginInvoke(new Action(main.ShowWidget)), null, Timeout.Infinite, false);
        DispatcherUnhandledException += (_, args) =>
        {
            try { Directory.CreateDirectory(Storage.Root); File.AppendAllText(Path.Combine(Storage.Root, "errors.log"), DateTime.Now + " " + args.Exception + Environment.NewLine); } catch { }
            MessageBox.Show("挂件遇到异常，请重新启动。错误记录保存在本地应用数据目录。", "AIHOT");
            args.Handled = true; Shutdown(1);
        };
        main.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        listener?.Unregister(null); wake?.Dispose(); mutex?.Dispose(); base.OnExit(e);
    }
}
