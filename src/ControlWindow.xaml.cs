using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TvDesk.Sources;

namespace TvDesk.UI;

public partial class ControlWindow : Window
{
    private readonly ObservableCollection<Channel> _channels = new();
    private ICollectionView? _view;

    public ControlWindow()
    {
        InitializeComponent();
        ChannelList.ItemsSource = _channels;
        _view = CollectionViewSource.GetDefaultView(_channels);
        _view.Filter = FilterChannel;

        var s = AppController.Instance.Settings;
        VolumeSlider.Value = s.Volume;
        DimSlider.Value = s.WallpaperDim;
        UpdateSliderLabels();

        foreach (var obj in QualityCombo.Items)
            if (obj is ComboBoxItem item && (item.Tag as string) == s.Quality)
            {
                QualityCombo.SelectedItem = item;
                break;
            }
        if (QualityCombo.SelectedItem == null) QualityCombo.SelectedIndex = 0;

        Closing += (s2, e) => { e.Cancel = true; Hide(); };
        AppController.Instance.StatusChanged += OnStatusChanged;
    }

    private void OnStatusChanged(string message)
        => Dispatcher.BeginInvoke(new System.Action(() => StatusText.Text = message));

    public void PopulateChannels(IEnumerable<Channel> channels)
    {
        Dispatcher.Invoke(() =>
        {
            _channels.Clear();
            var settings = AppController.Instance.Settings;
            var favs = settings.FavoriteUrls ?? new List<string>();

            foreach (var item in settings.FavoriteItems.Where(x => !string.IsNullOrWhiteSpace(x.Url)))
            {
                _channels.Add(new Channel
                {
                    Name = string.IsNullOrWhiteSpace(item.Name) ? item.Url : item.Name,
                    Url = item.Url,
                    Group = "منتخب‌های دستی",
                    Source = "Custom Favorite",
                    IsFavorite = true
                });
            }

            foreach (var c in channels)
            {
                c.IsFavorite = favs.Contains(c.Url);
                if (_channels.Any(x => x.Url == c.Url)) continue;
                _channels.Add(c);
            }

            var groups = _channels.Select(c => c.Group ?? "")
                .Where(g => g.Length > 0).Distinct().OrderBy(g => g).ToList();
            groups.Insert(0, "همه دسته‌ها");
            GroupCombo.ItemsSource = groups;
            GroupCombo.SelectedIndex = 0;
            _view?.Refresh();
        });
    }

    public void RefreshFavoriteStates()
    {
        var favs = AppController.Instance.Settings.FavoriteUrls;
        foreach (var c in _channels) c.IsFavorite = favs.Contains(c.Url);
        _view?.Refresh();
    }

    private bool FilterChannel(object obj)
    {
        if (obj is not Channel c) return false;
        string q = SearchBox.Text?.Trim() ?? "";
        if (q.Length > 0 && !((c.Name ?? "").Contains(q, System.StringComparison.OrdinalIgnoreCase) || (c.Url ?? "").Contains(q, System.StringComparison.OrdinalIgnoreCase)))
            return false;

        string group = GroupCombo.SelectedItem as string ?? "همه دسته‌ها";
        if (group != "همه دسته‌ها" && c.Group != group) return false;
        if (FavOnly.IsChecked == true && !c.IsFavorite) return false;
        return true;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();
    private void OnGroupChanged(object sender, SelectionChangedEventArgs e) => _view?.Refresh();
    private void OnFavOnlyChanged(object sender, RoutedEventArgs e) => _view?.Refresh();

    private async void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is Channel c)
            await AppController.Instance.PlayAsync(c.Url, c.Name);
    }

    private async void OnPlayLink(object sender, RoutedEventArgs e)
    {
        string url = LinkBox.Text?.Trim() ?? "";
        string name = LinkNameBox.Text?.Trim() ?? "";
        if (url.Length > 0)
            await AppController.Instance.PlayAsync(url, string.IsNullOrWhiteSpace(name) ? "لینک سفارشی" : name);
    }

    private void OnSaveLinkFavorite(object sender, RoutedEventArgs e)
    {
        string url = LinkBox.Text?.Trim() ?? "";
        if (url.Length == 0) return;
        string name = LinkNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = url.Length > 54 ? url[..54] + "…" : url;
        AppController.Instance.AddFavoriteLink(name, url);
        PopulateChannels(AppController.Instance.Library.Channels);
        StatusText.Text = $"★ ذخیره شد: {name}";
    }

    private void OnFavoriteButtonClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Channel c)
        {
            AppController.Instance.ToggleFavorite(c);
            RefreshFavoriteStates();
        }
    }

    private void OnToggleSelectedFavorite(object sender, RoutedEventArgs e)
    {
        if (ChannelList.SelectedItem is Channel c)
        {
            AppController.Instance.ToggleFavorite(c);
            RefreshFavoriteStates();
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        AppController.Instance.SetVolume((int)e.NewValue);
        UpdateSliderLabels();
    }

    private void OnDimChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        AppController.Instance.SetDim(e.NewValue);
        UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        if (VolumeValue != null) VolumeValue.Text = $"{(int)VolumeSlider.Value}%";
        if (DimValue != null) DimValue.Text = $"{(int)(DimSlider.Value * 100)}%";
    }

    private void OnQualityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QualityCombo.SelectedItem is ComboBoxItem item && item.Tag is string q)
            AppController.Instance.SetQuality(q);
    }

    private void OnMute(object sender, RoutedEventArgs e) => AppController.Instance.ToggleMute();
    private void OnToggleIcons(object sender, RoutedEventArgs e) => AppController.Instance.ToggleDesktopIcons();

    private void OnShowDesktopHint(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
}
