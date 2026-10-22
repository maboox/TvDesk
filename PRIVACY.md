# Privacy

TvDesk has no account system, analytics, advertising SDK, telemetry endpoint, or hosted backend.

## Stored on your computer

- `%AppData%\TvDesk\settings.json`: preferences, saved links, favorites, recent playback, and custom proxy. Protected using Windows DPAPI for the current account. It is no longer a directly editable JSON file. Older plaintext settings migrate when saved. They cannot be copied to another Windows account as a portable backup.
- `%LocalAppData%\TvDesk\cache`: downloaded playlists protected with the same account protection. Old plaintext caches expire or can be removed using **Clear cache**.
- `logos`: downloaded channel artwork, cached for up to 30 days. `health.json`: SHA-256 identifiers and availability timestamps, without full URLs.
- `tools`: the verified, pinned yt-dlp helper. `logs`: size-limited diagnostic logs. URLs and local paths are redacted before logging. A temporary freeze-frame file is removed after loading it into memory; an abnormal exit can leave one behind until cache cleanup.

DPAPI protects files at rest from other accounts; it does not protect against malware running as you or a compromised administrator. Existing `.broken` backups and logs made by older releases can still contain private information. Review or delete those before sharing a diagnostics folder.

## Network requests

The sources you enable receive requests for playlists. Visible channel logos are downloaded on demand. Playing media or testing a channel contacts its provider. Web video resolution may contact several provider domains through yt-dlp. These services receive your IP address and ordinary HTTP metadata. HTTP streams are not encrypted; prefer HTTPS where available. Custom playlists may point at your local network; add only trusted sources.

The helper is bundled in release packages. A source build without it downloads only the pinned executable from GitHub when a web video needs it or when you select **Install / repair**. Its SHA-256 is checked before installation and execution. TvDesk does not load browser cookies, yt-dlp plugins, or user yt-dlp configuration. No background helper self-updates run.

The default uses Windows proxy settings. Selecting no proxy permits direct connections. An invalid custom proxy blocks downloads rather than silently connecting directly. VLC and yt-dlp have their own networking stacks; a proxy is not a system-wide VPN or an anonymity guarantee, especially for UDP/RTSP and redirects.

## Remove your data

Exit TvDesk from the tray first. Delete the two TvDesk folders above to erase preferences, history, logs, tools, and caches. Disable **Start with Windows** before removing the application, or remove its `TvDesk` entry from the current-user Windows Run key. No cloud deletion request is needed.

## فارسی

TvDesk حساب کاربری، تحلیل رفتار، تبلیغات یا سرور جمع‌آوری اطلاعات ندارد. تنظیمات، لینک‌های ذخیره‌شده، تاریخچه و کش پلی‌لیست با حفاظت حساب ویندوز ذخیره می‌شوند. این حفاظت در برابر بدافزاری که با حساب خودتان اجرا شده کافی نیست.

منابع پلی‌لیست، لوگو و پخش، IP و اطلاعات عادی درخواست را دریافت می‌کنند. تست کانال هم به ارائه‌دهنده وصل می‌شود. لینک‌های HTTP رمزگذاری نشده‌اند. فقط منابع مورداعتماد را اضافه کنید؛ پلی‌لیست می‌تواند آدرس شبکهٔ داخلی داشته باشد. پراکسی تضمین ناشناس‌ماندن نیست.

نسخهٔ yt-dlp مشخص و هش آن بررسی می‌شود؛ پیکربندی، افزونه و کوکی مرورگر بارگذاری نمی‌شود. به‌روزرسانی خودکار پس‌زمینه وجود ندارد. فایل‌های لاگ و پشتیبان نسخه‌های قدیمی ممکن است اطلاعات خصوصی داشته باشند؛ قبل از اشتراک‌گذاری بررسی‌شان کنید. برای پاک‌کردن داده‌ها، برنامه را از کنار ساعت ببندید و پوشه‌های بالا را حذف کنید.
