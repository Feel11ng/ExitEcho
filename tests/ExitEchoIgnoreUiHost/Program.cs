using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ExitEcho.App;
using ExitEcho.Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var ignoredPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExitEcho", "ignored.json");
        var previous = File.Exists(ignoredPath) ? File.ReadAllBytes(ignoredPath) : null;
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
                var list = (ItemsControl)details.FindName("ProcessesList")!;
                Check(list.Items.Count == 2, "initial process cards");
                Click(FindChildren<Button>(details).First(button => button.Tag is LeftoverProcess p && p.Pid == ignoredChild.Id));
                await Task.Delay(500);
                Check(list.Items.Count == 1, "Details updates immediately after Ignore process");
                Check(((TextBlock)notification.FindName("CountText")!).Text == "1", "notification count updates immediately");
                Check(((Button)details.FindName("EndLeftoversButton")!).IsEnabled, "End leftovers remains available");
                var rules = IgnoreStore.Load(ignoredPath);
                Check(rules.Any(rule => rule.IsProcess && rule.ProcessName == ignoredChild.ProcessName),
                    "process rule saved");
                typeof(App).GetMethod("Ignore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, ["Another app"]);
                typeof(MainWindow).GetMethod("ShowIgnoredApps", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(main, null);
                await Task.Delay(300);
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
                Console.WriteLine("PASS: WPF dark/light, rule list/removal, immediate Details/notification update, persistence, confirm/cancel, visible-only End");
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

    private static void Capture(Window window, string path)
    {
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
