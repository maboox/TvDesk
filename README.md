# TvDesk

**Live TV, IPTV and YouTube Live as your Windows desktop wallpaper** — playing right behind your desktop icons.

TvDesk is like Wallpaper Engine, but for live streams: pick a channel from thousands of free public channels (or add your own link / IPTV playlist / video file) and it becomes your background.

> فارسی: TvDesk تلویزیون زنده، IPTV و لایو یوتیوب را پشت آیکون‌های دسکتاپ ویندوز پخش می‌کند. رابط برنامه فارسی و انگلیسی است.

## Features

- **Thousands of free channels, organized** — built-in sources you can switch on/off:
  - *Wallpaper picks* — 24/7 YouTube lives that look great as a background (lofi, aquariums, live cams, world news)
  - *Iran — IRIB* (Telewebion), *iptv-org Iran*, *iptv-org Worldwide* (10,000+), *Free-TV*
- **Real categories** (News, Music, Movies, Sports, Kids, Documentary… with counts), plus **country**, **language** and **source** filters, search (Persian-aware), favorites, recently played and your own links.
- **Add anything**: YouTube (live or video), Twitch, Aparat and other yt-dlp sites, `m3u8`/`mp4`/`rtmp` links, your own **M3U playlists** (URL or file) and **local video files** (looped).
- **Channel health check** — test the visible list; broken channels are hidden automatically (remembered for a few days).
- **Smart pause** — independently choose *keep playing / mute / pause* for: fullscreen apps & games, maximized windows, any other app in focus, battery power, locked PC. Pause keeps the last frame on screen and stops the stream, so no data or CPU is used.
- **Robust playback** — auto-reconnect with backoff, stall detection, expired YouTube links re-resolved, per-channel User-Agent/Referrer from playlists, system/custom proxy passed to VLC and yt-dlp.
- **Works across Windows versions** — Windows 10, Windows 11 (incl. 24H2 "raised desktop"), multi-monitor (primary / any monitor / span all), high-DPI; survives Explorer restarts and wallpaper slideshows.
- Tray icon with favorites, global hotkeys, start with Windows, Persian & English UI.

## Hotkeys

| Keys | Action |
|---|---|
| `Ctrl+Alt+P` | Play / stop |
| `Ctrl+Alt+M` | Mute |
| `Ctrl+Alt+D` | Hide / show desktop icons |
| `Ctrl+Alt+←` / `→` | Previous / next favorite |
| `Ctrl+Alt+T` | Open TvDesk |

## Install

Download **TvDesk-win-x64** from the latest GitHub Actions run (or Releases), extract the whole folder and run `TvDesk.exe`. Keep the folder together — `libvlc` and `yt-dlp.exe` live next to the exe.

## Build

Requirements: Windows, .NET 8 SDK.

```powershell
dotnet publish src/TvDesk.csproj -c Release -r win-x64 --self-contained true -o publish/TvDesk
```

Put `yt-dlp.exe` in a `tools/` folder at the repo root before publishing to bundle it (the CI does this). Without it, TvDesk downloads yt-dlp on first use.

GitHub Actions (`.github/workflows/build.yml`) builds on every push; pushing a tag like `v2.0.0` also creates a Release with a ZIP.

## Project layout

```
src/
  App.xaml(.cs)          startup, single instance
  AppController.cs       playback state machine, reconnect, smart pause, wiring
  Core/                  settings (+ v1 migration), localization (fa/en), HTTP/proxy, logging
  Sources/               catalog, M3U parser, library merge, categories/countries, yt-dlp, health checks, logos
  Playback/              LibVLC engine (all VLC calls off the UI thread)
  Desktop/               behind-the-icons attach (WorkerW / 24H2), wallpaper window, tray, hotkeys, smart-pause watcher
  UI/                    WPF window, views, theme, view-model
  Resources/catalog.json built-in free sources — edit to add/remove lists
```

### Adding a free source

Edit `src/Resources/catalog.json`. A source is either a list of `playlists` (M3U URLs, with `groupMeans` = `category` | `country` | `language` | `auto`) or inline `channels`. To try a catalog without rebuilding, copy it to `%AppData%\TvDesk\catalog.json`.

## Files

- Settings: `%AppData%\TvDesk\settings.json` (settings from TvDesk 1.x are migrated automatically)
- Cache, logos, yt-dlp, logs: `%LocalAppData%\TvDesk\` (Settings → Troubleshooting → *Open logs folder*)

## Troubleshooting

- **Video not visible on the desktop** — Settings → *Re-attach to desktop*. If still black, try another *Video output*. Send `TvDesk.log` with an issue.
- **YouTube stopped working** — Settings → *Update* yt-dlp (it also updates itself every few days).
- **Channels fail** — public channels go offline or are geo-blocked; use *Test channels* and keep *Hide broken channels* on. If you use a VPN with a local proxy, keep Proxy on *Windows proxy* or enter it manually.

## Legal

TvDesk is only a player and playlist manager; it does not host or redistribute any stream. Lists come from public projects ([iptv-org](https://github.com/iptv-org/iptv), [Free-TV](https://github.com/Free-TV/IPTV)). Only watch streams you are allowed to access.

Font: [Vazirmatn](https://github.com/rastikerdar/vazirmatn) (SIL Open Font License). Playback: [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) / VLC. Web links: [yt-dlp](https://github.com/yt-dlp/yt-dlp).
