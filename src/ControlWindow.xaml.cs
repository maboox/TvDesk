using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TvDesk.Settings;
using TvDesk.Sources;

namespace TvDesk.UI;

public partial class ControlWindow : Window
{
    private readonly ObservableCollection<Channel> _channels = new();
    private ICollectionView? _view;
    private bool _loadingUi;

    public ControlWindow()
    {
        InitializeComponent();
        ChannelList.ItemsSource = _channels;
        _view = CollectionViewSource.GetDefaultView(_channels);
        _view.Filter = FilterChannel;

        LoadSettingsToUi();
        Closing += (s2, e) => { e.Cancel = true; Hide(); };
        AppController.Instance.StatusChanged += OnStatusChanged;
    }

    private void LoadSettingsToUi()
    {
        _loadingUi = true;
        var s = AppController.Instance.Settings;

        OnboardingPanel.Visibility = s.OnboardingComplete ? Visibility.Collapsed : Visibility.Visible;
        LanguageCombo.SelectedIndex = s.Language == "en" ? 1 : 0;
        SourceTelewebionBox.IsChecked = s.SourceTelewebion;
        SourceIranBox.IsChecked = s.SourceIptvOrgIran;
        SourceCategoriesBox.IsChecked = s.SourceIptvOrgCategories;
        SourceLanguagesBox.IsChecked = s.SourceIptvOrgLanguages;
        SourceFreeTvBox.IsChecked = s.SourceFreeTv;

        MuteFocusBox.IsChecked = s.MuteAudioOnFocusLoss;
        PauseFocusBox.IsChecked = s.PauseVideoOnFocusLoss;
        MuteFullscreenBox.IsChecked = s.MuteAudioOnFullscreen;
        PauseFullscreenBox.IsChecked = s.PauseVideoOnFullscreen;
        AutoStartBox.IsChecked = s.AutoStart;

        foreach (var obj in QualityCombo.Items)
            if (obj is ComboBoxItem item && (item.Tag as string) == s.Quality)
            {
                QualityCombo.SelectedItem = item;
                break;
            }
        if (QualityCombo.SelectedItem == null) QualityCombo.SelectedIndex = 0;
        _loadingUi = false;
    }

    private void OnStatusChanged(string message)
        => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message));

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
                    Group = "Favorites / منتخب‌ها",
                    Country = "",
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

            RebuildFilters();
            _view?.Refresh();
        });
    }

    private void RebuildFilters()
    {
        string curCountry = CountryCombo.SelectedItem as string ?? "همه کشورها";
        string curGenre = GenreCombo.SelectedItem as string ?? "همه سبک‌ها";
        string curSource = SourceCombo.SelectedItem as string ?? "همه سورس‌ها";

        var countries = new List<string> { "همه کشورها" };
        countries.AddRange(_channels.Select(c => NormalizeFilterValue(c.Country)).Where(x => x.Length > 0).Distinct().OrderBy(x => x));
        CountryCombo.ItemsSource = countries;
        CountryCombo.SelectedItem = countries.Contains(curCountry) ? curCountry : countries[0];

        var countrySet = countries.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var genres = new List<string> { "همه سبک‌ها" };
        genres.AddRange(_channels.Select(c => NormalizeFilterValue(c.Group))
            .Where(x => x.Length > 0)
            .Where(x => !countrySet.Contains(x) && !TvDesk.Sources.M3uParser.LooksLikeCountry(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x));
        GenreCombo.ItemsSource = genres;
        GenreCombo.SelectedItem = genres.Contains(curGenre) ? curGenre : genres[0];

        var sources = new List<string> { "همه سورس‌ها" };
        sources.AddRange(_channels.Select(c => NormalizeFilterValue(c.Source)).Where(x => x.Length > 0).Distinct().OrderBy(x => x));
        SourceCombo.ItemsSource = sources;
        SourceCombo.SelectedItem = sources.Contains(curSource) ? curSource : sources[0];

        if (HealthCombo.SelectedIndex < 0) HealthCombo.SelectedIndex = 0;
    }

    private static string NormalizeFilterValue(string? value) => (value ?? "").Trim();

    private static string HealthTagOf(System.Windows.Controls.ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

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
        if (q.Length > 0 && !((c.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            || (c.Url ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            || (c.Group ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            || (c.Source ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            || (c.Country ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)))
            return false;

        string country = CountryCombo.SelectedItem as string ?? "همه کشورها";
        if (country != "همه کشورها" && NormalizeFilterValue(c.Country) != country) return false;

        string genre = GenreCombo.SelectedItem as string ?? "همه سبک‌ها";
        if (genre != "همه سبک‌ها" && NormalizeFilterValue(c.Group) != genre) return false;

        string source = SourceCombo.SelectedItem as string ?? "همه سورس‌ها";
        if (source != "همه سورس‌ها" && NormalizeFilterValue(c.Source) != source) return false;

        switch (HealthTagOf(HealthCombo))
        {
            case "alive": if (c.IsAlive != true) return false; break;
            case "dead": if (c.IsAlive != false) return false; break;
            case "unknown": if (c.IsAlive != null) return false; break;
        }

        if (FavOnly.IsChecked == true && !c.IsFavorite) return false;
        return true;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();
    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => _view?.Refresh();
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
            await AppController.Instance.PlayAsync(url, string.IsNullOrWhiteSpace(name) ? "Custom link / لینک سفارشی" : name);
    }

    private void OnSaveLinkFavorite(object sender, RoutedEventArgs e)
    {
        string url = LinkBox.Text?.Trim() ?? "";
        if (url.Length == 0) return;
        string name = LinkNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = url.Length > 54 ? url[..54] + "…" : url;
        AppController.Instance.AddFavoriteLink(name, url);
        PopulateChannels(AppController.Instance.Library.Channels);
        StatusText.Text = $"★ Saved / ذخیره شد: {name}";
    }

    private async void OnAddPlaylistSource(object sender, RoutedEventArgs e)
    {
        string url = PlaylistUrlBox.Text?.Trim() ?? "";
        if (url.Length == 0) return;
        string name = PlaylistNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Custom IPTV";
        AppController.Instance.AddPlaylistSource(name, url);
        StatusText.Text = "در حال بارگذاری سورس IPTV…";
        await AppController.Instance.ReloadLibraryAsync();
        PopulateChannels(AppController.Instance.Library.Channels);
    }

    private void OnFavoriteButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is Channel c)
        {
            AppController.Instance.ToggleFavorite(c);
            RefreshFavoriteStates();
        }
    }

    private async void OnQuickTestVisible(object sender, RoutedEventArgs e)
    {
        var visible = _channels.Where(c => FilterChannel(c)).Take(30).ToList();
        if (visible.Count == 0) return;
        StatusText.Text = $"⚡ Testing {visible.Count} channel(s)…";
        int ok = 0, bad = 0;
        foreach (var c in visible)
        {
            StatusText.Text = $"⚡ Testing: {c.Name}";
            bool alive = await ChannelHealth.IsAliveAsync(c.Url);
            c.IsAlive = alive;
            if (alive) ok++; else bad++;
            _view?.Refresh();
            await Task.Delay(50);
        }
        StatusText.Text = $"تست تمام شد: {ok} سالم، {bad} ناموفق";
    }

    private void OnBehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        var s = AppController.Instance.Settings;
        s.MuteAudioOnFocusLoss = MuteFocusBox.IsChecked == true;
        s.PauseVideoOnFocusLoss = PauseFocusBox.IsChecked == true;
        s.MuteAudioOnFullscreen = MuteFullscreenBox.IsChecked == true;
        s.PauseVideoOnFullscreen = PauseFullscreenBox.IsChecked == true;
        s.AutoStart = AutoStartBox.IsChecked == true;
        SettingsStore.Save(s);
        AutoStartManager.Apply(s.AutoStart);
    }

    private async void OnCompleteOnboarding(object sender, RoutedEventArgs e)
    {
        var s = AppController.Instance.Settings;
        s.Language = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "fa";
        s.SourceTelewebion = SourceTelewebionBox.IsChecked == true;
        s.SourceIptvOrgIran = SourceIranBox.IsChecked == true;
        s.SourceIptvOrgCategories = SourceCategoriesBox.IsChecked == true;
        s.SourceIptvOrgLanguages = SourceLanguagesBox.IsChecked == true;
        s.SourceFreeTv = SourceFreeTvBox.IsChecked == true;
        s.OnboardingComplete = true;
        SettingsStore.Save(s);
        OnboardingPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "در حال بارگذاری سورس‌ها…";
        await AppController.Instance.ReloadLibraryAsync();
        PopulateChannels(AppController.Instance.Library.Channels);
    }

    private void OnQualityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        if (QualityCombo.SelectedItem is ComboBoxItem item && item.Tag is string q)
            AppController.Instance.SetQuality(q);
    }

    private void OnStopPlayback(object sender, RoutedEventArgs e)
    {
        AppController.Instance.StopPlayback();
        StatusText.Text = "⏹ پخش کامل متوقف شد؛ مصرف اینترنت قطع شد";
    }

    private async void OnRestartPlayback(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "↻ شروع دوباره…";
        await AppController.Instance.RestartPlaybackAsync();
    }

    private void OnToggleIcons(object sender, RoutedEventArgs e) => AppController.Instance.ToggleDesktopIcons();
    private void OnTitleMouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnClose(object sender, RoutedEventArgs e) => Hide();
}
