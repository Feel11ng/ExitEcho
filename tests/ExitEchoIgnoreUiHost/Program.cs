using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ExitEcho.App;
using ExitEcho.Core;
using Forms = System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (Environment.GetEnvironmentVariable("EXITECHO_VERIFY_THEME_LOAD") is { } expectedTheme)
        {
            var freshApp = new TestApp();
            var freshSettings = typeof(App).GetProperty("Settings", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(freshApp)!;
            var actualTheme = (string)freshSettings.GetType().GetProperty("Theme")!.GetValue(freshSettings)!;
            if (actualTheme != expectedTheme)
                throw new Exception($"Restart loaded {actualTheme} instead of {expectedTheme}");
            Console.WriteLine("PASS: fresh process restored " + actualTheme);
            return;
        }
        var ignoredPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExitEcho", "ignored.json");
        var previous = File.Exists(ignoredPath) ? File.ReadAllBytes(ignoredPath) : null;
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExitEcho", "settings.json");
        var previousSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var historyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExitEcho", "history.json");
        var previousHistory = File.Exists(historyPath) ? File.ReadAllBytes(historyPath) : null;
        using var ignoredChild = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep -Seconds 90")
        { UseShellExecute = false, CreateNoWindow = true })!;
        using var visibleChild = Process.Start(new ProcessStartInfo("cmd.exe", "/c choice /t 90 /d y >nul")
        { UseShellExecute = false, CreateNoWindow = true })!;
        var app = new TestApp();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/ExitEcho;component/Resources/Design.xaml") });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/ExitEcho;component/Resources/Icons.xaml") });
        typeof(App).Assembly.GetType("ExitEcho.App.Localization.Loc")!
            .GetMethod("SetLanguage", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, ["en"]);
        var setTheme = typeof(App).GetMethod("SetTheme", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var @event = new LeftoverEvent("Ignore UI probe", new[]
        {
            ProcessItem(ignoredChild), ProcessItem(visibleChild)
        }, @"C:\IgnoreUiProbe\probe.exe");
        var details = new DetailsWindow(@event);
        var notification = new NotificationWindow(app, @event);
        ((List<NotificationWindow>)typeof(App).GetField("_notifications", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app)!).Add(notification);
        var main = new MainWindow(app);
        var captureButtons = Environment.GetEnvironmentVariable("EXITECHO_CAPTURE_BUTTONS") == "1";
        var captureLightButtons = Environment.GetEnvironmentVariable("EXITECHO_CAPTURE_LIGHT_BUTTONS") == "1";
        setTheme.Invoke(app, [captureLightButtons ? "light" : "dark"]);
        main.Show();
        notification.Show();
        details.Show();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        _ = RunChecksAsync();
        Dispatcher.Run();

        async Task RunChecksAsync()
        {
            try
            {
                await Task.Delay(550);
                if (Environment.GetEnvironmentVariable("EXITECHO_EXPECT_REDUCED_MOTION") == "1")
                    Check(!SystemParameters.ClientAreaAnimation, "Windows reduced motion setting is observed");
                if (!SystemParameters.ClientAreaAnimation)
                {
                    Check(((Grid)details.FindName("DetailsBody")!).RenderTransform is not ScaleTransform,
                        "reduced motion Details has no scale transform");
                    Check(Math.Abs(((TranslateTransform)((TransformGroup)((Border)notification.FindName("ToastCard")!).RenderTransform).Children[1]).Y) < 0.01,
                        "reduced motion notification has no offset");
                }
                var swapOutput = Path.Combine("artifacts", "ui");
                Directory.CreateDirectory(swapOutput);
                if (Environment.GetEnvironmentVariable("EXITECHO_CAPTURE_THEMES") == "1")
                {
                    typeof(App).GetMethod("OpenSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
                    typeof(App).GetMethod("OpenHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
                    var settings = app.Windows.OfType<SettingsWindow>().Single();
                    var themeHistory = app.Windows.OfType<HistoryWindow>().Single();
                    var confirmation = new ConfirmationWindow("Theme probe", 2);
                    var completion = new CompletionWindow(2, 2);
                    confirmation.Show();
                    completion.Show();
                    var themeCombo = (ComboBox)settings.FindName("ThemeCombo")!;
                    Check(themeCombo.Items.Count == 6 &&
                          new[] { "system", "light", "dark", "oled", "graphite", "midnight" }
                              .SequenceEqual(themeCombo.Items.Cast<ComboBoxItem>().Select(item => (string)item.Tag)),
                        "theme selector has six options in the requested order");
                    using var systemThemeKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var systemLight = systemThemeKey?.GetValue("AppsUseLightTheme") is not int light || light != 0;
                    var themeIds = new[] { "system", "light", "dark", "oled", "graphite", "midnight" };
                    var mainColors = new[] { systemLight ? "#ECF2F3" : "#1C2B33", "#ECF2F3", "#1C2B33",
                        "#000000", "#191B1E", "#0C1424" };
                    var paletteType = typeof(App).Assembly.GetType("ExitEcho.App.ThemePalettes")!;
                    var resolvePalette = paletteType.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic)!;
                    foreach (var (windowsLight, expected) in new[] { (true, "#ECF2F3"), (false, "#1C2B33") })
                    {
                        var resolved = resolvePalette.Invoke(null, ["system", windowsLight])!;
                        Check((string)resolved.GetType().GetProperty("MainBackground")!.GetValue(resolved)! == expected,
                            "System default resolves both Windows light and dark palettes");
                    }
                    for (var index = 0; index < themeIds.Length; index++)
                    {
                        settings.Show();
                        themeHistory.Show();
                        confirmation.Show();
                        completion.Show();
                        notification.Show();
                        themeCombo.SelectedIndex = index;
                        await Task.Delay(260);
                        var theme = themeIds[index];
                        Check((string)((ComboBoxItem)themeCombo.SelectedItem).Tag == theme,
                            theme + " selector value");
                        using var saved = JsonDocument.Parse(File.ReadAllText(settingsPath));
                        Check(saved.RootElement.GetProperty("Theme").GetString() == theme,
                            theme + " persists in settings.json");
                        var loadedSettings = typeof(App).Assembly.GetType("ExitEcho.App.SettingsStore")!
                            .GetMethod("Load", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null)!;
                        Check((string)loadedSettings.GetType().GetProperty("Theme")!.GetValue(loadedSettings)! == theme,
                            theme + " restores through SettingsStore.Load");
                        using (var freshProcess = new Process())
                        {
                            var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
                            var dotnetPath = Path.GetFullPath(Path.Combine(
                                runtimeDirectory, "..", "..", "..", "dotnet.exe"));
                            freshProcess.StartInfo = new ProcessStartInfo(dotnetPath)
                            { UseShellExecute = false, CreateNoWindow = true };
                            freshProcess.StartInfo.ArgumentList.Add(Path.Combine(
                                AppContext.BaseDirectory, "ExitEchoIgnoreUiHost.dll"));
                            freshProcess.StartInfo.Environment["EXITECHO_VERIFY_THEME_LOAD"] = theme;
                            freshProcess.Start();
                            await freshProcess.WaitForExitAsync();
                            Check(freshProcess.ExitCode == 0, theme + " restores after a fresh process launch");
                        }
                        var background = (SolidColorBrush)app.Resources["MainBackgroundBrush"];
                        Check(background.Color == (Color)ColorConverter.ConvertFromString(mainColors[index])!,
                            theme + " exact main background");
                        Color BrushColor(string key) => ((SolidColorBrush)app.Resources[key]).Color;
                        Check(Contrast(BrushColor("TextBrush"), BrushColor("BackgroundBrush")) >= 7 &&
                              Contrast(BrushColor("TextBrush"), BrushColor("SurfaceBrush")) >= 7 &&
                              Contrast(BrushColor("MutedBrush"), BrushColor("BackgroundBrush")) >= 4.5 &&
                              Contrast(BrushColor("ButtonTextBrush"), BrushColor("AccentBrush")) >= 4.5,
                            theme + " text and action contrast");
                        foreach (var window in new Window[] { main, settings, details, notification,
                                     themeHistory, confirmation, completion })
                            Check(window.FindResource("TextBrush") is SolidColorBrush,
                                theme + " resources available in " + window.GetType().Name);
                        foreach (var (name, window) in new (string, Window)[]
                                 { ("main", main), ("settings", settings), ("details", details),
                                   ("notification", notification), ("history", themeHistory),
                                   ("confirmation", confirmation), ("completion", completion) })
                            Capture(window, Path.Combine(swapOutput, $"theme-{theme}-{name}.png"));
                        settings.Hide();
                        themeHistory.Hide();
                        confirmation.Hide();
                        completion.Hide();
                        notification.Hide();
                        var endButton = (Button)details.FindName("EndLeftoversButton")!;
                        var buttonTheme = Environment.GetEnvironmentVariable("EXITECHO_THEME_BUTTON_ID");
                        if (buttonTheme is null ? theme == "oled" : theme == buttonTheme)
                        {
                            await CaptureButtonStatesAsync(main, (Button)main.FindName("PauseButton")!,
                                Path.Combine(swapOutput, $"theme-{theme}-pause"));
                            await CaptureButtonStatesAsync(details, endButton,
                                Path.Combine(swapOutput, $"theme-{theme}-end"));
                        }
                        endButton.IsEnabled = false;
                        Capture(details, Path.Combine(swapOutput, $"theme-{theme}-details-disabled.png"));
                        endButton.IsEnabled = true;
                    }
                    var changeLanguage = typeof(App).Assembly.GetType("ExitEcho.App.Localization.Loc")!
                        .GetMethod("SetLanguage", BindingFlags.Static | BindingFlags.Public)!;
                    var localizationCache = (IDictionary)typeof(App).Assembly
                        .GetType("ExitEcho.App.Localization.Loc")!
                        .GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    localizationCache["ru"] = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(Path.Combine(FindRepoRoot(), "ExitEcho.App", "Localization", "strings.ru.json")))!;
                    changeLanguage.Invoke(null, ["ru"]);
                    Check((string)((ComboBoxItem)themeCombo.Items[3]).Content == "Чёрная (OLED)" &&
                          (string)((ComboBoxItem)themeCombo.Items[4]).Content == "Графитовая" &&
                          (string)((ComboBoxItem)themeCombo.Items[5]).Content == "Полночь",
                        "Russian theme labels update without restart");
                    changeLanguage.Invoke(null, ["en"]);
                    Check((string)((ComboBoxItem)themeCombo.Items[3]).Content == "OLED Black" &&
                          (string)((ComboBoxItem)themeCombo.Items[4]).Content == "Graphite" &&
                          (string)((ComboBoxItem)themeCombo.Items[5]).Content == "Midnight",
                        "English theme labels update without restart");
                    completion.Close();
                    confirmation.Close();
                    themeHistory.Close();
                    settings.Close();
                    Console.WriteLine("PASS: six themes, Windows system mapping, selector, persistence, exact main colors and seven WPF windows");
                    return;
                }
                var pauseButton = (Button)main.FindName("PauseButton")!;
                if (captureButtons)
                {
                    await CaptureButtonStatesAsync(main, pauseButton, Path.Combine(swapOutput,
                        captureLightButtons ? "skeuo-pause-light" : "skeuo-pause-dark"));
                    await CaptureButtonStatesAsync(details, (Button)details.FindName("EndLeftoversButton")!,
                        Path.Combine(swapOutput, captureLightButtons ? "skeuo-end-light" : "skeuo-end-dark"));
                    Console.WriteLine("PASS: skeuomorphic button states captured in " +
                                      (captureLightButtons ? "light" : "dark") + " theme");
                    return;
                }
                var pauseIcon = (System.Windows.Shapes.Path)main.FindName("PauseIcon")!;
                var pauseIncoming = (System.Windows.Shapes.Path)main.FindName("PauseIncomingIcon")!;
                var pauseContent = (StackPanel)main.FindName("PauseButtonContent")!;
                var resumeContent = (StackPanel)main.FindName("ResumeButtonContent")!;
                var updateMainStatus = typeof(MainWindow).GetMethod("UpdateStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var buttonWidth = pauseButton.ActualWidth;
                var maximizeX = Bounds((Button)main.FindName("MainMaximizeButton")!, main).X;
                var heading = (Grid)VisualTreeHelper.GetParent((TextBlock)main.FindName("MonitoringText")!);
                var subtitle = (Grid)VisualTreeHelper.GetParent((TextBlock)main.FindName("MonitoringSubtitle")!);
                var headingBounds = Bounds(heading, main);
                var subtitleBounds = Bounds(subtitle, main);
                var countY = Bounds((TextBlock)main.FindName("EventCountText")!, main).Y;
                Capture(main, Path.Combine(swapOutput, "icon-swap-monitoring.png"));
                Click(pauseButton);
                updateMainStatus.Invoke(main, null);
                if (!SystemParameters.ClientAreaAnimation)
                    Check(resumeContent.Opacity == 1 && pauseContent.Opacity == 0,
                        "reduced motion swaps Pause immediately");
                if (SystemParameters.ClientAreaAnimation)
                {
                    await Task.Delay(100);
                    Capture(main, Path.Combine(swapOutput, "icon-swap-pause-mid.png"));
                }
                await Task.Delay(150);
                Check(ReferenceEquals(pauseIncoming.Data, main.FindResource("IconResume")) &&
                      resumeContent.Opacity == 1 && pauseContent.Opacity == 0 &&
                      Math.Abs(pauseButton.ActualWidth - buttonWidth) < 0.1 &&
                      Math.Abs(Bounds((Button)main.FindName("MainMaximizeButton")!, main).X - maximizeX) < 0.1 &&
                      Bounds(heading, main) == headingBounds && Bounds(subtitle, main) == subtitleBounds &&
                      Math.Abs(Bounds((TextBlock)main.FindName("EventCountText")!, main).Y - countY) < 0.1,
                    "Pause transition settles without button, header, title, subtitle or count shift");
                Capture(main, Path.Combine(swapOutput, "icon-swap-paused.png"));
                Click(pauseButton);
                updateMainStatus.Invoke(main, null);
                await Task.Delay(240);
                Check(ReferenceEquals(pauseIcon.Data, main.FindResource("IconPause")) &&
                      pauseContent.Opacity == 1 && resumeContent.Opacity == 0 &&
                      Math.Abs(pauseButton.ActualWidth - buttonWidth) < 0.1,
                    "Resume icon settles on Pause without button shift");
                Capture(main, Path.Combine(swapOutput, "icon-swap-resumed.png"));
                for (var repeat = 0; repeat < 4; repeat++)
                {
                    Click(pauseButton);
                    updateMainStatus.Invoke(main, null);
                    await Task.Delay(55);
                }
                await Task.Delay(240);
                Check(pauseContent.Opacity == 1 && resumeContent.Opacity == 0 &&
                      Math.Abs(pauseButton.ActualWidth - buttonWidth) < 0.1,
                    "rapid repeated clicks settle without overlap or button shift");

                var mainMax = (Button)main.FindName("MainMaximizeButton")!;
                var mainMaxIcon = (System.Windows.Shapes.Path)main.FindName("MainMaximizeIcon")!;
                var mainMaxIncoming = (System.Windows.Shapes.Path)main.FindName("MainMaximizeIncomingIcon")!;
                Click(mainMax);
                await Task.Delay(190);
                Check(ReferenceEquals(mainMaxIcon.Data, main.FindResource("IconRestore")) &&
                      mainMaxIcon.Opacity == 1 && mainMaxIncoming.Opacity == 0,
                    "Main maximize icon settles on Restore");
                Click(mainMax);
                await Task.Delay(190);
                Check(ReferenceEquals(mainMaxIcon.Data, main.FindResource("IconMaximize")) &&
                      mainMaxIcon.Opacity == 1 && mainMaxIncoming.Opacity == 0,
                    "Main restore icon settles on Maximize");
                var chrome = FindChildren<ChromeTitleBar>(details).Single();
                var chromeMax = (Button)chrome.FindName("MaximizeButton")!;
                var chromeIcon = (System.Windows.Shapes.Path)chrome.FindName("MaximizeIcon")!;
                var chromeIncoming = (System.Windows.Shapes.Path)chrome.FindName("MaximizeIncomingIcon")!;
                Click(chromeMax);
                await Task.Delay(190);
                Check(ReferenceEquals(chromeIcon.Data, chrome.FindResource("IconRestore")) &&
                      chromeIcon.Opacity == 1 && chromeIncoming.Opacity == 0,
                    "Chrome maximize icon settles on Restore");
                Click(chromeMax);
                await Task.Delay(190);
                Check(ReferenceEquals(chromeIcon.Data, chrome.FindResource("IconMaximize")) &&
                      chromeIcon.Opacity == 1 && chromeIncoming.Opacity == 0,
                    "Chrome restore icon settles on Maximize");
                var list = (ItemsControl)details.FindName("ProcessesList")!;
                Check(list.Items.Count == 2, "initial process cards");
                Check(((StatusStepper)details.FindName("EventStepper")!).Status == LeftoverStatus.Detected,
                    "Details begins at detected status");
                Directory.CreateDirectory(Path.Combine("artifacts", "ui"));
                Capture(details, Path.Combine("artifacts", "ui", "stepper-details-dark.png"));
                Capture(details, Path.Combine(swapOutput, "icon-swap-still-running.png"));
                Click(FindChildren<Button>(details).First(button => button.Tag is LeftoverProcess p && p.Pid == ignoredChild.Id));
                await Task.Delay(500);
                Check(list.Items.Count == 1, "Details updates immediately after Ignore process");
                Check(((TextBlock)notification.FindName("CountText")!).Text == "1", "notification count updates immediately");
                Check(((Button)details.FindName("EndLeftoversButton")!).IsEnabled, "End leftovers remains available");
                var rowIgnore = FindChildren<Button>(details).Single(button => button.Tag is LeftoverProcess);
                var pidLabel = FindChildren<TextBlock>(details).Single(label => label.Text.StartsWith("PID "));
                var ramLabel = FindChildren<TextBlock>(details).Single(label => label.Text.EndsWith(" MB RAM"));
                Check(!Bounds(rowIgnore, details).IntersectsWith(Bounds(pidLabel, details)) &&
                      !Bounds(rowIgnore, details).IntersectsWith(Bounds(ramLabel, details)),
                    "Ignore action does not overlap PID or RAM");
                var rules = IgnoreStore.Load(ignoredPath);
                Check(rules.Any(rule => rule.IsProcess && rule.ProcessName == ignoredChild.ProcessName),
                    "process rule saved");
                Check(new IgnoredRule("Fallback app", null, ignoredChild.ProcessName).ToString()
                    .Contains("name only", StringComparison.Ordinal), "name-only scope is visible in rule label");
                typeof(App).GetMethod("Ignore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, ["Another app", null]);
                typeof(MainWindow).GetMethod("ShowIgnoredApps", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(main, null);
                await Task.Delay(300);
                var ignoredContent = (FrameworkElement)main.FindName("IgnoredContent")!;
                var layoutPauseButton = (Button)main.FindName("PauseButton")!;
                Check(main.Height > 470 &&
                      Bounds(ignoredContent, main).Bottom + 8 <= Bounds(layoutPauseButton, main).Top &&
                      Bounds(layoutPauseButton, main).Bottom <= main.ActualHeight,
                    "ignored section expands without overlapping Pause");
                Click((Button)main.FindName("IgnoredToggle")!);
                await Task.Delay(260);
                Check(Math.Abs(main.Height - 470) < 1, "ignored section collapses with window layout");
                Click((Button)main.FindName("IgnoredToggle")!);
                await Task.Delay(260);
                Click(FindChildren<Button>(main).First(button => AutomationProperties.GetName(button) == "Recent leftovers"));
                var history = app.Windows.OfType<HistoryWindow>().Single();
                Check(history.IsVisible, "History section opens on click");
                history.Close();
                var ignoredList = (ListBox)main.FindName("IgnoredList")!;
                Check(ignoredList.Items.Count == 2 &&
                      ignoredList.Items.Cast<object>().Any(item => item.ToString() == "App: Another app") &&
                      ignoredList.Items.Cast<object>().Any(item => item.ToString() ==
                          $"Process: {ignoredChild.ProcessName} · Ignore UI probe"),
                    "both rule types displayed with clear labels");
                var output = Path.Combine("artifacts", "ui");
                Directory.CreateDirectory(output);
                Capture(main, Path.Combine(output, "process-ignore-rules-dark.png"));
                Capture(details, Path.Combine(output, "process-ignore-details-dark.png"));
                Capture(details, Path.Combine(output, "process-ignore-details-dpi-100.png"));
                Capture(details, Path.Combine(output, "process-ignore-details-dpi-125.png"), 1.25);
                Capture(details, Path.Combine(output, "process-ignore-details-dpi-150.png"), 1.5);
                Capture(notification, Path.Combine(output, "process-ignore-notification-filtered.png"));

                ignoredList.SelectedItem = ignoredList.Items.Cast<object>()
                    .First(item => item.ToString() == "App: Another app");
                Click((Button)main.FindName("RemoveButton")!);
                await Task.Delay(240);
                Check(IgnoreStore.Load(ignoredPath).Count == 1, "app rule removed from UI and storage");

                setTheme.Invoke(app, ["light"]);
                await Task.Delay(280);
                Capture(details, Path.Combine(output, "process-ignore-details-light.png"));
                Capture(details, Path.Combine(output, "icon-swap-still-running-light.png"));
                Capture(notification, Path.Combine(output, "process-ignore-notification-light.png"));
                Capture(main, Path.Combine(output, "process-ignore-rules-light.png"));

                QueueDialogResult<ConfirmationWindow>(false);
                Click((Button)details.FindName("EndLeftoversButton")!);
                Check(!visibleChild.HasExited && !ignoredChild.HasExited, "confirmation cancel keeps processes alive");

                QueueDialogResult<ConfirmationWindow>(true);
                QueueDialogResult<CompletionWindow>(true);
                Click((Button)details.FindName("EndLeftoversButton")!);
                visibleChild.WaitForExit(3000);
                Check(visibleChild.HasExited && !ignoredChild.HasExited,
                    "End leftovers terminates only visible process after confirmation");
                Check(((StatusStepper)details.FindName("EventStepper")!).Status == LeftoverStatus.Ended,
                    "Details shows ended status after a real process ends");
                Check(FindChildren<TextBlock>((StatusStepper)details.FindName("EventStepper")!)
                    .Any(label => label.Text == "Ended by ExitEcho"),
                    "Details stepper renders ended label");
                await Task.Delay(90);
                Capture(details, Path.Combine(output, "stepper-details.png"));
                await Task.Delay(180);
                var endedStepper = (StatusStepper)details.FindName("EventStepper")!;
                var completedMark = (System.Windows.Shapes.Path)endedStepper.FindName("CompletedMark")!;
                var pendingDot = (System.Windows.Shapes.Ellipse)endedStepper.FindName("PendingDot")!;
                Check(completedMark.Opacity == 1 && pendingDot.Opacity == 0,
                    "final stepper check replaces the pending circle without overlay");
                Capture(details, Path.Combine(output, "icon-swap-ended.png"));
                notification.Close();
                typeof(App).GetMethod("SetNotifications", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, [true]);
                setTheme.Invoke(app, ["dark"]);
                var showLeftover = typeof(App).GetMethod("ShowLeftover", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var notificationField = typeof(App).GetField("_notifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
                List<NotificationWindow> Notifications() => (List<NotificationWindow>)notificationField.GetValue(app)!;
                var historyProperty = typeof(App).GetProperty("HistoryEntries", BindingFlags.Instance | BindingFlags.NonPublic)!;
                IEnumerable<string> HistoryNames() => ((IEnumerable)historyProperty.GetValue(app)!).Cast<object>()
                    .Select(entry => (string)entry.GetType().GetProperty("AppName")!.GetValue(entry)!);
                var floodName = "ExitEcho flood " + Guid.NewGuid().ToString("N");
                LeftoverEvent Probe(string name, string path, int processCount) => new(name,
                    Enumerable.Range(0, processCount).Select(index => new LeftoverProcess(
                        "probe", 47000 + index, DateTime.UtcNow.Ticks, (index + 1) * 1_000_000)).ToArray(), path);
                for (var index = 1; index <= 10; index++)
                    showLeftover.Invoke(app, [Probe(floodName, @"C:\Probe\flood.exe", index == 10 ? 2 : 1)]);
                Check(Notifications().Count == 1, "10 burst events create one notification");
                var latestEvent = (LeftoverEvent)typeof(NotificationWindow)
                    .GetField("_leftover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Notifications()[0])!;
                Check(latestEvent.Processes.Count == 2 && latestEvent.Processes.Sum(process => process.WorkingSetBytes) == 3_000_000,
                    "burst updates the existing card's process list and RAM");
                Check(((TextBlock)Notifications()[0].FindName("ProcessText")!).Text.Contains("processes"),
                    "existing notification shows the latest process state");
                Check(HistoryNames().Count(name => name == floodName) == 10,
                    "all burst events are retained in History");
                for (var index = 1; index <= 4; index++)
                    showLeftover.Invoke(app, [Probe(floodName + index, $@"C:\Probe\app{index}.exe", 1)]);
                Check(Notifications().Count == 3 && Notifications().All(window => window.IsVisible),
                    "five apps show at most three notifications");
                foreach (var window in Notifications())
                {
                    var area = Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
                    var toDevice = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice;
                    var topLeft = toDevice.Transform(new Point(window.Left, window.Top));
                    var bottomRight = toDevice.Transform(new Point(window.Left + window.Width, window.Top + window.Height));
                    Check(topLeft.Y >= area.Top - 2 && bottomRight.Y <= area.Bottom + 2,
                        "notification stays inside monitor working area");
                }
                Check(HistoryNames().Count(name => name.StartsWith(floodName, StringComparison.Ordinal)) == 14,
                    "queued notifications do not lose History events");
                using (var savedHistory = JsonDocument.Parse(File.ReadAllText(historyPath)))
                    Check(savedHistory.RootElement.EnumerateArray().Count(entry =>
                        entry.GetProperty("AppName").GetString()!.StartsWith(floodName, StringComparison.Ordinal)) == 14,
                        "all detection events are persisted to history.json");
                showLeftover.Invoke(app, [Probe(floodName + 3, @"C:\Probe\app3.exe", 2)]);
                var pending = (System.Collections.ICollection)typeof(App)
                    .GetField("_pendingNotifications", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
                Check(pending.Count == 2, "burst updates a queued app instead of adding another slot");
                await Task.Delay(260);
                var previousTop = Notifications()[1].Top;
                Notifications()[0].Close();
                await Task.Delay(260);
                Check(Notifications().Count == 3 && Notifications()[0].Top > previousTop + 100,
                    "closing one notification fills the slot and smoothly repositions the rest");
                Check(Notifications().Any(window => ((TextBlock)window.FindName("AppNameText")!).Text == floodName + 3 &&
                    ((LeftoverEvent)typeof(NotificationWindow).GetField("_leftover", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(window)!).Processes.Count == 2),
                    "promoted queued notification uses the latest event");
                setTheme.Invoke(app, ["light"]);
                Check(Notifications().All(window => window.IsVisible), "notifications survive light theme switch");
                while (Notifications().Count > 0) Notifications()[0].Close();
                showLeftover.Invoke(app, [Probe(floodName, @"C:\Probe\one.exe", 1)]);
                showLeftover.Invoke(app, [Probe(floodName, @"C:\Probe\two.exe", 1)]);
                Check(Notifications().Count == 2, "same app name with different executable paths remains distinct");
                while (Notifications().Count > 0) Notifications()[0].Close();
                showLeftover.Invoke(app, [Probe(floodName + " fallback", null!, 1)]);
                showLeftover.Invoke(app, [Probe(floodName + " fallback", null!, 1)]);
                Check(Notifications().Count == 1, "missing path falls back to app name");
                while (Notifications().Count > 0) Notifications()[0].Close();
                var ignoredEvent = Probe("Stepper ignored " + Guid.NewGuid().ToString("N"),
                    @"C:\Probe\ignored-stepper.exe", 1);
                showLeftover.Invoke(app, [ignoredEvent]);
                var ignoredSource = (LeftoverEvent)typeof(NotificationWindow)
                    .GetField("_leftover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Notifications().Single())!;
                typeof(App).GetMethod("Ignore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, [ignoredEvent.AppName, ignoredSource]);
                Check(((IEnumerable)historyProperty.GetValue(app)!).Cast<object>().Any(entry =>
                    (string)entry.GetType().GetProperty("AppName")!.GetValue(entry)! == ignoredEvent.AppName &&
                    (bool)entry.GetType().GetProperty("Ignored")!.GetValue(entry)!),
                    "Ignore records the actual event as ignored");
                var allIgnored = Probe("Details ignored probe", @"C:\Probe\details-ignored.exe", 1);
                var ignoredDetails = new DetailsWindow(allIgnored);
                ignoredDetails.Show();
                typeof(DetailsWindow).GetMethod("ApplyIgnoredRules", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(ignoredDetails, [new[] { new IgnoredRule(allIgnored.AppName, allIgnored.ExecutablePath, "probe") }]);
                Check(((StatusStepper)ignoredDetails.FindName("EventStepper")!).Status == LeftoverStatus.Ignored &&
                      !((Button)ignoredDetails.FindName("EndLeftoversButton")!).IsEnabled,
                    "Details shows ignored only when all processes are excluded");
                ignoredDetails.Close();

                var endedEvent = new LeftoverEvent("Stepper ended " + Guid.NewGuid().ToString("N"),
                    [ProcessItem(ignoredChild)], @"C:\Probe\ended-stepper.exe");
                showLeftover.Invoke(app, [endedEvent]);
                var endedSource = (LeftoverEvent)typeof(NotificationWindow)
                    .GetField("_leftover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Notifications().Single())!;
                typeof(App).GetMethod("OpenDetails", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, [endedSource, null]);
                var endDetails = app.Windows.OfType<DetailsWindow>().Single(window => window != details);
                QueueDialogResult<ConfirmationWindow>(true);
                QueueDialogResult<CompletionWindow>(true);
                Click((Button)endDetails.FindName("EndLeftoversButton")!);
                await Task.Delay(400);
                Check(ignoredChild.HasExited && ((StatusStepper)endDetails.FindName("EventStepper")!).Status == LeftoverStatus.Ended,
                    "real End leftovers updates the stepper");
                Check(((IEnumerable)historyProperty.GetValue(app)!).Cast<object>().Any(entry =>
                    (string)entry.GetType().GetProperty("AppName")!.GetValue(entry)! == endedEvent.AppName &&
                    (bool)entry.GetType().GetProperty("EndedViaExitEcho")!.GetValue(entry)!),
                    "End leftovers persists the ended status");

                typeof(App).GetMethod("OpenHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
                var stepperHistory = app.Windows.OfType<HistoryWindow>().Single();
                await Task.Delay(300);
                stepperHistory.Height = 540;
                await Task.Delay(120);
                Capture(stepperHistory, Path.Combine(output, "stepper-history-light.png"));
                setTheme.Invoke(app, ["dark"]);
                await Task.Delay(160);
                Capture(stepperHistory, Path.Combine(output, "stepper-history.png"));
                stepperHistory.Close();

                var legacyId = Guid.NewGuid();
                File.WriteAllText(historyPath, JsonSerializer.Serialize(new[] {
                    new { Id = legacyId, AppName = "Legacy stepper", DetectedAt = DateTimeOffset.Now,
                        ProcessCount = 1, TotalRamBytes = 1_000_000L, EndedViaExitEcho = false }
                }));
                var storeType = typeof(App).Assembly.GetType("ExitEcho.App.HistoryStore")!;
                var legacyStore = Activator.CreateInstance(storeType, nonPublic: true)!;
                object FirstEntry(object store) => ((IEnumerable)storeType.GetProperty("Entries")!.GetValue(store)!).Cast<object>().Single();
                Check(FirstEntry(legacyStore).GetType().GetProperty("ActionStatus")!.GetValue(FirstEntry(legacyStore))!.ToString() == "Detected",
                    "old history.json defaults to detected");
                storeType.GetMethod("MarkIgnored")!.Invoke(legacyStore, [legacyId]);
                var reloaded = Activator.CreateInstance(storeType, nonPublic: true)!;
                Check(FirstEntry(reloaded).GetType().GetProperty("ActionStatus")!.GetValue(FirstEntry(reloaded))!.ToString() == "Ignored",
                    "ignored status survives restart");
                Console.WriteLine("PASS: stepper detected/ignored/ended, legacy History and restart, dark/light, reduced motion, notification cap, queue and visible-only End");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Environment.ExitCode = 1;
            }
            finally
            {
                details.Close();
                notification.Close();
                main.Close();
                if (previous is null) File.Delete(ignoredPath);
                else File.WriteAllBytes(ignoredPath, previous);
                if (previousSettings is null) File.Delete(settingsPath);
                else File.WriteAllBytes(settingsPath, previousSettings);
                if (previousHistory is null) File.Delete(historyPath);
                else File.WriteAllBytes(historyPath, previousHistory);
                if (!ignoredChild.HasExited) ignoredChild.Kill();
                if (!visibleChild.HasExited) visibleChild.Kill();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }
    }

    private static LeftoverProcess ProcessItem(Process process) =>
        new(process.ProcessName, process.Id, process.StartTime.ToUniversalTime().Ticks, process.WorkingSet64);

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ExitEcho.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("ExitEcho repository root was not found.");
    }


    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in FindChildren<T>(child)) yield return nested;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extraInfo);

    private static async Task CaptureButtonStatesAsync(Window window, Button button, string prefix)
    {
        Check(MotionPreferences.GetEnabledForControl(button) == SystemParameters.ClientAreaAnimation,
            "button follows Windows animation setting");
        if (Environment.GetEnvironmentVariable("EXITECHO_FORCE_REDUCED_BUTTONS") == "1")
        {
            MotionPreferences.SetEnabledForControl(button, false);
            prefix += "-reduced";
        }
        window.Topmost = true;
        window.Activate();
        SetForegroundWindow(new WindowInteropHelper(window).Handle);
        window.Topmost = false;
        Keyboard.ClearFocus();
        var outside = window.PointToScreen(new Point(window.ActualWidth - 12, window.ActualHeight - 12));
        SetCursorPos((int)outside.X, (int)outside.Y);
        await Task.Delay(170);
        Capture(window, prefix + "-default.png", 2);
        var center = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
        SetCursorPos((int)center.X, (int)center.Y);
        await Task.Delay(180);
        Check(button.IsMouseOver, "button hover is reached for " + prefix);
        Capture(window, prefix + "-hover.png", 2);
        mouse_event(0x0002, 0, 0, 0, 0);
        await Task.Delay(140);
        Check(button.IsPressed, "button pressed state is reached for " + prefix);
        var outerShadow = (Border)button.Template.FindName("OuterShadow", button)!;
        var insetShade = (Border)button.Template.FindName("InsetShade", button)!;
        var contentOffset = (Border)button.Template.FindName("ContentOffset", button)!;
        Check(outerShadow.Opacity < 0.16 && insetShade.Opacity > 0.25,
            "pressed depth is visible for " + prefix);
        if (MotionPreferences.GetEnabledForControl(button))
            Check(((TranslateTransform)contentOffset.RenderTransform).Y > 1,
                "pressed content moves with Windows animations enabled for " + prefix);
        else
            Check(Math.Abs(((TranslateTransform)contentOffset.RenderTransform).Y) < 0.01,
                "pressed content stays still with reduced motion for " + prefix);
        Capture(window, prefix + "-pressed.png", 2);
        SetCursorPos((int)outside.X, (int)outside.Y);
        mouse_event(0x0004, 0, 0, 0, 0);
        await Task.Delay(160);
        Keyboard.ClearFocus();
    }

    private static void QueueDialogResult<T>(bool result) where T : Window
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<T>().FirstOrDefault(window => window.IsVisible);
            if (dialog is null) return;
            timer.Stop();
            dialog.DialogResult = result;
        };
        timer.Start();
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static void Capture(Window window, string path, double scale = 1)
    {
        var width = (int)Math.Ceiling(window.ActualWidth * scale);
        var height = (int)Math.Ceiling(window.ActualHeight * scale);
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
    }
}

internal sealed class TestApp : App
{
    protected override void OnStartup(StartupEventArgs e) { }
}
