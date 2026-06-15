using System;
using System.Windows;
using System.Windows.Interop;
using TvDesk.Interop;
using TvDesk.Playback;

namespace TvDesk.UI;

public partial class DesktopWindow : Window
{
    public DesktopWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // پوشش کل صفحه‌نمای‌ها
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public void AttachToDesktop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        WorkerWHelper.AttachToDesktop(handle);
    }

    public void BindPlayback(PlaybackEngine engine)
    {
        VideoView.MediaPlayer = engine.Player;
    }

    public void SetDim(double dim)
    {
        DimOverlay.Opacity = Math.Clamp(dim, 0, 0.85);
    }
}
