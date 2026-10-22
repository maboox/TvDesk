# Third-party notices

TvDesk's own source is MIT licensed. Third-party components retain their own licenses. No stream content is licensed or redistributed by TvDesk.

| Component | Version | License / source |
|---|---|---|
| LibVLCSharp | 3.10.2 | LGPL-2.1-or-later; [upstream commit](https://code.videolan.org/videolan/LibVLCSharp/-/tree/3082c09ca955131c705fc4767fde03e9c4a14999) |
| VideoLAN.LibVLC.Windows | 3.0.24 | LGPL-2.1-or-later package; [packaging source](https://code.videolan.org/videolan/libvlc-nuget), [VLC source](https://code.videolan.org/videolan/vlc), [release archives and bundled dependency sources](https://download.videolan.org/pub/videolan/vlc/3.0.24/) |
| yt-dlp Windows executable | 2026.08.19 | [release and source](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19); core Unlicense; bundled PyInstaller release includes other components with their own terms |
| Vazirmatn | bundled font files | SIL Open Font License 1.1; [upstream](https://github.com/rastikerdar/vazirmatn); full license in `src/Resources/Fonts/Vazirmatn-OFL.txt` and release `licenses` |
| Lucide navigation artwork | 1.59.0 | ISC, with inherited Feather artwork under MIT; full notices in `assets/lucide/LICENSE` and release `licenses` |
| .NET runtime | .NET 10, resolved during self-contained publish | MIT and third-party notices in release `licenses`; [runtime source](https://github.com/dotnet/runtime), [Windows desktop source](https://github.com/dotnet/wpf) |

The VLC and LibVLCSharp DLLs remain separate and replaceable. You may modify these libraries and reverse engineer the combined application to debug those modifications as allowed by their licenses. Do not remove the LGPL text or upstream source references when redistributing. Native codecs/plugins and executable helper dependencies can have additional notices; consult the upstream source/distribution license files before redistributing a modified bundle.

Source catalogs reference [iptv-org](https://github.com/iptv-org/iptv), [Free-TV](https://github.com/Free-TV/IPTV), and public provider URLs. Their availability, terms and regional permissions are outside TvDesk's control. Only access media you are authorized to watch.
