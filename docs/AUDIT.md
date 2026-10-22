# Release review — 2.1.0

This is a source, dependency and regression review, not a guarantee of zero vulnerabilities or an independent security certification.

## Findings addressed

| Finding | Change |
|---|---|
| Full provider URLs/proxy credentials could enter diagnostics | Redact URL/path diagnostics, omit executable path and raw helper stderr |
| Settings and downloaded playlists retained private URLs in plaintext | Windows current-user protection on new writes; legacy settings read/migrate |
| Untrusted playlist addresses could select local files or arbitrary input modules | Shared URI/header policy at parser, library, UI and resolver boundaries |
| Helper executable downloaded from mutable `latest`, then self-updated | Version/hash pin checked on installation/use; no user configuration/plugins or background self-update |
| Download-size checks occurred after buffering entire responses | Streaming limits on decompressed playlist/logo/helper content |
| Invalid custom proxy could silently use another connection | Invalid custom-proxy downloads fail closed; visible validation feedback |
| Thread-pool scheduling could reorder VLC operations | FIFO queue, obsolete-request checks and duplicate-failure suppression |
| Switching channels while auto-paused could leave old playback running | Cancel resolver/retry and enqueue old-stream stop before new selection |
| Cache clearing left failed logo memory entries | Clear memory/failure caches; deduplicate inflight logo tasks |
| Temporary paused frame could persist after use | Remove after loading and when a pending pause is superseded |
| Defaults/invalid source IDs could break library reload | Normalize enums, source IDs and invalid saved URLs |
| Old dependencies and unrepeatable CI inputs | .NET 10, updated VLC packages, lockfiles, pinned actions and dependency checks |
| Missing public project documentation and licensing | Bilingual README, real-control previews, policies, contributor guide and third-party notices |

## Scope and limits

The original fetched repository had one branch (`main`) and 23 commits, without tags. A full-history Gitleaks scan found no recognized secret. Author metadata contained a personal email; it is normalized to the maintainer's GitHub noreply address while preserving source history. Git hosting caches, forks and existing clones can retain old objects after any rewrite.

NuGet's advisory check for the updated managed/native packages reported no known vulnerable package at the time of review. This is limited to the advisory sources and package metadata; it does not audit every native codec or yt-dlp's bundled components.

Automated checks cover the cases in [BUILDING.md](BUILDING.md). Native smoke tests use real VLC with a synthetic local WAV. UI images render actual WPF controls in two languages at normal/minimum sizes. The synthetic preview catalog has no real subscriptions, tokens or user data.

Unverified hardware scenarios remain part of the release checklist: Windows 10/11 Explorer attachment across builds, monitor/DPI changes, GPU outputs, long-duration streaming, provider restrictions and lock/battery behavior on physical devices. Public stream uptime is not under project control.
