using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ExitEcho.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var repo = FindRepoRoot();
        var app = new LayoutApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/ExitEcho;component/Resources/Design.xaml") });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/ExitEcho;component/Resources/Icons.xaml") });

        var localization = typeof(App).Assembly.GetType("ExitEcho.App.Localization.Loc")!;
        var cache = (IDictionary)localization.GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        cache["ru"] = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(repo, "ExitEcho.App", "Localization", "strings.ru.json")))!;
        var setLanguage = localization.GetMethod("SetLanguage", BindingFlags.Static | BindingFlags.Public)!;
        setLanguage.Invoke(null, ["ru"]);

        var setTheme = typeof(App).GetMethod("SetTheme", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ignored = (IList)typeof(App).GetProperty("IgnoredApps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var history = (IList)typeof(App).GetProperty("HistoryEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        ignored.Clear();
        history.Clear();
        var main = new MainWindow(app);
        setTheme.Invoke(app, ["dark"]);
        main.Show();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        _ = CheckAsync();
        Dispatcher.Run();

        async Task CheckAsync()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("EXITECHO_EXPECT_REDUCED_MOTION") == "1")
                    Assert(!SystemParameters.ClientAreaAnimation, "Windows reduced motion setting was not observed");
                var screenshots = Path.Combine(repo, "artifacts", "ui");
                Directory.CreateDirectory(screenshots);
                var toggle = (Button)main.FindName("IgnoredToggle")!;
                var content = (FrameworkElement)main.FindName("IgnoredContent")!;
                var pause = (FrameworkElement)main.FindName("PauseButton")!;
                var recent = (FrameworkElement)main.FindName("RecentActivityRow")!;
                var fixedRecentTop = Bounds(recent, main).Top;
                foreach (var language in new[] { "ru", "en" })
                {
                    setLanguage.Invoke(null, [language]);
                    foreach (var theme in new[] { "dark", "light" })
                    {
                        setTheme.Invoke(app, [theme]);
                        foreach (var ruleCount in new[] { 0, 1, 5 })
                        {
                            ignored.Clear();
                            for (var index = 0; index < ruleCount; index++)
                                ignored.Add(new IgnoredRule("Application " + index));
                            await Task.Delay(60);
                            CheckGeometry($"{language}/{theme}/{ruleCount}/collapsed", expanded: false);

                            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await Task.Delay(70);
                            CheckGeometry($"{language}/{theme}/{ruleCount}/opening", expanded: true);
                            await Task.Delay(300);
                            CheckGeometry($"{language}/{theme}/{ruleCount}/expanded", expanded: true);
                            main.Height = 470;
                            await Task.Delay(50);
                            CheckGeometry($"{language}/{theme}/{ruleCount}/minimum", expanded: true);

                            if (language == "ru" && ruleCount is 0 or 5)
                                foreach (var scale in new[] { 1d, 1.25, 1.5 })
                                    Capture(main, Path.Combine(screenshots,
                                        $"main-layout-ru-{theme}-{(ruleCount == 0 ? "empty" : "filled")}-{scale * 100:0}.png"), scale);
                            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await Task.Delay(300);
                            CheckGeometry($"{language}/{theme}/{ruleCount}/closed", expanded: false);
                        }
                    }
                }
                var pauseButton = (Button)main.FindName("PauseButton")!;
                var buttonWidth = pauseButton.ActualWidth;
                pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250);
                Assert(IsPaused(), "Pause button did not pause monitoring");
                Assert(Math.Abs(pauseButton.ActualWidth - buttonWidth) < 1, "Pause button width changed");
                pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250);
                Assert(!IsPaused(), "Resume button did not resume monitoring");
                Assert(Math.Abs(pauseButton.ActualWidth - buttonWidth) < 1, "Resume button width changed");

                var historyButton = FindParent<Button>(recent)!;
                historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Assert(app.Windows.Cast<Window>().Any(window => window is HistoryWindow && window.IsVisible),
                    "Recent activity click did not open History");
                Console.WriteLine($"PASS: MainWindow bounds, RU/EN, Dark/Light, 0/1/5 ignored rules, open/closed, minimum size; system DPI {VisualTreeHelper.GetDpi(main).PixelsPerInchY / 96:0.##}x");

                bool IsPaused() => (bool)typeof(App).GetProperty("IsPaused", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;

                void CheckGeometry(string scenario, bool expanded)
                {
                    var viewport = Bounds(content, main);
                    var button = Bounds(pause, main);
                    var window = new Rect(0, 0, main.ActualWidth, main.ActualHeight);
                    Assert(!viewport.IntersectsWith(button), scenario + ": ignored area intersects Pause");
                    Assert(viewport.Bottom + 8 <= button.Top, scenario + ": gap to Pause is below 8 DIP");
                    Assert(window.Contains(button), scenario + ": Pause is outside visible window");
                    Assert(window.Contains(viewport), scenario + ": ignored area is outside visible window");
                    Assert(Math.Abs(Bounds(recent, main).Top - fixedRecentTop) < 1, scenario + ": central content jumped");
                    if (expanded && ignored.Count == 0 && !scenario.EndsWith("/opening", StringComparison.Ordinal))
                    {
                        var emptyText = Bounds((FrameworkElement)main.FindName("EmptyState")!, main);
                        Assert(viewport.Bottom + 0.5 >= emptyText.Bottom, scenario + ": localized empty text is clipped");
                    }
                    if (expanded)
                        Assert(main.ActualHeight >= main.MinHeight - 1, scenario + ": window is below expanded minimum");
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally
            {
                foreach (var window in app.Windows.Cast<Window>().ToArray()) window.Close();
                app.Shutdown();
                Dispatcher.ExitAllFrames();
            }
        }
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found) return found;
        return null;
    }

    private static void Capture(Window window, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ExitEcho.App", "Localization", "strings.ru.json")))
                return directory.FullName;
        throw new DirectoryNotFoundException("ExitEcho repository root was not found.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }
}

internal sealed class LayoutApp : App
{
    protected override void OnStartup(StartupEventArgs e) { }
}
