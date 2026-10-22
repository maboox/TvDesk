# Build, test and package

Use Windows 10/11 x64 and a .NET 10 SDK. `global.json` accepts newer .NET 10 feature bands. NuGet packages resolve only from nuget.org, with committed dependency lockfiles.

```powershell
dotnet restore tests/TvDesk.Tests.csproj --locked-mode
dotnet build tests/TvDesk.Tests.csproj -c Release --no-restore -warnaserror
dotnet run --project tests/TvDesk.Tests.csproj -c Release --no-build
dotnet run --project tests/TvDesk.Tests.csproj -c Release --no-build -- --native
dotnet run --project tests/TvDesk.Tests.csproj -c Release --no-build -- --render artifacts/preview
./scripts/Publish.ps1
```

The dependency-free regression runner exits nonzero on failure. It covers untrusted URI schemes, local/UNC boundaries, signed-URL preservation, M3U headers and quoted commas, URL redaction, current-user encryption, invalid proxies, bounded streaming downloads, settings normalization, library merging/cancellation and FIFO worker order. `--native` additionally tests real VLC playback and rapid play/stop requests with a generated silent WAV. This needs a working Windows audio session and is a local smoke test, not a CI audio requirement.

`--render` renders the actual WPF controls in Persian and English at normal and minimum window sizes using synthetic channels. No desktop capture or private data is used. Review the PNGs and `bindings.txt`. This checks layout; it does not certify Explorer attachment, GPU output or every Windows build.

`scripts/Publish.ps1` downloads the exact helper in `src/Resources/tools.json`, checks SHA-256, publishes the app with its .NET runtime, and includes documentation and licenses. Keep the whole `TvDesk` folder together. The public release workflow creates a ZIP and a SHA-256 file only after build/tests pass. Release binaries are unsigned; a checksum checks consistency, not publisher identity.

## Before a release

1. Confirm both languages, keyboard focus, filtering, favorites, and source toggles in the actual app.
2. Test a local video, an authorized HTTP(S) stream and a web video. Public channels are not guaranteed to stay online or be licensed in every region.
3. Test pause/resume for fullscreen apps, battery and lock/unlock, then stop/reconnect rapidly.
4. Check primary/secondary/span targets, DPI scaling, Explorer restart and exit restoration on Windows 10 and Windows 11, including recent desktop changes. These need real machine coverage.
5. Run dependency advisories and a full-history secret scan. Review changed dependencies and helper pins.
6. Ensure native/managed dependency licenses and corresponding upstream source references remain available. Preserve replaceable DLLs and do not publish trimmed/single-file packages that prevent library replacement.
7. Update version, manifest version, changelog, screenshots and tag. Push a `vX.Y.Z` tag after the main build passes. The release job publishes the tested portable ZIP.

Regenerate vendored Lucide drawings with `./scripts/Generate-Icons.ps1`. It reads the checked-in SVGs offline; license/provenance are in `assets/lucide`.

## Layout

| Directory | Responsibility |
|---|---|
| `src/Core` | settings, privacy, localization, networking, serialized work |
| `src/Sources` | catalog, bounded playlist imports, caching, helper resolution |
| `src/Playback` | VLC lifecycle and generation-aware playback commands |
| `src/Desktop` | Windows shell attachment, tray, hotkeys and activity watcher |
| `src/UI` | WPF views, bindings, reusable theme and vector icons |
| `tests` | behavioral regression tests and real-control render harness |
| `scripts` | verified helper installation, packaging and icon generation |

The controller coordinates UI-thread state and cancellation. Blocking VLC operations run on a FIFO worker queue; generation checks discard obsolete requests. Keep callbacks small and post changes to the dispatcher.
