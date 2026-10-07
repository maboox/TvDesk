using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TvDesk.Core;

namespace TvDesk.UI;

public partial class MainWindow : Window
{
    public static bool AllowClose { get; set; }

    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => WindowEffects.ApplyDark(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) => UpdateMaximizeState();
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateMaximizeState();
    }

    private void UpdateMaximizeState()
    {
        bool max = WindowState == WindowState.Maximized;
        // With a custom chrome a maximized window overhangs the screen by the resize border.
        Root.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = max ? Loc.T("tip_restore") : Loc.T("tip_maximize");
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            // Closing the window keeps the wallpaper running in the tray.
            e.Cancel = true;
            Hide();
            _vm.Controller.OnMainWindowHidden();
        }
        base.OnClosing(e);
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDialog(_vm) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

internal static class WindowEffects
{
    /// <summary>Dark title-bar hints, rounded corners and accent-free border on Windows 11 (ignored elsewhere).</summary>
    public static void ApplyDark(IntPtr hwnd)
    {
        try
        {
            int on = 1;
            Desktop.Native.DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));       // DWMWA_USE_IMMERSIVE_DARK_MODE
            int round = 2;
            Desktop.Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));    // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
            int border = 0x00372B25;                                                    // COLORREF (BGR) for #252B37
            Desktop.Native.DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));   // DWMWA_BORDER_COLOR
        }
        catch { }
    }
}
