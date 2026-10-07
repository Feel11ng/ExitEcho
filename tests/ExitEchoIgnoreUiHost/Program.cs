using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
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
        setTheme.Invoke(app, ["dark"]);
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
                var list = (ItemsControl)details.FindName("ProcessesList")!;
                Check(list.Items.Count == 2, "initial process cards");
                Check(((StatusStepper)details.FindName("EventStepper")!).Status == LeftoverStatus.Detected,
                    "Details begins at detected status");
                Directory.CreateDirectory(Path.Combine("artifacts", "ui"));
                Capture(details, Path.Combine("artifacts", "ui", "stepper-details-dark.png"));
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
                Check(Math.Abs(main.Height - 620) < 1, "ignored section expands with window layout");
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
