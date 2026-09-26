using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ExitEcho.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;

namespace ExitEcho.App;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\ExitEcho.Gui";
    private const string ActivateEventName = @"Local\ExitEcho.Gui.Activate";
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<string> _ignoredItems = [];
    private readonly List<NotificationWindow> _notifications = [];
    private MainWindow? _main;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private MemoryStream? _trayIconStream;
    private Forms.ToolStripMenuItem? _pauseMenuItem;
    private CancellationTokenSource? _monitorCancellation;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _activateCancellation;
    private Task? _activateTask;
    private bool _ownsInstanceMutex;
    private bool _paused;
    private bool _exiting;
    private int _monitorGeneration;
    private int _eventCount;
    private bool _lightTheme = true;

    internal bool IsExiting => _exiting;
    internal bool IsPaused => _paused;
    internal int EventCount => _eventCount;
    internal ObservableCollection<string> IgnoredApps => _ignoredItems;

    internal void ApplyWindowTheme(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        var dark = _lightTheme ? 0 : 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);
        _ownsInstanceMutex = isFirstInstance;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        if (!isFirstInstance)
        {
            _activateEvent.Set();
            Shutdown();
            return;
        }

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        foreach (var name in LoadIgnored())
        {
            _ignored.Add(name);
            _ignoredItems.Add(name);
        }

        _main = new MainWindow(this);
        MainWindow = _main;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(OpenMain));
        _pauseMenuItem = new Forms.ToolStripMenuItem("Pause monitoring", null,
            (_, _) => Dispatcher.Invoke(ToggleMonitoring));
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        using (var iconResource = System.Windows.Application.GetResourceStream(
                   new Uri("pack://application:,,,/Assets/ExitEcho.ico")).Stream)
        {
            _trayIconStream = new MemoryStream();
            iconResource.CopyTo(_trayIconStream);
            _trayIconStream.Position = 0;
            var iconSize = Forms.SystemInformation.SmallIconSize;
            _trayIcon = new System.Drawing.Icon(_trayIconStream, iconSize.Width, iconSize.Height);
        }

        _tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "ExitEcho",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenMain);

        _activateCancellation = new CancellationTokenSource();
        _activateTask = Task.Run(() => WaitForActivation(_activateCancellation.Token));
        StartMonitoring();
    }

    private void WaitForActivation(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_activateEvent?.WaitOne(250) != true)
                continue;
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(new Action(OpenMain));
        }
    }

    internal void OpenMain()
    {
        if (_main is null)
            return;
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    internal void ToggleMonitoring()
    {
        if (_paused)
            StartMonitoring();
        else
            PauseMonitoring();
    }

    internal void RemoveIgnored(string? name)
    {
        if (name is null || !_ignored.Remove(name))
            return;
        _ignoredItems.Remove(name);
        SaveIgnored();
    }

    internal void Ignore(string name)
    {
        if (_ignored.Add(name))
        {
            _ignoredItems.Add(name);
            SaveIgnored();
        }
        foreach (var window in _notifications.Where(window =>
                     string.Equals(window.AppName, name, StringComparison.OrdinalIgnoreCase)).ToArray())
            window.Close();
    }

    internal void OpenDetails(LeftoverEvent leftover)
    {
        new DetailsWindow(leftover).Show();
    }

    internal void NotificationClosed(NotificationWindow window)
    {
        _notifications.Remove(window);
        PositionNotifications();
    }

    private void StartMonitoring()
    {
        _monitorCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _monitorCancellation = cancellation;
        _paused = false;
        var generation = ++_monitorGeneration;
        UpdateMonitoringState();

        var monitor = new PassiveMonitor { WriteToConsole = false };
        monitor.LeftoversFound += leftover => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_exiting && !_paused && generation == _monitorGeneration)
                ShowLeftover(leftover);
        }));

        _ = Task.Run(async () =>
        {
            try
            {
                await monitor.WatchAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_exiting || generation != _monitorGeneration)
                        return;
                    PauseMonitoring();
                    MessageBox.Show($"Monitoring stopped: {exception.Message}", "ExitEcho",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }));
            }
        });
    }

    private void PauseMonitoring()
    {
        _paused = true;
        ++_monitorGeneration;
        _monitorCancellation?.Cancel();
        UpdateMonitoringState();
    }

    private void UpdateMonitoringState()
    {
        if (_pauseMenuItem is not null)
            _pauseMenuItem.Text = _paused ? "Resume monitoring" : "Pause monitoring";
        _main?.UpdateStatus();
    }

    private void ShowLeftover(LeftoverEvent leftover)
    {
        _eventCount++;
        _main?.UpdateStatus();
        if (_ignored.Contains(leftover.AppName))
            return;

        var window = new NotificationWindow(this, leftover);
        _notifications.Add(window);
        PositionNotifications();
        window.Show();
    }

    private void PositionNotifications()
    {
        var workArea = SystemParameters.WorkArea;
        for (var index = 0; index < _notifications.Count; index++)
        {
            var window = _notifications[index];
            window.Left = workArea.Right - window.Width - 16;
            window.Top = workArea.Bottom - (index + 1) * (window.Height + 8) - 8;
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        _monitorCancellation?.Cancel();
        foreach (var window in _notifications.ToArray())
            window.Close();
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _activateCancellation?.Cancel();
        _activateTask?.Wait(1000);
        _activateEvent?.Dispose();
        _activateCancellation?.Dispose();
        if (_ownsInstanceMutex)
            _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _trayIcon?.Dispose();
        _trayIconStream?.Dispose();
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(ApplyTheme));

    private void ApplyTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        _lightTheme = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        var colors = _lightTheme
            ? new[] { "#F5F7FA", "#FFFFFF", "#18212D", "#667485", "#DCE2E8", "#2563EB", "#FFFFFF", "#EEF2F6", "#198754" }
            : new[] { "#151A20", "#202730", "#F2F5F8", "#AAB5C2", "#37424E", "#6B9DFF", "#111820", "#2B3540", "#48BC80" };
        var names = new[] { "BackgroundBrush", "SurfaceBrush", "TextBrush", "MutedBrush",
            "BorderBrush", "AccentBrush", "ButtonTextBrush", "SecondaryBrush", "SuccessBrush" };
        for (var index = 0; index < names.Length; index++)
            Resources[names[index]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index])!);
        foreach (Window window in Windows)
            ApplyWindowTheme(window);
    }

    private static string IgnoredFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExitEcho", "ignored.json");

    private static string LegacyIgnoredFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloseTrace", "ignored.json");

    private static IEnumerable<string> LoadIgnored()
    {
        try
        {
            var path = File.Exists(IgnoredFile) ? IgnoredFile : LegacyIgnoredFile;
            if (!File.Exists(path))
                return [];
            return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void SaveIgnored()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IgnoredFile)!);
            var temp = IgnoredFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_ignoredItems.ToArray()));
            File.Move(temp, IgnoredFile, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not save ignored apps: {exception.Message}", "ExitEcho",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
