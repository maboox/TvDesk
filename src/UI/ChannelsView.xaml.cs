using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TvDesk.Core;
using TvDesk.Sources;

namespace TvDesk.UI;

public partial class ChannelsView : UserControl
{
    private MainViewModel? _vm;

    public ChannelsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.ScrollToTopRequested -= ScrollToTop;
            _vm = DataContext as MainViewModel;
            if (_vm != null) _vm.ScrollToTopRequested += ScrollToTop;
        };
    }

    private void ScrollToTop()
    {
        try
        {
            if (ChannelList.Items.Count > 0) ChannelList.ScrollIntoView(ChannelList.Items[0]);
            var sv = FindScrollViewer(ChannelList);
            sv?.ScrollToTop();
        }
        catch { }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private static Channel? ChannelOf(object sender)
        => (sender as FrameworkElement)?.DataContext as Channel;

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (sender is ListBoxItem item && item.DataContext is Channel c) _vm?.Play(c);
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var c = ChannelOf(sender);
        if (c != null) _vm?.Play(c);
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var c = ChannelOf(sender);
        if (c != null) _vm?.ToggleFavorite(c);
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (ChannelList.SelectedItem is not Channel c) return;
        if (e.Key == Key.Enter) { _vm?.Play(c); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None) { _vm?.ToggleFavorite(c); e.Handled = true; }
    }

    private void OnMenuPlay(object sender, RoutedEventArgs e)
    {
        var c = ChannelOf(sender);
        if (c != null) _vm?.Play(c);
    }

    private void OnMenuFavorite(object sender, RoutedEventArgs e)
    {
        var c = ChannelOf(sender);
        if (c != null) _vm?.ToggleFavorite(c);
    }

    private async void OnMenuTest(object sender, RoutedEventArgs e)
    {
        var c = ChannelOf(sender);
        if (c != null && _vm != null) await _vm.TestOneAsync(c);
    }

    private void OnMenuCopy(object sender, RoutedEventArgs e)
    {
        var c = ChannelOf(sender);
        if (c == null) return;
        try
        {
            Clipboard.SetText(c.Url);
            _vm?.Toast(Loc.T("toast_copied"));
        }
        catch (Exception ex) { Logger.Log("Clipboard", ex); }
    }

    private void OnMenuRemove(object sender, RoutedEventArgs e)
    {
        var c = ChannelOf(sender);
        if (c != null) _vm?.RemoveLink(c);
    }
}
