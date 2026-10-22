# Changelog

## 2.1.0

- Yellow/graphite visual system, Lucide vector navigation, improved contrast, keyboard focus, responsive search and a refreshed channel workspace.
- Preserve selected settings when changing languages by updating stable option objects in place.
- English and Persian documentation, security/privacy policies, contributor guides and reproducible WPF previews.
- .NET 10, LibVLCSharp 3.10.2 and VideoLAN native libraries 3.0.24, with dependency locks and NuGet source mapping.
- Ordered VLC commands and stale-request checks; stop old playback when changing channels, including while smart pause is active. Ignore duplicate error callbacks while reconnecting.
- Reject arbitrary protocols, local files and SMB entries from untrusted playlists; validate headers and homepage links. Preserve valid punctuation in signed URLs.
- Bound playlist, logo and helper downloads while streaming. Invalid custom proxies fail closed. Clear in-memory logo failures when clearing caches.
- Protect saved settings and cached playlists with current-user Windows encryption. Redact URLs/paths in logs, hash health keys and remove freeze-frame files after loading.
- Pin and verify yt-dlp; disable user configuration/plugins and background self-updates. Install/repair uses the reviewed helper release.
- Add regression/native playback tests, bilingual render checks, least-privilege CI, pinned actions, checksummed portable releases, issue templates and Dependabot.

## Earlier versions

The original 1.x/2.0 source history is retained. Historical commit metadata may be normalized to the maintainer's GitHub noreply address for privacy.
