# TvDesk

TvDesk is a Windows desktop app that turns live streams, IPTV channels, local videos, and supported live links into a live desktop wallpaper. It is designed for people who want a Wallpaper Engine–style experience, but focused on TV/live streams and M3U playlists.

> Current status: early preview / experimental. Windows desktop rendering relies on Explorer/Progman/WorkerW behavior, which can vary between Windows versions.

## Features

- Play live video as a desktop wallpaper.
- Play direct streams: `m3u8`, `mp4`, `mkv`, `ts`, `rtmp`, `udp`.
- Resolve supported web/live links through `yt-dlp`, including YouTube Live and some other supported websites.
- Built-in optional playlist sources:
  - Telewebion live channels
  - iptv-org Iran playlist
  - iptv-org category playlist
  - iptv-org language playlist
  - Free-TV/IPTV playlist
- Add custom IPTV/M3U/M3U8 playlist URLs.
- Search and filter channels by:
  - country
  - genre/category
  - source/provider
  - health check status
  - favorites
- Save favorite channels and custom links.
- Quick-test visible channels and mark them as working or failed.
- Toggle desktop icons.
- Configure behavior independently for focus loss and fullscreen apps:
  - mute audio
  - freeze visual frame
- Full stop/restart controls for when you want to stop network usage.
- System tray integration and global hotkeys.

## Screenshots

Screenshots are not included yet. Contributions are welcome.

## Installation

Download the latest artifact from GitHub Actions or Releases.

There are two build styles:

### Recommended: folder build

Artifact name:

```text
TvDesk-win-x64.zip
```

Extract the ZIP completely, then run:

```text
TvDesk.exe
```

Do not move only `TvDesk.exe` out of the extracted folder. The folder build keeps native LibVLC files next to the executable and is the most reliable option.

### Experimental: single EXE build

Artifact name:

```text
TvDesk-single-exe-win-x64.zip
```

This contains one self-contained executable. It is more convenient, but less tested because LibVLC native files are bundled/extracted by .NET at runtime. If video playback fails in the single-EXE build, use the folder build.

## Build locally

Requirements:

- Windows
- .NET 8 SDK

Restore and build:

```powershell
dotnet restore src/TvDesk.csproj
dotnet build src/TvDesk.csproj -c Release
```

Publish the recommended folder build:

```powershell
dotnet publish src/TvDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
```

Publish the experimental single-EXE build:

```powershell
dotnet publish src/TvDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-single
```

## Build with GitHub Actions

The repository includes a workflow at:

```text
.github/workflows/build.yml
```

On every push to `main` or manual workflow run, it builds and uploads:

- `TvDesk-win-x64.zip` — recommended folder build
- `TvDesk-single-exe-win-x64.zip` — experimental one-file EXE build

On version tags like `v1.0.0`, the workflow also creates a GitHub Release and attaches both artifacts.

## First run

On first launch, TvDesk asks for:

- language: Persian or English
- which built-in playlist sources should be enabled

You can disable all sources and start with an empty library, then add your own IPTV/M3U links.

Settings are stored locally in:

```text
%AppData%\TvDesk\settings.json
```

Logs are written next to the executable when possible:

```text
TvDesk.log
```

If the executable folder is not writable, logs may fall back to AppData.

## Privacy and security

This project does not include API keys, tokens, passwords, paid IPTV credentials, or private provider credentials.

The app may download public playlist files from the sources you enable, for example iptv-org or Free-TV/IPTV. Custom playlist URLs you add are stored locally in your own settings file.

Before making your fork public, check that you have not committed:

- `settings.json`
- `TvDesk.log`
- downloaded playlist cache files
- personal playlist URLs
- paid/private IPTV credentials
- local publish output

The included `.gitignore` is intended to keep build outputs, logs, and local settings out of git.

## Legal note

TvDesk is only a player and playlist manager. It does not host or redistribute streams. Use only streams and playlists that you are allowed to access in your region and under applicable law.

Some public channels may be offline, geo-blocked, or unstable.

## Hotkeys

- `Ctrl + Alt + D` — toggle desktop icons
- `Ctrl + Alt + M` — mute/unmute
- `Ctrl + Alt + T` — show TvDesk control window

## Troubleshooting

### Video does not appear on the desktop

Check `TvDesk.log` for desktop attach information. Windows 11 versions can behave differently, especially around Explorer/Progman/WorkerW.

### Single EXE does not play video

Use the recommended folder build. LibVLC native loading is more reliable when native files are kept next to the executable.

### Some channels fail

Public IPTV channels often go offline or are geo-blocked. Use the quick-test feature to mark working/failed channels for the current filter.
