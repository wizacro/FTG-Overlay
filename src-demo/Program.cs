// FTG-Overlay demo —— 入口
// 运行：直接双击 FTG-Overlay-demo.exe（管理器 + 悬浮小抄窗同时启动）
// 参数：--selftest[=报告路径]  --shot-overlay[=PNG路径]  --shot-manager[=PNG路径]

using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace FTGOverlayDemo
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var a in args)
            {
                if (a.StartsWith("--selftest", StringComparison.Ordinal))
                {
                    var report = Engine.SelfTest();
                    var path = OptValue(a, Path.Combine(baseDir, "docs", "design", "selftest-report.txt"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, report, new UTF8Encoding(false));
                    return report.Contains("FAIL") ? 1 : 0;
                }
                if (a.StartsWith("--shot-overlay", StringComparison.Ordinal))
                {
                    var store = new Store();
                    store.Load(baseDir);
                    var app = new Application();
                    var win = new OverlayWindow(store, OptValue(a, Path.Combine(baseDir, "docs", "design", "demo-overlay-screenshot.png")));
                    win.Show();
                    return app.Run(win);
                }
                if (a.StartsWith("--shot-lock", StringComparison.Ordinal))
                {
                    var store = new Store();
                    store.Load(baseDir);
                    var app = new Application();
                    var win = new OverlayWindow(store, null);
                    var lockPath = OptValue(a, Path.Combine(baseDir, "docs", "design", "demo-lock-icon-screenshot.png"));
                    win.Show();
                    win.ContentRendered += (s, e) =>
                        win.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => win.ShootLock(lockPath)));
                    return app.Run();
                }
                if (a.StartsWith("--shot-manager", StringComparison.Ordinal))
                {
                    var store = new Store();
                    store.Load(baseDir);
                    var app = new Application();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var overlay = new OverlayWindow(store, null);
                    var manager = new ManagerWindow(store, overlay, OptValue(a, Path.Combine(baseDir, "docs", "design", "demo-manager-screenshot.png")));
                    manager.Show();
                    return app.Run();
                }
            }

            var storeMain = new Store();
            storeMain.Load(baseDir);
            var appMain = new Application();
            appMain.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var overlayMain = new OverlayWindow(storeMain, null);
            var managerMain = new ManagerWindow(storeMain, overlayMain, null);
            if (storeMain.Settings.OverlayVisible) overlayMain.Show();
            managerMain.Show();
            return appMain.Run();
        }

        static string OptValue(string arg, string def)
        {
            int i = arg.IndexOf('=');
            return i < 0 ? def : arg.Substring(i + 1).Trim('"');
        }
    }
}
