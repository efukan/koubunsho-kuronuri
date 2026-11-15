using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Kuronuri.Core.Pdfium;
using Kuronuri.Core.Settings;

namespace Kuronuri.App
{
    public partial class App : Application
    {
        private PdfiumWorker? _worker;
        private bool _failing;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Unhandled exceptions: show the exception type only (never a message, path or document content), then exit.
            DispatcherUnhandledException += (s, a) => { a.Handled = true; Fail(a.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Fail(a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => a.SetObserved();   // stale render requests etc.

            try { _worker = new PdfiumWorker(); }
            catch (InvalidOperationException)
            {
                MessageBox.Show("pdfium.dll を読み込めませんでした。\nKuronuri.exe と同じフォルダに pdfium.dll があるか確認してください。",
                    "公文書くろぬり", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            string settingsPath = SettingsStore.DefaultPath();
            var settings = SettingsStore.Load(settingsPath);
            Themes.ThemeManager.Apply(settings.Theme);
            var win = new MainWindow(_worker, settings, settingsPath);
            MainWindow = win;
            win.Show();
            if (e.Args.Length > 0 && File.Exists(e.Args[0])) win.Dispatcher.BeginInvoke(new Action(() => win.OpenPath(e.Args[0])), DispatcherPriority.ApplicationIdle);
        }

        private void Fail(Exception? ex)
        {
            if (_failing) return;
            _failing = true;
            string kind = ex?.GetType().Name ?? "Unknown";
            try
            {
                MessageBox.Show("予期しないエラーが発生したため、終了します。\n種別: " + kind + "\n\n保存していない変更は失われます。",
                    "公文書くろぬり", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* nothing more can be done */ }
            Environment.Exit(2);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _worker?.Dispose();
            base.OnExit(e);
        }
    }
}
