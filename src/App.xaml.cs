using System;
using System.Windows;
using System.Windows.Threading;

namespace TvDesk;

public partial class App : System.Windows.Application
{
    private AppController? _controller;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        Logger.Init();
        Logger.Log("=== TvDesk Startup ===");

        // هندلرهای سراسری خطا تا هیچ کرشی بی‌صدا نباشد
        DispatcherUnhandledException += (s, ex) =>
        {
            Logger.Log("DispatcherUnhandledException", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            Logger.Log("UnhandledException",
                ex.ExceptionObject as Exception ?? new Exception(ex.ExceptionObject?.ToString()));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            Logger.Log("UnobservedTaskException", ex.Exception);
            ex.SetObserved();
        };

        try
        {
            _controller = new AppController();
            _controller.Start();
            Logger.Log("AppController.Start() invoked");
        }
        catch (Exception ex)
        {
            Logger.Log("OnStartup", ex);
            System.Windows.MessageBox.Show(
                "خطا در راه‌اندازی TvDesk. فایل TvDesk.log را ببینید." + Environment.NewLine + Environment.NewLine + ex.Message,
                "TvDesk", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        Logger.Log("=== TvDesk Exit ===");
        _controller?.Dispose();
    }
}
