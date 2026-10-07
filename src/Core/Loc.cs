using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace TvDesk.Core;

/// <summary>
/// Persian / English strings. XAML: <c>Text="{l:T key}"</c> (updates live when the language changes).
/// Code: <c>Loc.T("key")</c> or <c>Loc.F("key", args)</c>.
/// To add a language: add a column here and a choice in Settings.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private string _lang = "fa";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string Language => _lang;
    public bool IsRtl => _lang == "fa";
    public FlowDirection Flow => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string this[string key] => Get(key);

    public static string T(string key) => I.Get(key);

    public static string F(string key, params object[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, I.Get(key), args); }
        catch { return I.Get(key); }
    }

    public string Get(string key)
    {
        if (Strings.TryGetValue(key, out var s)) return _lang == "fa" ? s.Fa : s.En;
        return key;
    }

    public void SetLanguage(string lang)
    {
        lang = lang == "en" ? "en" : "fa";
        if (lang == _lang) return;
        _lang = lang;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flow)));
        LanguageChanged?.Invoke();
    }

    private static readonly Dictionary<string, (string Fa, string En)> Strings = new()
    {
        // ---------- app / navigation
        ["app_tagline"] = ("تلویزیون زنده روی دسکتاپ", "Live TV on your desktop"),
        ["nav_watch"] = ("تماشا", "WATCH"),
        ["nav_manage"] = ("مدیریت", "MANAGE"),
        ["nav_browse"] = ("همه‌ی کانال‌ها", "All channels"),
        ["nav_favorites"] = ("علاقه‌مندی‌ها", "Favorites"),
        ["nav_recent"] = ("اخیراً پخش‌شده", "Recently played"),
        ["nav_mine"] = ("لینک‌های من", "My links"),
        ["nav_sources"] = ("منابع کانال", "Channel sources"),
        ["nav_settings"] = ("تنظیمات", "Settings"),
        ["sub_browse"] = ("روی هر کانال کلیک کن تا پس‌زمینه‌ی دسکتاپت شود.", "Click any channel to make it your desktop background."),
        ["sub_favorites"] = ("کانال‌هایی که ستاره زده‌ای. با Ctrl+Alt+←/→ بینشان جابه‌جا شو.", "Channels you starred. Switch between them with Ctrl+Alt+←/→."),
        ["sub_recent"] = ("آخرین کانال‌هایی که پخش کرده‌ای.", "The channels you played most recently."),
        ["sub_mine"] = ("لینک‌ها و فایل‌هایی که خودت اضافه کرده‌ای.", "Links and files you added yourself."),
        ["sub_sources"] = ("فهرست‌های رایگان را روشن یا خاموش کن، یا لیست IPTV خودت را اضافه کن.", "Turn free lists on or off, or add your own IPTV playlist."),
        ["sub_settings"] = ("رفتار والپیپر، مکث هوشمند و موارد پیشرفته.", "Wallpaper behaviour, smart pause and advanced options."),
        ["btn_add"] = ("افزودن کانال یا لیست", "Add channel or playlist"),
        ["search_placeholder"] = ("جستجوی کانال…   (Ctrl+F)", "Search channels…   (Ctrl+F)"),
        ["categories"] = ("دسته‌بندی‌ها", "CATEGORIES"),
        ["cat_all"] = ("همه", "All"),
        ["cat_none"] = ("بدون دسته", "No category"),
        ["hotkeys_title"] = ("میان‌بُرها", "Shortcuts"),
        ["hk_play"] = ("پخش / توقف", "Play / stop"),
        ["hk_mute"] = ("قطع و وصل صدا", "Mute"),
        ["hk_icons"] = ("آیکون‌های دسکتاپ", "Desktop icons"),
        ["hk_next"] = ("علاقه‌مندی قبلی/بعدی", "Prev / next favorite"),

        // ---------- filters / list
        ["filter_country"] = ("کشور", "Country"),
        ["filter_language"] = ("زبان", "Language"),
        ["filter_source"] = ("منبع", "Source"),
        ["filter_all_countries"] = ("همه‌ی کشورها", "All countries"),
        ["filter_all_languages"] = ("همه‌ی زبان‌ها", "All languages"),
        ["filter_all_sources"] = ("همه‌ی منابع", "All sources"),
        ["filter_only_working"] = ("فقط سالم‌ها", "Working only"),
        ["btn_clear_filters"] = ("پاک کردن فیلترها", "Clear filters"),
        ["btn_test"] = ("تست کانال‌ها", "Test channels"),
        ["tip_test"] = ("کانال‌های همین لیست را بررسی می‌کند و خراب‌ها را علامت می‌زند (تا ۴۰۰ کانال).", "Checks the channels in this list and marks broken ones (up to 400)."),
        ["btn_cancel"] = ("لغو", "Cancel"),
        ["result_count"] = ("{0} کانال", "{0} channels"),
        ["loading_library"] = ("در حال آماده‌سازی فهرست کانال‌ها…", "Loading channel lists…"),
        ["badge_live"] = ("در حال پخش", "ON AIR"),
        ["badge_geo"] = ("محدودیت منطقه‌ای", "Geo-blocked"),
        ["health_ok"] = ("سالم (آخرین بررسی)", "Working (last check)"),
        ["health_bad"] = ("خراب (آخرین بررسی)", "Broken (last check)"),
        ["health_unknown"] = ("بررسی نشده", "Not tested"),
        ["empty_lib_title"] = ("هنوز کانالی نیست", "No channels yet"),
        ["empty_lib_text"] = ("یک منبع رایگان را روشن کن یا لینک خودت را اضافه کن.", "Turn on a free source or add your own link."),
        ["empty_filter_title"] = ("کانالی پیدا نشد", "Nothing found"),
        ["empty_filter_text"] = ("فیلترها یا عبارت جستجو را تغییر بده.", "Try other filters or another search."),
        ["empty_fav_title"] = ("هنوز علاقه‌مندی نداری", "No favorites yet"),
        ["empty_fav_text"] = ("روی ستاره‌ی کنار هر کانال بزن تا اینجا بیاید.", "Tap the star next to any channel to keep it here."),
        ["empty_recent_title"] = ("هنوز چیزی پخش نکرده‌ای", "Nothing played yet"),
        ["empty_recent_text"] = ("کانال‌هایی که پخش کنی اینجا نمایش داده می‌شوند.", "Channels you play will show up here."),
        ["empty_mine_title"] = ("لینکی اضافه نکرده‌ای", "No links of your own"),
        ["empty_mine_text"] = ("لینک یوتیوب، توییچ، m3u8 یا یک فایل ویدیویی را با دکمه‌ی «افزودن» اضافه کن.", "Add a YouTube, Twitch or m3u8 link — or a video file — with the Add button."),
        ["btn_open_sources"] = ("رفتن به منابع", "Open sources"),
        ["menu_play"] = ("پخش روی دسکتاپ", "Play on desktop"),
        ["menu_favorite"] = ("علاقه‌مندی", "Favorite"),
        ["menu_test"] = ("تست این کانال", "Test this channel"),
        ["menu_copy"] = ("کپی لینک", "Copy link"),
        ["menu_remove"] = ("حذف", "Remove"),
        ["test_progress"] = ("بررسی {0} از {1} — {2} سالم", "Checked {0} of {1} — {2} working"),
        ["test_done"] = ("بررسی تمام شد: {0} سالم از {1}", "Done: {0} of {1} working"),
        ["test_cancelled"] = ("بررسی لغو شد: {0} سالم از {1}", "Cancelled: {0} of {1} working"),

        // ---------- now playing / status
        ["np_nothing"] = ("چیزی پخش نمی‌شود", "Nothing playing"),
        ["st_idle"] = ("یک کانال انتخاب کن", "Pick a channel"),
        ["st_resolving"] = ("در حال پیدا کردن استریم…", "Finding the stream…"),
        ["st_connecting"] = ("در حال اتصال…", "Connecting…"),
        ["st_buffering"] = ("در حال بافر… {0}", "Buffering… {0}"),
        ["st_playing"] = ("در حال پخش روی دسکتاپ", "Playing on your desktop"),
        ["st_playing_muted"] = ("در حال پخش — صدا موقتاً قطع است", "Playing — muted for now"),
        ["st_paused_auto"] = ("مکث خودکار (برنامه‌ی تمام‌صفحه / قفل / باتری)", "Auto-paused (fullscreen app / locked / battery)"),
        ["st_stopped"] = ("متوقف شد — اینترنتی مصرف نمی‌شود", "Stopped — no data is used"),
        ["st_retry_in"] = ("قطع شد؛ اتصال دوباره تا {0} ثانیه‌ی دیگر", "Disconnected — retrying in {0}s"),
        ["st_failed"] = ("این کانال پخش نشد", "This channel didn't play"),
        ["st_error"] = ("خطای پخش", "Playback error"),
        ["st_ended"] = ("استریم قطع شد", "Stream ended"),
        ["st_timeout"] = ("پاسخی نیامد", "No response"),
        ["st_stalled"] = ("تصویر متوقف ماند", "Stream stalled"),
        ["st_resolve_failed"] = ("لینک باز نشد:", "Couldn't open the link:"),
        ["tip_play"] = ("پخش (Ctrl+Alt+P)", "Play (Ctrl+Alt+P)"),
        ["tip_stop"] = ("توقف کامل و قطع مصرف اینترنت (Ctrl+Alt+P)", "Stop completely, no data used (Ctrl+Alt+P)"),
        ["tip_reconnect"] = ("اتصال دوباره", "Reconnect"),
        ["tip_mute"] = ("قطع و وصل صدا (Ctrl+Alt+M)", "Mute (Ctrl+Alt+M)"),
        ["tip_hide_icons"] = ("پنهان کردن آیکون‌های دسکتاپ (Ctrl+Alt+D)", "Hide desktop icons (Ctrl+Alt+D)"),
        ["tip_show_icons"] = ("نمایش آیکون‌های دسکتاپ (Ctrl+Alt+D)", "Show desktop icons (Ctrl+Alt+D)"),
        ["tip_minimize"] = ("کوچک کردن", "Minimize"),
        ["tip_maximize"] = ("بزرگ کردن", "Maximize"),
        ["tip_restore"] = ("اندازه‌ی قبلی", "Restore"),
        ["tip_close"] = ("بستن (TvDesk در سینی سیستم ادامه می‌دهد)", "Close (TvDesk keeps running in the tray)"),
        ["tip_homepage"] = ("صفحه‌ی پروژه", "Project page"),

        // ---------- wallpaper overlay
        ["ov_connecting"] = ("در حال اتصال", "Connecting"),
        ["ov_retry"] = ("اتصال دوباره تا {0} ثانیه", "Reconnecting in {0}s"),
        ["ov_failed"] = ("این کانال در دسترس نیست — از TvDesk کانال دیگری انتخاب کن", "This channel is unavailable — pick another one in TvDesk"),
        ["ov_audio_only"] = ("فقط صدا", "Audio only"),

        // ---------- toasts
        ["toast_fav_added"] = ("«{0}» به علاقه‌مندی‌ها اضافه شد", "Added “{0}” to favorites"),
        ["toast_fav_removed"] = ("«{0}» از علاقه‌مندی‌ها حذف شد", "Removed “{0}” from favorites"),
        ["toast_removed"] = ("«{0}» حذف شد", "Removed “{0}”"),
        ["toast_saved"] = ("«{0}» ذخیره شد و در حال پخش است", "Saved “{0}” and playing it"),
        ["toast_testing"] = ("در حال بررسی «{0}»…", "Testing “{0}”…"),
        ["toast_test_ok"] = ("«{0}» سالم است", "“{0}” works"),
        ["toast_test_bad"] = ("«{0}» جواب نمی‌دهد", "“{0}” isn't responding"),
        ["toast_test_unknown"] = ("این نوع لینک را نمی‌شود سریع بررسی کرد", "This kind of link can't be checked quickly"),
        ["toast_pick_channel"] = ("آماده است! روی یک کانال کلیک کن تا پس‌زمینه‌ات شود.", "All set! Click a channel to make it your wallpaper."),
        ["toast_copied"] = ("لینک کپی شد", "Link copied"),
        ["toast_playlist_added"] = ("لیست اضافه شد؛ در حال بارگذاری کانال‌ها…", "Playlist added — loading its channels…"),
        ["toast_cache_cleared"] = ("حافظه‌ی موقت پاک شد؛ لیست‌ها دوباره دانلود می‌شوند", "Cache cleared — lists are being downloaded again"),
        ["toast_reattached"] = ("والپیپر دوباره به دسکتاپ وصل شد", "Wallpaper re-attached to the desktop"),
        ["toast_health_reset"] = ("وضعیت سالم/خراب همه‌ی کانال‌ها پاک شد", "All working/broken marks were cleared"),
        ["toast_vlc_failed"] = ("موتور پخش (VLC) بارگذاری نشد. پوشه‌ی برنامه را کامل از حالت فشرده خارج کن.", "The VLC engine failed to load. Extract the whole app folder from the ZIP."),
        ["toast_ytdlp_ok"] = ("yt-dlp به‌روز است", "yt-dlp is up to date"),
        ["toast_ytdlp_fail"] = ("به‌روزرسانی yt-dlp انجام نشد (اینترنت/پروکسی را بررسی کن)", "Couldn't update yt-dlp (check internet / proxy)"),
        ["err_ytdlp_missing"] = ("yt-dlp پیدا نشد و دانلود هم نشد", "yt-dlp is missing and couldn't be downloaded"),

        // ---------- tray
        ["tray_play"] = ("پخش", "Play"),
        ["tray_stop"] = ("توقف", "Stop"),
        ["tray_reconnect"] = ("اتصال دوباره", "Reconnect"),
        ["tray_mute"] = ("قطع صدا", "Mute"),
        ["tray_unmute"] = ("وصل صدا", "Unmute"),
        ["tray_hide_icons"] = ("پنهان کردن آیکون‌های دسکتاپ", "Hide desktop icons"),
        ["tray_show_icons"] = ("نمایش آیکون‌های دسکتاپ", "Show desktop icons"),
        ["tray_open"] = ("باز کردن TvDesk", "Open TvDesk"),
        ["tray_exit"] = ("خروج", "Exit"),
        ["tray_hint_title"] = ("TvDesk هنوز روشن است", "TvDesk is still running"),
        ["tray_hint_body"] = ("والپیپر ادامه دارد. برای باز کردن دوباره روی آیکون کنار ساعت کلیک کن یا Ctrl+Alt+T را بزن.", "Your wallpaper keeps playing. Click the icon near the clock or press Ctrl+Alt+T to reopen."),

        // ---------- sources page
        ["sources_free"] = ("منابع رایگان", "FREE SOURCES"),
        ["sources_custom"] = ("لیست‌های شخصی", "YOUR PLAYLISTS"),
        ["sources_custom_empty_title"] = ("لیست IPTV خودت را اضافه کن", "Add your own IPTV playlist"),
        ["sources_custom_empty_text"] = ("لینک فایل m3u / m3u8 یا یک فایل روی کامپیوتر را بده تا کانال‌هایش با دسته‌بندی به کتابخانه اضافه شوند.", "Paste an m3u / m3u8 link or pick a file — its channels are added to the library with their categories."),
        ["sources_legal"] = ("TvDesk فقط یک پخش‌کننده است و هیچ استریمی را میزبانی نمی‌کند. فهرست‌ها از پروژه‌های عمومی می‌آیند؛ برخی کانال‌ها ممکن است قطع یا در منطقه‌ی تو مسدود باشند. فقط از استریم‌هایی استفاده کن که اجازه‌ی تماشایشان را داری.", "TvDesk is only a player and hosts no streams. Lists come from public projects; some channels may be offline or blocked in your region. Only use streams you're allowed to watch."),
        ["btn_refresh_all"] = ("به‌روزرسانی همه", "Refresh all"),
        ["btn_add_playlist"] = ("افزودن لیست", "Add playlist"),
        ["src_off"] = ("خاموش", "Off"),
        ["src_loading"] = ("در حال بارگذاری…", "Loading…"),
        ["src_pending"] = ("بعد از ذخیره بارگذاری می‌شود", "Will load shortly"),
        ["src_count"] = ("{0} کانال", "{0} channels"),
        ["src_updated"] = ("به‌روزشده {0}", "updated {0}"),
        ["src_stale"] = ("نسخه‌ی ذخیره‌شده (دانلود نشد)", "cached copy (download failed)"),
        ["src_error"] = ("بارگذاری نشد: {0}", "Couldn't load: {0}"),
        ["src_mine"] = ("لینک‌های من", "My links"),
        ["src_link"] = ("لینک", "Link"),
        ["time_now"] = ("همین الان", "just now"),
        ["time_min"] = ("{0} دقیقه پیش", "{0} min ago"),
        ["time_hour"] = ("{0} ساعت پیش", "{0} h ago"),
        ["time_day"] = ("{0} روز پیش", "{0} days ago"),

        // ---------- settings
        ["set_general"] = ("عمومی", "General"),
        ["set_language"] = ("زبان برنامه", "Language"),
        ["set_autostart"] = ("اجرا با روشن شدن ویندوز", "Start with Windows"),
        ["set_autostart_hint"] = ("بی‌صدا در سینی سیستم باز می‌شود و والپیپر را ادامه می‌دهد.", "Starts quietly in the tray and resumes the wallpaper."),
        ["set_resume"] = ("ادامه‌ی آخرین کانال", "Resume last channel"),
        ["set_resume_hint"] = ("هنگام باز شدن، آخرین کانال خودکار پخش شود.", "Play the last channel automatically on start."),
        ["set_wallpaper"] = ("والپیپر", "Wallpaper"),
        ["set_monitor"] = ("نمایشگر", "Display"),
        ["set_monitor_hint"] = ("روی کدام مانیتور پخش شود.", "Which monitor shows the video."),
        ["set_fit"] = ("اندازه‌ی تصویر", "Picture size"),
        ["set_fit_hint"] = ("«پر کردن» کل صفحه را بدون نوار سیاه می‌پوشاند.", "“Fill” covers the whole screen without black bars."),
        ["set_quality"] = ("حداکثر کیفیت", "Maximum quality"),
        ["set_quality_hint"] = ("کیفیت پایین‌تر یعنی مصرف اینترنت و پردازنده‌ی کمتر.", "Lower quality uses less data and CPU."),
        ["set_smart"] = ("مکث هوشمند", "Smart pause"),
        ["set_smart_hint"] = ("«مکث» آخرین تصویر را نگه می‌دارد و استریم را قطع می‌کند تا اینترنت و پردازنده مصرف نشود؛ بعد خودکار ادامه می‌دهد.", "“Pause” keeps the last frame on screen and stops the stream to save data and CPU, then resumes automatically."),
        ["set_on_fullscreen"] = ("وقتی بازی یا برنامه‌ای تمام‌صفحه است", "When a game or app is fullscreen"),
        ["set_on_maximized"] = ("وقتی پنجره‌ای کل دسکتاپ را پوشانده (Maximize)", "When a window is maximized over the desktop"),
        ["set_on_focus"] = ("وقتی در هر برنامه‌ی دیگری کار می‌کنی", "Whenever another app is in use"),
        ["set_on_battery"] = ("وقتی لپ‌تاپ روی باتری است", "When the laptop is on battery"),
        ["set_on_locked"] = ("وقتی ویندوز قفل است", "When Windows is locked"),
        ["act_none"] = ("ادامه‌ی پخش", "Keep playing"),
        ["act_mute"] = ("فقط قطع صدا", "Mute only"),
        ["act_pause"] = ("مکث (توصیه‌شده)", "Pause (recommended)"),
        ["set_channels"] = ("کانال‌ها", "Channels"),
        ["set_hide_broken"] = ("پنهان کردن کانال‌های خراب", "Hide broken channels"),
        ["set_hide_broken_hint"] = ("کانال‌هایی که در تست یا پخش جواب ندادند تا چند روز نمایش داده نمی‌شوند.", "Channels that failed a test or playback are hidden for a few days."),
        ["set_hide_geo"] = ("پنهان کردن کانال‌های دارای محدودیت منطقه‌ای", "Hide geo-blocked channels"),
        ["set_hide_geo_hint"] = ("این کانال‌ها معمولاً فقط داخل کشور خودشان پخش می‌شوند.", "These usually only play inside their own country."),
        ["set_hide_adult"] = ("پنهان کردن محتوای بزرگسالان", "Hide adult content"),
        ["set_refresh"] = ("به‌روزرسانی لیست‌ها", "Refresh lists every"),
        ["set_refresh_hint"] = ("لیست‌ها ذخیره می‌شوند تا برنامه سریع باز شود.", "Lists are cached so the app opens instantly."),
        ["set_advanced"] = ("شبکه و عملکرد", "Network & performance"),
        ["set_proxy"] = ("پروکسی", "Proxy"),
        ["set_proxy_hint"] = ("برای پخش و دانلود لیست‌ها. «پروکسی ویندوز» با بیشتر فیلترشکن‌ها خودکار کار می‌کند.", "Used for playback and list downloads. “Windows proxy” works automatically with most VPN apps."),
        ["set_hw"] = ("رمزگشایی سخت‌افزاری", "Hardware decoding"),
        ["set_hw_hint"] = ("مصرف پردازنده را خیلی کم می‌کند. اگر تصویر سبز/خراب بود خاموشش کن.", "Greatly reduces CPU use. Turn off if the picture looks green or corrupted."),
        ["set_vout"] = ("خروجی تصویر", "Video output"),
        ["set_vout_hint"] = ("«سازگار» روی همه‌ی نسخه‌های ویندوز پشت آیکون‌ها کار می‌کند. Direct3D سریع‌تر است؛ اگر تصویر سیاه ماند برگرد روی «سازگار».", "“Compatible” works behind the icons on every Windows build. Direct3D is faster; switch back if the video stays black."),
        ["set_ytdlp"] = ("yt-dlp (یوتیوب، توییچ و…)", "yt-dlp (YouTube, Twitch…)"),
        ["set_ytdlp_hint"] = ("هر چند روز خودکار به‌روز می‌شود", "Updated automatically every few days"),
        ["btn_update"] = ("به‌روزرسانی", "Update"),
        ["ytdlp_missing"] = ("نصب نیست", "not installed"),
        ["ytdlp_updating"] = ("در حال به‌روزرسانی…", "updating…"),
        ["set_trouble"] = ("رفع مشکل", "Troubleshooting"),
        ["set_trouble_hint"] = ("اگر ویدیو روی دسکتاپ دیده نمی‌شود، «اتصال دوباره به دسکتاپ» را بزن و اگر نشد «خروجی تصویر» را عوض کن. لاگ‌ها برای گزارش مشکل مفیدند.", "If the video doesn't show on the desktop, use “Re-attach to desktop”, then try another video output. Logs help when reporting issues."),
        ["btn_reattach"] = ("اتصال دوباره به دسکتاپ", "Re-attach to desktop"),
        ["btn_reset_health"] = ("پاک کردن وضعیت سالم/خراب", "Reset working/broken marks"),
        ["btn_clear_cache"] = ("پاک کردن حافظه‌ی موقت", "Clear cache"),
        ["btn_open_logs"] = ("پوشه‌ی لاگ‌ها", "Open logs folder"),
        ["btn_open_settings"] = ("پوشه‌ی تنظیمات", "Open settings folder"),
        ["opt_monitor_primary"] = ("مانیتور اصلی", "Primary monitor"),
        ["opt_monitor_all"] = ("همه‌ی مانیتورها (یک تصویر کشیده)", "All monitors (one stretched picture)"),
        ["opt_monitor_n"] = ("مانیتور {0}", "Monitor {0}"),
        ["opt_fit_fill"] = ("پر کردن صفحه", "Fill screen"),
        ["opt_fit_fit"] = ("کامل با نوار سیاه", "Fit (letterbox)"),
        ["opt_fit_stretch"] = ("کشیدن", "Stretch"),
        ["opt_quality_auto"] = ("خودکار (تا 1080p)", "Auto (up to 1080p)"),
        ["opt_quality_480"] = ("480p (کم‌مصرف)", "480p (data saver)"),
        ["opt_vout_gdi"] = ("سازگار (پیشنهادی)", "Compatible (recommended)"),
        ["opt_vout_d3d11"] = ("Direct3D 11 (سریع‌تر)", "Direct3D 11 (faster)"),
        ["opt_vout_auto"] = ("انتخاب خودکار VLC", "Let VLC decide"),
        ["opt_proxy_system"] = ("پروکسی ویندوز", "Windows proxy"),
        ["opt_proxy_none"] = ("بدون پروکسی", "No proxy"),
        ["opt_proxy_custom"] = ("دستی…", "Custom…"),
        ["opt_hours"] = ("هر {0} ساعت", "{0} hours"),
        ["opt_day1"] = ("هر روز", "Every day"),
        ["opt_days"] = ("هر {0} روز", "{0} days"),
        ["opt_week1"] = ("هر هفته", "Every week"),

        // ---------- onboarding
        ["ob_title"] = ("به TvDesk خوش آمدی", "Welcome to TvDesk"),
        ["ob_subtitle"] = ("تلویزیون زنده، IPTV و پخش زنده‌ی یوتیوب را پس‌زمینه‌ی دسکتاپت کن — پشت آیکون‌ها، بی‌دردسر.", "Turn live TV, IPTV and YouTube live streams into your desktop background — right behind your icons."),
        ["ob_sources"] = ("از کجا کانال بیاوریم؟", "Where should channels come from?"),
        ["ob_sources_hint"] = ("همه رایگان و عمومی‌اند. بعداً از «منابع کانال» قابل تغییرند.", "All free and public. You can change this later under Channel sources."),
        ["ob_start"] = ("شروع کن", "Get started"),
        ["ob_later"] = ("لیست‌ها یک‌بار دانلود و ذخیره می‌شوند؛ ممکن است چند ثانیه طول بکشد.", "Lists are downloaded once and cached — this can take a few seconds."),

        // ---------- add dialog
        ["add_title"] = ("افزودن", "Add"),
        ["add_tab_link"] = ("لینک", "Link"),
        ["add_tab_playlist"] = ("لیست IPTV", "IPTV playlist"),
        ["add_tab_file"] = ("فایل ویدیو", "Video file"),
        ["add_link_label"] = ("لینک کانال یا ویدیو", "Channel or video link"),
        ["add_link_hint"] = ("یوتیوب (لایو یا ویدیو)، توییچ، آپارات، m3u8، mp4، rtmp و…", "YouTube (live or video), Twitch, Aparat, m3u8, mp4, rtmp…"),
        ["add_looks_playlist"] = ("این لینک شبیه لیست IPTV است.", "This looks like an IPTV playlist."),
        ["add_switch_playlist"] = ("افزودن به‌عنوان لیست", "Add as playlist"),
        ["add_name_label"] = ("نام (اختیاری)", "Name (optional)"),
        ["add_name_placeholder"] = ("مثلاً: لوفای شبانه", "e.g. Night lofi"),
        ["add_category_label"] = ("دسته", "Category"),
        ["add_play_once"] = ("فقط پخش", "Just play"),
        ["add_save_play"] = ("ذخیره و پخش", "Save & play"),
        ["add_playlist_label"] = ("لینک یا فایل لیست (m3u / m3u8)", "Playlist link or file (m3u / m3u8)"),
        ["add_playlist_hint"] = ("کانال‌های لیست با دسته‌بندی، کشور و لوگو به کتابخانه اضافه می‌شوند.", "Its channels are added to the library with categories, countries and logos."),
        ["add_playlist_name_placeholder"] = ("مثلاً: IPTV من", "e.g. My IPTV"),
        ["add_file_label"] = ("فایل ویدیو روی کامپیوتر", "Video file on this PC"),
        ["add_file_hint"] = ("فایل‌ها بی‌نهایت تکرار می‌شوند — مناسب والپیپرهای متحرک.", "Files loop forever — great for animated wallpapers."),
        ["btn_browse"] = ("انتخاب…", "Browse…"),
        ["add_err_link"] = ("لینک معتبر نیست.", "That doesn't look like a valid link."),
        ["add_err_playlist"] = ("لینک http(s) یا یک فایل m3u موجود وارد کن.", "Enter an http(s) link or an existing m3u file."),
        ["add_err_file"] = ("فایل پیدا نشد.", "File not found."),
    };
}

/// <summary>XAML markup extension: <c>{l:T key}</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) { Key = key; }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding("[" + Key + "]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
