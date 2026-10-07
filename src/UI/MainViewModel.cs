using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TvDesk.Core;
using TvDesk.Sources;

namespace TvDesk.UI;

public enum Page { Browse, Favorites, Recent, Mine, Sources, Settings }

public sealed class CategoryItem : Observable
{
    public string Key { get; }
    public string Glyph { get; }

    private string _label;
    public string Label { get => _label; set => Set(ref _label, value); }

    private int _count;
    public int Count
    {
        get => _count;
        set { if (Set(ref _count, value)) Raise(nameof(IsEmpty)); }
    }

    public bool IsEmpty => _count == 0;

    public CategoryItem(string key)
    {
        Key = key;
        Glyph = Categories.Glyph(key);
        _label = Categories.Label(key);
    }
}

public sealed class FilterOption
{
    public const string AllKey = "*";
    public string Key { get; }
    public string Label { get; }
    public int Count { get; }
    public string Display => Count > 0 ? $"{Label}   {Count:N0}" : Label;

    public FilterOption(string key, string label, int count)
    {
        Key = key;
        Label = label;
        Count = count;
    }

    public override string ToString() => Display;
}

public sealed class Option
{
    public string Key { get; }
    public string Label { get; }
    public Option(string key, string label) { Key = key; Label = label; }
    public override string ToString() => Label;
}

public sealed class SourceItem : Observable
{
    private readonly Action<SourceItem, bool> _onToggle;
    public string Id { get; }
    public string? CustomId { get; }
    public bool IsCustom => CustomId != null;
    public string Icon { get; }
    public string? Homepage { get; }
    public string Url { get; }

    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    private string _description = "";
    public string Description { get => _description; set => Set(ref _description, value); }

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { if (Set(ref _enabled, value)) _onToggle(this, value); }
    }

    public void SetEnabledSilently(bool value) { _enabled = value; Raise(nameof(Enabled)); }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    private Brush _statusBrush = Palette.Text3;
    public Brush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }

    private bool _loading;
    public bool Loading { get => _loading; set => Set(ref _loading, value); }

    public SourceItem(string id, string? customId, string icon, string url, string? homepage, Action<SourceItem, bool> onToggle)
    {
        Id = id;
        CustomId = customId;
        Icon = icon;
        Url = url;
        Homepage = homepage;
        _onToggle = onToggle;
    }
}

public sealed partial class MainViewModel : Observable
{
    private readonly AppController C;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _toastTimer;
    private bool _suspendFilter;
    private CancellationTokenSource? _testCts;

    public event Action? ScrollToTopRequested;

    public MainViewModel(AppController controller)
    {
        C = controller;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Refilter(); ScrollToTopRequested?.Invoke(); };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3200) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastVisible = false; };

        C.LibraryChanged += OnLibraryChanged;
        C.PlayerChanged += OnPlayerChanged;
        C.FavoritesChanged += OnFavoritesChanged;
        Loc.I.LanguageChanged += OnLanguageChanged;

        TogglePlayCommand = new RelayCommand(() => C.TogglePlay());
        ReconnectCommand = new RelayCommand(() => C.Reconnect());
        ToggleMuteCommand = new RelayCommand(() => C.ToggleMute());
        ToggleIconsCommand = new RelayCommand(() => C.ToggleDesktopIcons());
        RefreshSourcesCommand = new RelayCommand(() => _ = C.ReloadLibraryAsync(true));
        TestCommand = new RelayCommand(() => _ = TestVisibleAsync());
        CancelTestCommand = new RelayCommand(() => _testCts?.Cancel());
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        GoSourcesCommand = new RelayCommand(() => Page = Page.Sources);
        GoBrowseCommand = new RelayCommand(() => Page = Page.Browse);
        FinishOnboardingCommand = new RelayCommand(FinishOnboarding);
        InitSettingsCommands();

        _showOnboarding = !C.Settings.OnboardingComplete;
        BuildSourceItems();
        RebuildFacets();
        Refilter();
        OnPlayerChanged();
    }

    public RelayCommand TogglePlayCommand { get; }
    public RelayCommand ReconnectCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand ToggleIconsCommand { get; }
    public RelayCommand RefreshSourcesCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand CancelTestCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand GoSourcesCommand { get; }
    public RelayCommand GoBrowseCommand { get; }
    public RelayCommand FinishOnboardingCommand { get; }

    public AppController Controller => C;

    // ============================================================================ navigation

    private Page _page = Page.Browse;
    public Page Page
    {
        get => _page;
        set
        {
            if (!Set(ref _page, value)) return;
            Raise(nameof(IsChannelsPage));
            Raise(nameof(IsSourcesPage));
            Raise(nameof(IsSettingsPage));
            Raise(nameof(PageTitle));
            Raise(nameof(PageSubtitle));
            if (IsChannelsPage)
            {
                RebuildFacets();
                Refilter();
                ScrollToTopRequested?.Invoke();
            }
            if (value == Page.Sources) UpdateSourceStatus();
            if (value == Page.Settings) _ = LoadYtDlpVersionAsync();
        }
    }

    public bool IsChannelsPage => _page <= Page.Mine;
    public bool IsSourcesPage => _page == Page.Sources;
    public bool IsSettingsPage => _page == Page.Settings;

    public string PageTitle => _page switch
    {
        Page.Favorites => Loc.T("nav_favorites"),
        Page.Recent => Loc.T("nav_recent"),
        Page.Mine => Loc.T("nav_mine"),
        Page.Sources => Loc.T("nav_sources"),
        Page.Settings => Loc.T("nav_settings"),
        _ => Loc.T("nav_browse"),
    };

    public string PageSubtitle => _page switch
    {
        Page.Favorites => Loc.T("sub_favorites"),
        Page.Recent => Loc.T("sub_recent"),
        Page.Mine => Loc.T("sub_mine"),
        Page.Sources => Loc.T("sub_sources"),
        Page.Settings => Loc.T("sub_settings"),
        _ => Loc.T("sub_browse"),
    };

    // ============================================================================ onboarding

    private bool _showOnboarding;
    public bool ShowOnboarding { get => _showOnboarding; set => Set(ref _showOnboarding, value); }

    private void FinishOnboarding()
    {
        ShowOnboarding = false;
        C.FinishOnboarding();
    }

    // ============================================================================ filters

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value ?? "")) return;
            if (!IsChannelsPage) Page = Page.Browse;
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    public ObservableCollection<CategoryItem> CategoryItems { get; } = new();

    private CategoryItem? _selectedCategory;
    public CategoryItem? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!Set(ref _selectedCategory, value) || _suspendFilter) return;
            Refilter();
            ScrollToTopRequested?.Invoke();
        }
    }

    private List<FilterOption> _countries = new();
    public List<FilterOption> Countries { get => _countries; private set => Set(ref _countries, value); }

    private FilterOption? _selectedCountry;
    public FilterOption? SelectedCountry
    {
        get => _selectedCountry;
        set { if (Set(ref _selectedCountry, value) && !_suspendFilter && value != null) { Refilter(); ScrollToTopRequested?.Invoke(); } }
    }

    private List<FilterOption> _languages = new();
    public List<FilterOption> Languages { get => _languages; private set => Set(ref _languages, value); }

    private FilterOption? _selectedLanguage;
    public FilterOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set { if (Set(ref _selectedLanguage, value) && !_suspendFilter && value != null) { Refilter(); ScrollToTopRequested?.Invoke(); } }
    }

    private List<FilterOption> _sourceFilters = new();
    public List<FilterOption> SourceFilters { get => _sourceFilters; private set => Set(ref _sourceFilters, value); }

    private FilterOption? _selectedSource;
    public FilterOption? SelectedSource
    {
        get => _selectedSource;
        set { if (Set(ref _selectedSource, value) && !_suspendFilter && value != null) { Refilter(); ScrollToTopRequested?.Invoke(); } }
    }

    private bool _onlyWorking;
    public bool OnlyWorking
    {
        get => _onlyWorking;
        set { if (Set(ref _onlyWorking, value)) Refilter(); }
    }

    private IReadOnlyList<Channel> _channels = Array.Empty<Channel>();
    public IReadOnlyList<Channel> Channels { get => _channels; private set => Set(ref _channels, value); }

    private int _resultCount;
    public int ResultCount { get => _resultCount; private set => Set(ref _resultCount, value); }

    public string ResultText => Loc.F("result_count", _resultCount.ToString("N0"));

    public bool HasActiveFilters =>
        (_selectedCountry != null && _selectedCountry.Key != FilterOption.AllKey) ||
        (_selectedLanguage != null && _selectedLanguage.Key != FilterOption.AllKey) ||
        (_selectedSource != null && _selectedSource.Key != FilterOption.AllKey) ||
        (_selectedCategory != null && _selectedCategory.Key != Categories.All) ||
        _onlyWorking || _searchText.Trim().Length > 0;

    public bool IsLibraryLoading => C.IsLibraryLoading;
    public bool IsLibraryEmpty => !C.IsLibraryLoading && C.Library.Channels.Count == 0;

    public string EmptyTitle => _page switch
    {
        Page.Favorites => Loc.T("empty_fav_title"),
        Page.Recent => Loc.T("empty_recent_title"),
        Page.Mine => Loc.T("empty_mine_title"),
        _ => IsLibraryEmpty ? Loc.T("empty_lib_title") : Loc.T("empty_filter_title"),
    };

    public string EmptyText => _page switch
    {
        Page.Favorites => Loc.T("empty_fav_text"),
        Page.Recent => Loc.T("empty_recent_text"),
        Page.Mine => Loc.T("empty_mine_text"),
        _ => IsLibraryEmpty ? Loc.T("empty_lib_text") : Loc.T("empty_filter_text"),
    };

    private IEnumerable<Channel> ScopeChannels() => _page switch
    {
        Page.Favorites => C.FavoriteChannels(),
        Page.Recent => C.RecentChannels(),
        Page.Mine => C.Library.Channels.Where(c => c.IsUserLink),
        _ => C.Library.Channels,
    };

    private bool UserScope => _page is Page.Favorites or Page.Recent or Page.Mine;

    private bool PassesBase(Channel c)
    {
        var s = C.Settings;
        if (!UserScope)
        {
            if (s.HideAdult && c.IsNsfw) return false;
            if (s.HideGeoBlocked && c.GeoBlocked) return false;
            if (s.HideBroken && c.IsAlive == false) return false;
        }
        if (_onlyWorking && c.IsAlive != true) return false;
        return true;
    }

    private void RebuildFacets()
    {
        _suspendFilter = true;
        try
        {
            var scope = ScopeChannels().Where(PassesBase).ToList();

            // Categories: known ones in their natural order, then playlist-specific groups by size.
            string selectedCat = _selectedCategory?.Key ?? Categories.All;
            var catCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in scope)
                foreach (var k in c.Categories)
                    catCounts[k] = catCounts.TryGetValue(k, out int n) ? n + 1 : 1;
            var keys = catCounts.Keys
                .Where(k => k != Categories.Adult || !C.Settings.HideAdult)
                .OrderBy(k => Categories.IsKnown(k) ? 0 : 1)
                .ThenBy(k => Categories.IsKnown(k) ? Categories.Order(k) : -catCounts[k])
                .ThenBy(k => k)
                .ToList();
            CategoryItems.Clear();
            var all = new CategoryItem(Categories.All);
            CategoryItems.Add(all);
            foreach (var k in keys) CategoryItems.Add(new CategoryItem(k));
            SelectedCategory = CategoryItems.FirstOrDefault(x => x.Key == selectedCat) ?? all;

            string cKey = _selectedCountry?.Key ?? FilterOption.AllKey;
            var countries = new List<FilterOption> { new(FilterOption.AllKey, Loc.T("filter_all_countries"), 0) };
            countries.AddRange(scope.SelectMany(c => c.Countries)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FilterOption(g.Key, Sources.Countries.Name(g.Key), g.Count()))
                .OrderByDescending(o => o.Count).ThenBy(o => o.Label));
            Countries = countries;
            SelectedCountry = countries.FirstOrDefault(x => x.Key == cKey) ?? countries[0];

            string lKey = _selectedLanguage?.Key ?? FilterOption.AllKey;
            var langs = new List<FilterOption> { new(FilterOption.AllKey, Loc.T("filter_all_languages"), 0) };
            langs.AddRange(scope.SelectMany(c => c.Languages)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FilterOption(g.Key, LanguageLabel(g.Key), g.Count()))
                .OrderByDescending(o => o.Count).ThenBy(o => o.Label));
            Languages = langs;
            SelectedLanguage = langs.FirstOrDefault(x => x.Key == lKey) ?? langs[0];

            string sKey = _selectedSource?.Key ?? FilterOption.AllKey;
            var srcs = new List<FilterOption> { new(FilterOption.AllKey, Loc.T("filter_all_sources"), 0) };
            srcs.AddRange(scope.SelectMany(c => c.SourceIds)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FilterOption(g.Key, SourceLabel(g.Key), g.Count()))
                .OrderByDescending(o => o.Count));
            SourceFilters = srcs;
            SelectedSource = srcs.FirstOrDefault(x => x.Key == sKey) ?? srcs[0];
        }
        finally
        {
            _suspendFilter = false;
        }
    }

    private static string LanguageLabel(string lang)
    {
        if (!Loc.I.IsRtl) return lang;
        return lang.ToLowerInvariant() switch
        {
            "persian" or "farsi" => "فارسی",
            "english" => "انگلیسی",
            "arabic" => "عربی",
            "turkish" => "ترکی",
            "kurdish" or "central kurdish" or "northern kurdish" => "کردی",
            "azerbaijani" or "south azerbaijani" or "north azerbaijani" => "آذری",
            "german" => "آلمانی",
            "french" => "فرانسوی",
            "spanish" => "اسپانیایی",
            "russian" => "روسی",
            "italian" => "ایتالیایی",
            "chinese" => "چینی",
            "japanese" => "ژاپنی",
            "korean" => "کره‌ای",
            "hindi" => "هندی",
            "urdu" => "اردو",
            "pashto" => "پشتو",
            "dari" => "دری",
            "portuguese" => "پرتغالی",
            "armenian" => "ارمنی",
            "hebrew" => "عبری",
            "dutch" => "هلندی",
            "greek" => "یونانی",
            _ => lang,
        };
    }

    private string SourceLabel(string id)
    {
        if (id == ChannelLibrary.MineSourceId) return Loc.T("src_mine");
        if (id.StartsWith("custom:"))
        {
            var cs = C.Settings.CustomSources.FirstOrDefault(x => "custom:" + x.Id == id);
            return cs?.Name ?? id;
        }
        return C.Catalog.Find(id)?.DisplayName ?? id;
    }

    public void Refilter()
    {
        string cat = _selectedCategory?.Key ?? Categories.All;
        string country = _selectedCountry?.Key ?? FilterOption.AllKey;
        string lang = _selectedLanguage?.Key ?? FilterOption.AllKey;
        string src = _selectedSource?.Key ?? FilterOption.AllKey;
        string q = TextUtil.SearchKey(_searchText.Trim());

        var result = new List<Channel>();
        var catCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int total = 0;

        foreach (var c in ScopeChannels())
        {
            if (!PassesBase(c)) continue;
            if (country != FilterOption.AllKey && !c.Countries.Contains(country)) continue;
            if (lang != FilterOption.AllKey && !c.Languages.Contains(lang)) continue;
            if (src != FilterOption.AllKey && !c.SourceIds.Contains(src)) continue;
            if (q.Length > 0 && !c.SearchKey.Contains(q, StringComparison.Ordinal)
                && !c.Url.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;

            total++;
            foreach (var k in c.Categories)
                catCounts[k] = catCounts.TryGetValue(k, out int n) ? n + 1 : 1;
            if (cat == Categories.All || c.Categories.Contains(cat)) result.Add(c);
        }

        foreach (var item in CategoryItems)
            item.Count = item.Key == Categories.All ? total : (catCounts.TryGetValue(item.Key, out int n) ? n : 0);

        if (_page == Page.Browse || _page == Page.Mine)
        {
            result = result
                .OrderBy(c => c.IsAlive == true ? 0 : c.IsAlive == null ? 1 : 2)
                .ThenBy(c => c.SortIndex)
                .ToList();
        }

        Channels = result;
        ResultCount = result.Count;
        Raise(nameof(ResultText));
        Raise(nameof(HasActiveFilters));
        Raise(nameof(EmptyTitle));
        Raise(nameof(EmptyText));
    }

    private void ClearFilters()
    {
        _suspendFilter = true;
        _searchText = "";
        Raise(nameof(SearchText));
        _onlyWorking = false;
        Raise(nameof(OnlyWorking));
        SelectedCategory = CategoryItems.FirstOrDefault();
        SelectedCountry = _countries.FirstOrDefault();
        SelectedLanguage = _languages.FirstOrDefault();
        SelectedSource = _sourceFilters.FirstOrDefault();
        _suspendFilter = false;
        Refilter();
    }

    // ============================================================================ events from the controller

    private void OnLibraryChanged()
    {
        Raise(nameof(IsLibraryLoading));
        Raise(nameof(IsLibraryEmpty));
        if (!C.IsLibraryLoading || C.Library.Channels.Count > 0)
        {
            RebuildFacets();
            Refilter();
        }
        UpdateSourceStatus();
    }

    private void OnFavoritesChanged()
    {
        if (_page == Page.Favorites) { RebuildFacets(); Refilter(); }
    }

    private void OnLanguageChanged()
    {
        foreach (var item in CategoryItems) item.Label = Categories.Label(item.Key);
        Raise(nameof(PageTitle));
        Raise(nameof(PageSubtitle));
        RebuildFacets();
        Refilter();
        RefreshSourceTexts();
        RaiseSettingsOptions();
        OnPlayerChanged();
    }

    // ============================================================================ now playing

    public Channel? NowChannel => C.Current;
    public string NowName => C.Current?.Name ?? Loc.T("np_nothing");
    public string NowStatus { get; private set; } = "";
    public Brush NowStatusBrush { get; private set; } = Palette.Text3;
    public bool IsActive { get; private set; }
    public string PlayGlyph => IsActive ? "" : "";
    public string PlayTooltip => IsActive ? Loc.T("tip_stop") : Loc.T("tip_play");
    public bool IsMuted => C.Settings.Muted;
    public string MuteGlyph => C.Settings.Muted || C.Settings.Volume == 0 ? "" : "";
    public bool IconsHidden => C.IconsHidden;
    public string IconsTooltip => C.IconsHidden ? Loc.T("tip_show_icons") : Loc.T("tip_hide_icons");

    private bool _volumeGuard;
    public double Volume
    {
        get => C.Settings.Volume;
        set
        {
            if (_volumeGuard) return;
            int v = (int)Math.Round(value);
            if (v == C.Settings.Volume) return;
            _volumeGuard = true;
            try { C.SetVolume(v); } finally { _volumeGuard = false; }
            Raise();
            Raise(nameof(VolumeText));
            Raise(nameof(MuteGlyph));
        }
    }

    public string VolumeText => $"{C.Settings.Volume}";

    private void OnPlayerChanged()
    {
        var (text, brush) = C.State switch
        {
            PlayerState.Idle => (Loc.T("st_idle"), Palette.Text3),
            PlayerState.Resolving => (Loc.T("st_resolving"), Palette.Warn),
            PlayerState.Connecting => (Loc.T("st_connecting"), Palette.Warn),
            PlayerState.Buffering => (Loc.F("st_buffering", C.StatusDetail), Palette.Warn),
            PlayerState.Playing => (C.ActiveAutoAction == AutoAction.Mute ? Loc.T("st_playing_muted") : Loc.T("st_playing"), Palette.Good),
            PlayerState.Paused => (Loc.T("st_paused_auto"), Palette.Text2),
            PlayerState.Stopped => (Loc.T("st_stopped"), Palette.Text3),
            PlayerState.Reconnecting => (C.StatusDetail, Palette.Warn),
            PlayerState.Error => (Loc.T("st_failed") + (string.IsNullOrEmpty(C.StatusDetail) ? "" : " — " + C.StatusDetail), Palette.Bad),
            _ => ("", Palette.Text3),
        };
        NowStatus = text;
        NowStatusBrush = brush;
        IsActive = C.State is not (PlayerState.Idle or PlayerState.Stopped or PlayerState.Error);
        Raise(nameof(NowChannel));
        Raise(nameof(NowName));
        Raise(nameof(NowStatus));
        Raise(nameof(NowStatusBrush));
        Raise(nameof(IsActive));
        Raise(nameof(PlayGlyph));
        Raise(nameof(PlayTooltip));
        Raise(nameof(IsMuted));
        Raise(nameof(MuteGlyph));
        Raise(nameof(IconsHidden));
        Raise(nameof(IconsTooltip));
        if (!_volumeGuard) { Raise(nameof(Volume)); Raise(nameof(VolumeText)); }
    }

    // ============================================================================ actions used by the views

    public void Play(Channel c) => C.Play(c);

    public void ToggleFavorite(Channel c)
    {
        C.ToggleFavorite(c);
        Toast(c.IsFavorite ? Loc.F("toast_fav_added", c.Name) : Loc.F("toast_fav_removed", c.Name));
    }

    public void RemoveLink(Channel c)
    {
        C.RemoveLink(c);
        Toast(Loc.F("toast_removed", c.Name));
    }

    public async Task TestOneAsync(Channel c)
    {
        Toast(Loc.F("toast_testing", c.Name));
        bool? alive = await HealthChecker.CheckAsync(c, CancellationToken.None);
        C.MarkHealth(c, alive);
        C.Health.Save();
        Toast(alive == true ? Loc.F("toast_test_ok", c.Name) : alive == false ? Loc.F("toast_test_bad", c.Name) : Loc.F("toast_test_unknown", c.Name));
    }

    // ---------------- bulk test ----------------

    private bool _isTesting;
    public bool IsTesting { get => _isTesting; private set => Set(ref _isTesting, value); }

    private string _testText = "";
    public string TestText { get => _testText; private set => Set(ref _testText, value); }

    private double _testProgress;
    public double TestProgress { get => _testProgress; private set => Set(ref _testProgress, value); }

    private async Task TestVisibleAsync()
    {
        if (_isTesting) return;
        var targets = _channels.Take(400).ToList();
        if (targets.Count == 0) return;

        var cts = _testCts = new CancellationTokenSource();
        IsTesting = true;
        int done = 0, ok = 0;
        TestProgress = 0;
        TestText = Loc.F("test_progress", 0, targets.Count, 0);
        var ui = Application.Current.Dispatcher;

        await HealthChecker.RunAsync(targets, (c, alive) =>
        {
            ui.BeginInvoke(new Action(() =>
            {
                C.MarkHealth(c, alive);
                done++;
                if (alive == true) ok++;
                TestProgress = (double)done / targets.Count;
                TestText = Loc.F("test_progress", done, targets.Count, ok);
            }));
        }, cts.Token);

        // Let queued UI updates land before summarising.
        await ui.InvokeAsync(() => { }, DispatcherPriority.Background);
        IsTesting = false;
        C.Health.Save();
        Toast(cts.IsCancellationRequested ? Loc.F("test_cancelled", ok, done) : Loc.F("test_done", ok, done));
        Refilter();
    }

    // ---------------- toast ----------------

    private string _toastText = "";
    public string ToastText { get => _toastText; private set => Set(ref _toastText, value); }

    private bool _toastVisible;
    public bool ToastVisible { get => _toastVisible; private set => Set(ref _toastVisible, value); }

    public void Toast(string message)
    {
        ToastText = message;
        ToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ============================================================================ sources page

    public ObservableCollection<SourceItem> BuiltInSources { get; } = new();
    public ObservableCollection<SourceItem> CustomSourceItems { get; } = new();

    private void BuildSourceItems()
    {
        BuiltInSources.Clear();
        foreach (var def in C.Catalog.Sources)
        {
            var item = new SourceItem(def.Id, null, def.Icon, def.Playlists.FirstOrDefault()?.Url ?? "", def.Homepage, OnSourceToggled);
            item.SetEnabledSilently(C.Catalog.IsEnabled(C.Settings, def));
            BuiltInSources.Add(item);
        }
        RebuildCustomSourceItems();
        RefreshSourceTexts();
        UpdateSourceStatus();
    }

    public void RebuildCustomSourceItems()
    {
        CustomSourceItems.Clear();
        foreach (var cs in C.Settings.CustomSources)
        {
            var item = new SourceItem("custom:" + cs.Id, cs.Id, "", cs.Url, null, OnSourceToggled)
            {
                Name = cs.Name,
                Description = cs.Url,
            };
            item.SetEnabledSilently(cs.Enabled);
            CustomSourceItems.Add(item);
        }
        Raise(nameof(HasCustomSources));
    }

    public bool HasCustomSources => CustomSourceItems.Count > 0;

    private void RefreshSourceTexts()
    {
        foreach (var item in BuiltInSources)
        {
            var def = C.Catalog.Find(item.Id);
            if (def == null) continue;
            item.Name = def.Name.ToString();
            item.Description = def.Description.ToString();
        }
        UpdateSourceStatus();
    }

    private void OnSourceToggled(SourceItem item, bool enabled)
    {
        if (C.Settings.OnboardingComplete)
        {
            C.SetSourceEnabled(item.Id, enabled);
        }
        else
        {
            // During onboarding just remember the choice; the first load happens when the user finishes.
            if (item.IsCustom) return;
            C.Settings.Sources[item.Id] = enabled;
            C.SaveSoon();
        }
    }

    public void UpdateSourceStatus()
    {
        foreach (var item in BuiltInSources.Concat(CustomSourceItems))
        {
            if (!item.Enabled)
            {
                item.Loading = false;
                item.Status = Loc.T("src_off");
                item.StatusBrush = Palette.Text3;
                continue;
            }
            if (!C.Library.Status.TryGetValue(item.Id, out var st))
            {
                item.Loading = C.IsLibraryLoading;
                item.Status = C.IsLibraryLoading ? Loc.T("src_loading") : Loc.T("src_pending");
                item.StatusBrush = Palette.Text3;
                continue;
            }
            item.Loading = st.Loading;
            if (st.Loading)
            {
                item.Status = Loc.T("src_loading");
                item.StatusBrush = Palette.Text3;
            }
            else if (st.Count == 0 && st.Error != null)
            {
                item.Status = Loc.F("src_error", st.Error);
                item.StatusBrush = Palette.Bad;
            }
            else
            {
                string when = st.UpdatedAt.HasValue ? Loc.F("src_updated", RelativeTime(st.UpdatedAt.Value)) : "";
                item.Status = Loc.F("src_count", st.Count.ToString("N0")) + (when.Length > 0 ? "  ·  " + when : "") +
                              (st.FromStaleCache ? "  ·  " + Loc.T("src_stale") : "");
                item.StatusBrush = st.FromStaleCache ? Palette.Warn : Palette.Good;
            }
        }
    }

    private static string RelativeTime(DateTime local)
    {
        var d = DateTime.Now - local;
        if (d.TotalMinutes < 2) return Loc.T("time_now");
        if (d.TotalHours < 1) return Loc.F("time_min", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return Loc.F("time_hour", (int)d.TotalHours);
        return Loc.F("time_day", (int)d.TotalDays);
    }

    public void AddPlaylist(string name, string url)
    {
        C.AddCustomSource(name, url);
        RebuildCustomSourceItems();
        UpdateSourceStatus();
        Toast(Loc.T("toast_playlist_added"));
    }

    public void RemovePlaylist(SourceItem item)
    {
        if (item.CustomId == null) return;
        C.RemoveCustomSource(item.CustomId);
        RebuildCustomSourceItems();
        Toast(Loc.F("toast_removed", item.Name));
    }
}
