using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TvDesk.Core;
using TvDesk.UI;

namespace TvDesk;

public partial class App : Application
{
    private const string MutexName = @"Local\TvDesk.SingleInstance.v2";
    private const string ShowEventName = @"Local\TvDesk.ShowWindow.v2";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppController? _controller;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        Logger.Init();
        string[] args = e.Args ?? Array.Empty<string>();
        bool autostart = args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        bool restarting = args.Any(a => a.Equals("--restart", StringComparison.OrdinalIgnoreCase));

        // One instance only: two players on one desktop fight over the wallpaper window.
        _mutex = new Mutex(false, MutexName);
        try { _ownsMutex = _mutex.WaitOne(restarting ? TimeSpan.FromSeconds(15) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { _ownsMutex = true; }

        if (!_ownsMutex)
        {
            Logger.Log("Another instance is running — asking it to show its window");
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(() => _controller?.ShowMainWindow())),
            null, Timeout.Infinite, false);

        // Logging off / shutting down Windows must not be blocked by "close to tray".
        SessionEnding += (_, _) => TvDesk.UI.MainWindow.AllowClose = true;

        DispatcherUnhandledException += (_, ex) =>
        {
            Logger.Log("Unhandled UI exception", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Logger.Log("Unhandled exception", ex.ExceptionObject as Exception ?? new Exception(ex.ExceptionObject?.ToString()));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Logger.Log("Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        // Bundled Persian-friendly font (falls back to Segoe UI).
        try
        {
            Resources["UiFont"] = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/#Vazirmatn, Segoe UI");
        }
        catch (Exception ex) { Logger.Log("Font load", ex); }

        try
        {
            _controller = new AppController();
            _controller.Start(autostart);
        }
        catch (Exception ex)
        {
            Logger.Log("Startup", ex);
            MessageBox.Show("TvDesk could not start.\n\n" + ex.Message + "\n\nLog: " + Logger.FilePath,
                "TvDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        Logger.Log("Exit");
        try { _showWait?.Unregister(null); } catch { }
        _showEvent?.Dispose();
        _controller?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
    }

    /// <summary>Restarts the app (used after changes that need a fresh process).</summary>
    public static void Restart()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe != null) Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
        }
        catch (Exception ex) { Logger.Log("Restart", ex); }
        TvDesk.UI.MainWindow.AllowClose = true;
        Current.Shutdown();
    }
}
