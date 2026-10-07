using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TvDesk.Core;
using TvDesk.Sources;

namespace TvDesk.UI;

public partial class AddDialog : Window
{
    public enum Mode { Link, Playlist, File }

    private readonly MainViewModel _vm;

    public AddDialog(MainViewModel vm, Mode mode = Mode.Link)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => WindowEffects.ApplyDark(new WindowInteropHelper(this).Handle);
        switch (mode)
        {
            case Mode.Playlist: TabPlaylist.IsChecked = true; break;
            case Mode.File: TabFile.IsChecked = true; break;
        }
        UpdatePanels();
        Loaded += (_, _) => FocusFirst();
    }

    private void FocusFirst()
    {
        if (TabPlaylist.IsChecked == true) PlaylistUrl.Focus();
        else if (TabFile.IsChecked == true) FilePath.Focus();
        else
        {
            LinkUrl.Focus();
            try
            {
                // Pre-fill from the clipboard when it holds a link.
                if (Clipboard.ContainsText())
                {
                    string t = Clipboard.GetText().Trim();
                    if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && t.Length < 2000 && !t.Contains('\n'))
                    {
                        LinkUrl.Text = t;
                        LinkUrl.SelectAll();
                    }
                }
            }
            catch { }
        }
    }

    private void OnTabChanged(object sender, RoutedEventArgs e) => UpdatePanels();

    private void UpdatePanels()
    {
        // Called while InitializeComponent is still running (IsChecked="True" in XAML) — elements may not exist yet.
        if (LinkPanel == null || PlaylistPanel == null || FilePanel == null) return;
        LinkPanel.Visibility = TabLink.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PlaylistPanel.Visibility = TabPlaylist.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        FilePanel.Visibility = TabFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (ErrorText != null) ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ link

    private void OnLinkChanged(object sender, TextChangedEventArgs e)
    {
        if (PlaylistHint == null) return;
        string url = StreamResolver.Sanitize(LinkUrl.Text);
        PlaylistHint.Visibility = StreamResolver.LooksLikePlaylistUrl(url) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSwitchToPlaylist(object sender, RoutedEventArgs e)
    {
        PlaylistUrl.Text = LinkUrl.Text;
        PlaylistName.Text = LinkName.Text;
        TabPlaylist.IsChecked = true;
    }

    private bool ValidateLink(out string url)
    {
        url = StreamResolver.Sanitize(LinkUrl.Text);
        bool ok = url.Length > 0 &&
                  (Uri.TryCreate(url, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Scheme) || StreamResolver.IsLocalFile(url));
        if (!ok) ShowError(Loc.T("add_err_link"));
        return ok;
    }

    private void OnPlayLink(object sender, RoutedEventArgs e)
    {
        if (!ValidateLink(out var url)) return;
        _vm.Controller.PlayUrl(url, string.IsNullOrWhiteSpace(LinkName.Text) ? null : LinkName.Text.Trim());
        Close();
    }

    private void OnSaveLink(object sender, RoutedEventArgs e)
    {
        if (!ValidateLink(out var url)) return;
        string? category = LinkCategory.SelectedValue as string;
        if (string.IsNullOrEmpty(category)) category = null;
        var c = _vm.Controller.AddLink(LinkName.Text, url, category);
        _vm.Controller.Play(c);
        _vm.Toast(Loc.F("toast_saved", c.Name));
        Close();
    }

    // ------------------------------------------------------------------ playlist

    private void OnBrowsePlaylist(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "M3U playlist (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
        {
            PlaylistUrl.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(PlaylistName.Text))
                PlaylistName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }

    private void OnAddPlaylist(object sender, RoutedEventArgs e)
    {
        string url = StreamResolver.Sanitize(PlaylistUrl.Text);
        bool valid = url.Length > 0 &&
                     ((Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)) ||
                      File.Exists(url));
        if (!valid)
        {
            ShowError(Loc.T("add_err_playlist"));
            return;
        }
        _vm.AddPlaylist(PlaylistName.Text, url);
        Close();
    }

    // ------------------------------------------------------------------ file

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video (*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.ts)|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.ts|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
        {
            FilePath.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(FileName.Text))
                FileName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }

    private bool ValidateFile(out string path)
    {
        path = StreamResolver.Sanitize(FilePath.Text);
        if (path.Length > 0 && File.Exists(path)) return true;
        ShowError(Loc.T("add_err_file"));
        return false;
    }

    private void OnPlayFile(object sender, RoutedEventArgs e)
    {
        if (!ValidateFile(out var path)) return;
        _vm.Controller.PlayUrl(path, string.IsNullOrWhiteSpace(FileName.Text) ? Path.GetFileNameWithoutExtension(path) : FileName.Text.Trim());
        Close();
    }

    private void OnSaveFile(object sender, RoutedEventArgs e)
    {
        if (!ValidateFile(out var path)) return;
        string name = string.IsNullOrWhiteSpace(FileName.Text) ? Path.GetFileNameWithoutExtension(path) : FileName.Text.Trim();
        var c = _vm.Controller.AddLink(name, path, "ambient");
        _vm.Controller.Play(c);
        _vm.Toast(Loc.F("toast_saved", c.Name));
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
