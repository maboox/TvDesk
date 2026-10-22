# Contributing

Use Windows 10/11 x64 and a .NET 10 SDK. Run `dotnet restore tests/TvDesk.Tests.csproj --locked-mode`, then `dotnet run --project tests/TvDesk.Tests.csproj -c Release`. See [building and testing](docs/BUILDING.md).

Keep changes focused. Describe the user-visible problem, resulting behavior and validation. Add a regression case for behavioral/security fixes. Include English and Persian strings, and check both flow directions for UI changes. Channel data and screenshots in tests must be synthetic or public, with no credentials or personal paths.

Do not commit generated binaries, logs, settings, playlist caches, credentials, private keys or local publishing credentials. Use GitHub's noreply email for commits if you do not want a personal address in public history. Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

Dependency updates must preserve `packages.lock.json`, notices and license files. When updating yt-dlp, review the upstream release, verify its published digest, update `tools.json`, then rebuild and test the bundled executable. Do not switch to an unverified `latest` download.

برای مشارکت، تغییر را کوچک و قابل‌بررسی نگه دارید؛ متن فارسی و انگلیسی و نمایش راست‌به‌چپ را بررسی کنید. دادهٔ شخصی یا پلی‌لیست خصوصی را کامیت نکنید. راهنمای ساخت و تست در فایل بالا موجود است.
