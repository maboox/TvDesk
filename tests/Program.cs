using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TvDesk;
using TvDesk.Core;
using TvDesk.Playback;
using TvDesk.Sources;
using TvDesk.UI;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        _passed++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CoreTests().GetAwaiter().GetResult();
            if (args.Contains("--native")) NativeTest().GetAwaiter().GetResult();
            if (args.Length >= 2 && args[0] == "--render") Render(args[1]);
            Console.WriteLine($"PASS: {_passed} assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task CoreTests()
    {
        foreach (string bad in new[] { "file:///C:/private.txt", @"C:\private.txt", @"\\server\share\x", "smb://server/x", "javascript:alert(1)", "data:text/plain,hello", "concat:http://a|file:///b", "http://example.test/\nheader" })
            Check(!MediaPolicy.IsRemoteMedia(bad), "Unsafe playlist entry accepted");
        foreach (string good in new[] { "https://example.test/a?token=1;", "http://192.168.1.2/live", "rtmp://example.test/live", "rtsp://example.test/live", "udp://@239.0.0.1:1234" })
            Check(MediaPolicy.IsRemoteMedia(good), "Supported IPTV protocol rejected: " + good);
        Check(MediaPolicy.IsLocalPath(@"C:\Videos\sample.mp4"), "Local video rejected");
        Check(!MediaPolicy.IsLocalPath("file://server/share/movie.mp4"), "UNC file URI accepted");
        Check(StreamResolver.Sanitize("https://example.test/x?token=abc;") == "https://example.test/x?token=abc;", "Signed URL punctuation lost");
        Check(StreamResolver.NeedsYtDlp("https://www.youtube.com/watch?v=1"), "YouTube not resolved");
        Check(!StreamResolver.NeedsYtDlp("https://youtube.com.attacker.test/watch?v=1"), "Host suffix confusion");
        Check(!StreamResolver.NeedsYtDlp("https://example.test/a.m3u8"), "Direct stream sent to helper");

        var entries = M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-name=\"Fallback\" group-title=\"News, World\",News, Live\n#EXTVLCOPT:http-user-agent=VLC\nhttps://example.test/a|Referer=https%3A%2F%2Fexample.test%2F\n#EXTINF:-1,Bad local\nfile:///C:/secret.txt\n#EXTINF:-1,Bad network share\n\\\\server\\file\n");
        Check(entries.Count == 1, "Untrusted playlist can open local paths");
        Check(entries[0].Title == "News, Live" && entries[0].Group == "News, World", "Quoted comma parsing");
        Check(entries[0].UserAgent == "VLC" && entries[0].Referrer == "https://example.test/", "IPTV headers lost");
        Check(M3uParser.Parse(new string('a', 17000) + "\nhttps://example.test/live").Count == 1, "Oversized playlist line was not isolated");
        Check(MediaPolicy.SafeHeader("agent\r\nAuthorization: token") == null, "Header injection");
        Check(M3uParser.Parse("https://example.test/live|User-Agent=VLC%0D%0AX-Test%3Asecret")[0].UserAgent == null, "Encoded header injection");
        Check(TextUtil.SearchKey("كيهان") == TextUtil.SearchKey("کیهان"), "Persian search variants");
        Check(NameCleaner.Clean("Example (1080p) [Geo-blocked]", out var quality, out var geo, out _) == "Example" && quality == "1080p" && geo, "Channel metadata parsing");

        string diagnostic = Privacy.Redact(@"failed https://user:secret@example.test/private/token?key=abc at C:\Users\someone\settings.json");
        Check(!diagnostic.Contains("secret") && !diagnostic.Contains("token") && !diagnostic.Contains("someone"), "Private data in diagnostics");
        string protectedText = PrivateFile.Encode("private-playlist-token");
        Check(!protectedText.Contains("private-playlist-token") && PrivateFile.Decode(protectedText) == "private-playlist-token", "Current-user encryption round trip");
        Check(PrivateFile.Decode("legacy") == "legacy", "Legacy migration");
        Check(!Http.IsValidProxy("bad proxy") && !Http.IsValidProxy("file:///C:/x") && Http.IsValidProxy("127.0.0.1:1080"), "Proxy validation");
        Http.Configure(ProxyMode.Custom, "bad proxy");
        bool blocked = false;
        try { await Http.Client.GetAsync("https://example.test"); } catch (System.Net.Http.HttpRequestException) { blocked = true; }
        Check(blocked, "Invalid proxy fell back to direct networking");
        Http.Configure(ProxyMode.System, "");

        using var oversized = new MemoryStream(new byte[1025]);
        bool limited = false;
        try { await Download.ReadLimitedAsync(oversized, 1024, default); } catch (InvalidDataException) { limited = true; }
        Check(limited, "Chunked download size limit bypass");
        using var exact = new MemoryStream(new byte[1024]);
        Check((await Download.ReadLimitedAsync(exact, 1024, default)).Length == 1024, "Boundary-size download rejected");

        var settings = new AppSettings { Volume = 900, RefreshHours = 9999, OnLocked = (AutoAction)999, Fit = (FitMode)99 };
        settings.MyLinks.Add(new SavedLink { Url = "javascript:alert(1)" });
        settings.CustomSources.Add(new CustomSource { Id = "duplicate", Url = "https://a.test/list.m3u" });
        settings.CustomSources.Add(new CustomSource { Id = "duplicate", Url = "https://b.test/list.m3u" });
        settings.Normalize();
        Check(settings.Volume == 100 && settings.RefreshHours == 720 && settings.OnLocked == AutoAction.Pause && settings.Fit == FitMode.Fill, "Invalid saved settings");
        Check(settings.MyLinks.Count == 0 && settings.CustomSources.Select(s => s.Id).Distinct().Count() == 2, "Invalid links or source ID collision");

        var queue = new SerialQueue();
        var order = new List<int>();
        var tasks = Enumerable.Range(0, 500).Select(i => queue.Enqueue(() => order.Add(i))).ToArray();
        await Task.WhenAll(tasks);
        Check(order.SequenceEqual(Enumerable.Range(0, 500)), "Playback operations reordered by thread scheduling");
        try { await queue.Enqueue(() => throw new InvalidOperationException()); } catch (InvalidOperationException) { }
        await queue.Enqueue(() => order.Add(500));
        Check(order[^1] == 500, "Worker queue poisoned by failed operation");

        var library = new ChannelLibrary();
        var catalog = new Catalog();
        catalog.Sources.Add(new SourceDef { Id = "test", DefaultEnabled = true, Channels = new() {
            new() { Name = "First", Url = "https://example.test/live", Category = "news" },
            new() { Name = "Duplicate", Url = "https://example.test/live", Country = "IR" },
            new() { Name = "Unsafe", Url = "file:///C:/secret.mp4" } } });
        await library.LoadAsync(new AppSettings(), catalog, new HealthStore(), false, default);
        Check(library.Channels.Count == 1 && library.Channels[0].Countries.Contains("IR"), "Library duplicate merge or catalog trust boundary");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool canceled = false;
        try { await library.LoadAsync(new AppSettings(), catalog, new HealthStore(), false, cancelled.Token); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && library.Channels.Count == 1, "Canceled reload replaced the existing library");
    }

    private static async Task NativeTest()
    {
        string path = Path.Combine(Path.GetTempPath(), "TvDesk-test-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var file = new BinaryWriter(File.Create(path)))
            {
                file.Write(Encoding.ASCII.GetBytes("RIFF")); file.Write(160036); file.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                file.Write(16); file.Write((short)1); file.Write((short)1); file.Write(8000); file.Write(16000); file.Write((short)2); file.Write((short)16);
                file.Write(Encoding.ASCII.GetBytes("data")); file.Write(160000); file.Write(new byte[160000]);
            }
            using var engine = new PlaybackEngine(false, "gdi");
            Check(engine.Available, "Native VLC initialization: " + engine.InitError);
            var played = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Event += (_, e, _) => { if (e == EngineEvent.Playing) played.TrySetResult(); };
            await engine.PlayAsync(new PlayRequest { Generation = 1, Url = path, IsLocalFile = true });
            await played.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Check(true, "Native playback started");
            var requests = new List<Task>();
            for (int i = 2; i < 42; i++) requests.Add(i % 2 == 0 ? engine.StopAsync(i) : engine.PlayAsync(new PlayRequest { Generation = i, Url = path, IsLocalFile = true }));
            requests.Add(engine.StopAsync(42));
            await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(20));
            Check(engine.State != LibVLCSharp.Shared.VLCState.Playing, "Stale play restarted after stop");
        }
        finally { File.Delete(path); }
    }

    private static void Render(string output)
    {
        Directory.CreateDirectory(output);
        // Load only view resources. Never run the real app startup or wallpaper controller.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TvDesk;component/UI/Theme.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TvDesk;component/UI/Icons.xaml", UriKind.Relative) });
        app.Resources["UiFont"] = new FontFamily(new Uri("pack://application:,,,/TvDesk;component/"), "./Resources/Fonts/#Vazirmatn, Segoe UI");
        var controller = new AppController();
        controller.Settings.OnboardingComplete = true;
        string[] names = { "Lofi study room", "Earth from above", "Ocean stories", "شبکهٔ مستند", "World news", "Quiet piano", "City lights", "Nature escape" };
        string[] categories = { "music", "documentary", "documentary", "documentary", "news", "music", "travel", "outdoor" };
        for (int i = 0; i < names.Length; i++)
        {
            var channel = controller.Library.AddUserLink(new SavedLink { Name = names[i], Url = "https://example.test/" + i, Category = categories[i] });
            channel.SourceName = "Preview collection";
            channel.IsFavorite = i % 3 == 0; channel.IsAlive = i % 4 == 0 ? null : true;
        }
        var vm = new MainViewModel(controller);
        var probe = new System.Windows.Controls.ComboBox { ItemsSource = vm.FitOptions, SelectedValuePath = "Key", SelectedValue = vm.Fit };
        Check(probe.SelectedItem != null, "Standalone option selection failed");
        var window = new TvDesk.UI.MainWindow(vm);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000; window.Top = -32000;
        window.ShowInTaskbar = false; window.ShowActivated = false;
        MainWindow.AllowClose = true;
        var errors = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(errors));
        window.Show();
        Pump(window);
        foreach (string language in new[] { "en", "fa" })
        {
            controller.Settings.Language = language; Loc.I.SetLanguage(language);
            Pump(window);
            Check(Loc.I.Language == language, "Language bindings reverted the selected language");
            vm.Page = Page.Browse;
            RenderVisual(window, output, "library-" + language, 1320, 820);
            RenderVisual(window, output, "compact-" + language, 1040, 680);
            vm.Page = Page.Settings;
            RenderVisual(window, output, "settings-" + language, 1320, 820);
        }
        vm.Page = Page.Browse;
        window.Close();
        string diagnostic = errors.ToString();
        File.WriteAllText(Path.Combine(output, "bindings.txt"), diagnostic);
        Check(!diagnostic.Contains("Error:"), "WPF data binding error: " + diagnostic);
    }

    private static void RenderVisual(Window window, string output, string name, int width, int height)
    {
        Pump(window);
        var root = (FrameworkElement)window.Content;
        root.Width = width; root.Height = height;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        Pump(window);
        root.UpdateLayout();
        if (name.StartsWith("settings-"))
        {
            File.WriteAllLines(Path.Combine(output, name + ".txt"), Descendants(root).OfType<System.Windows.Controls.ComboBox>()
                .Select(c => $"items={c.Items.Count}; path={c.SelectedValuePath}; selected={c.SelectedValue}; item={c.SelectedItem}; box={c.SelectionBoxItem}; keys={string.Join(',', c.Items.OfType<Option>().Select(o => o.Key + ':' + o.Label))}"));
            foreach (var combo in Descendants(root).OfType<System.Windows.Controls.ComboBox>().Where(c => c.SelectedValuePath == "Key"))
            {
                Check(combo.SelectedItem != null, "Settings dropdown did not select its stored value");
                if (combo.SelectedItem is Option option)
                    Check(Descendants(combo).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == option.Label), "Settings label did not update to selected language");
            }
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        Check(image.PixelWidth == width, "Render failed");
    }

    private static void Pump(Window window)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
