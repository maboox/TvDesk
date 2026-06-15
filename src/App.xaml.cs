using System.Windows;

namespace TvDesk;

public partial class App : Application
{
    private AppController? _controller;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _controller = new AppController();
        _controller.Start();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _controller?.Dispose();
    }
}
