# Security policy

Security fixes target the latest stable TvDesk release. Update the full portable folder, including native libraries.

Report vulnerabilities using [GitHub private vulnerability reporting](https://github.com/maboox/TvDesk/security/advisories/new) when enabled. If private reporting is unavailable, open a public issue asking for a private contact **without publishing exploit details, credentials, private playlists, or unredacted logs**. Never attach your settings or cache folders.

## Trust boundaries

- Remote M3U/catalog entries accept only HTTP(S), RTMP(S), RTSP(S), UDP and RTP media addresses. Local files must be chosen explicitly; UNC/SMB paths and arbitrary VLC access modules are rejected at the application boundary.
- Playlist headers are restricted to User-Agent and Referrer, with length and control-character checks. Shell execution is not used for playback/helper arguments. Catalog homepage links accept HTTP(S) only.
- Downloads are bounded while reading decompressed content: 16 MB playlists, 4 MB logos, 64 MB helper. Redirects and provider manifests remain external input parsed by VLC/yt-dlp.
- Helper version/hash live in `src/Resources/tools.json`; installation and every use verify SHA-256. Changes to this pin require maintainer review. A matching hash proves consistency with the reviewed executable, not that the executable is vulnerability-free.
- Settings and playlist caches use current-user Windows protection. Logs redact URLs and paths; health-cache keys are hashes. See [PRIVACY.md](PRIVACY.md).
- NuGet lockfiles, package-source mapping, pinned CI actions, dependency advisory checks and Dependabot help maintain reproducible inputs.

This is defense in depth, not an independent penetration-test certification. Use only trusted media and playlists. Native VLC codecs and yt-dlp process untrusted content and need ongoing security updates. Nested provider manifests may reference additional resources and protocols; the application URL allowlist is not a sandbox for those libraries.

## فارسی

گزارش امنیتی را ترجیحاً به‌صورت خصوصی از بخش Security گیت‌هاب ارسال کنید. کلید، رمز، لینک خصوصی IPTV و پوشهٔ تنظیمات را در ایشوی عمومی قرار ندهید. اگر گزارش خصوصی فعال نبود، بدون جزئیات آسیب‌پذیری درخواست راه ارتباط خصوصی کنید. فقط آخرین نسخهٔ پایدار هدف اصلاحات امنیتی است؛ هنگام ارتقا کل پوشه و کتابخانه‌های پخش را جایگزین کنید.
