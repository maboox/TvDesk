using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TvDesk.Core;

namespace TvDesk.UI;

public partial class SourcesView : UserControl
{
    public SourcesView()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SourceItem item) Vm?.RemovePlaylist(item);
    }

    private void OnHomepage(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SourceItem item || string.IsNullOrWhiteSpace(item.Homepage)) return;
        try { Process.Start(new ProcessStartInfo(item.Homepage) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Log("Open homepage", ex); }
    }

    private void OnAddPlaylist(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dialog = new AddDialog(Vm, AddDialog.Mode.Playlist) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
