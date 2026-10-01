using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ExitEcho.App.Localization;
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
    private readonly HistoryStore _history = new();
    private readonly ConditionalWeakTable<LeftoverEvent, StrongBox<Guid>> _historyIds = new();
    private readonly AppSettings _settings = SettingsStore.Load();
    private MainWindow? _main;
    private HistoryWindow? _historyWindow;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _tray;
    private System.Windows.Threading.DispatcherTimer? _trayClickTimer;
    private System.Drawing.Icon? _trayIcon;
    private MemoryStream? _trayIconStream;
    private Forms.ToolStripMenuItem? _pauseMenuItem;
    private Forms.ToolStripMenuItem? _openMenuItem;
    private Forms.ToolStripMenuItem? _settingsMenuItem;
    private Forms.ToolStripMenuItem? _exitMenuItem;
    private CancellationTokenSource? _monitorCancellation;
    private PassiveMonitor? _activeMonitor;
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
    private long _lastTrayDoubleClickAt = -1;

    internal bool IsExiting => _exiting;
    internal bool IsPaused => _paused;
    internal int EventCount => _eventCount;
    internal ObservableCollection<string> IgnoredApps => _ignoredItems;
    internal AppSettings Settings => _settings;

    internal void ApplyWindowTheme(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        var dark = _lightTheme ? 0 : 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        var rounded = 2;
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
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

        Loc.SetLanguage(_settings.Language);
        Loc.LanguageChanged += UpdateMenuLocalization;
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
        _openMenuItem = new Forms.ToolStripMenuItem(Loc.Get("TrayOpen"), null,
            (_, _) => Dispatcher.Invoke(OpenMain));
        menu.Items.Add(_openMenuItem);
        _pauseMenuItem = new Forms.ToolStripMenuItem(Loc.Get("PauseMonitoring"), null,
            (_, _) => Dispatcher.Invoke(ToggleMonitoring));
        menu.Items.Add(_pauseMenuItem);
        _settingsMenuItem = new Forms.ToolStripMenuItem(Loc.Get("TraySettings"), null,
            (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(_settingsMenuItem);
        _exitMenuItem = new Forms.ToolStripMenuItem(Loc.Get("TrayExit"), null,
            (_, _) => Dispatcher.Invoke(ExitApplication));
        menu.Items.Add(_exitMenuItem);

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
            Text = Loc.Get("Brand"),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayClickTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime + 50)
        };
        _trayClickTimer.Tick += (_, _) =>
        {
            _trayClickTimer.Stop();
            OpenMain();
        };
        _tray.MouseClick += OnTrayMouseClick;
        _tray.MouseDoubleClick += OnTrayMouseDoubleClick;

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

    private void OnTrayMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Left)
            return;

        var now = Environment.TickCount64;
        if (_lastTrayDoubleClickAt >= 0 &&
            now - _lastTrayDoubleClickAt < Forms.SystemInformation.DoubleClickTime)
            return;
        _trayClickTimer?.Start();
    }

    private void OnTrayMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Left)
            return;

        _trayClickTimer?.Stop();
        _lastTrayDoubleClickAt = Environment.TickCount64;
        OpenMain();
    }

    internal void OpenMain()
    {
        if (_main is null)
            return;
        if (!_main.IsVisible)
            _main.Show();
        var handle = new WindowInteropHelper(_main).Handle;
        if (IsIconic(handle))
            ShowWindow(handle, 9); // SW_RESTORE
        else if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        if (!_main.IsActive)
            _main.Activate();
        SetForegroundWindow(handle);
        _main.Focus();
    }

    internal void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    internal void OpenIgnoredApps()
    {
        OpenMain();
        _main?.ShowIgnoredApps();
    }

    internal void SetLanguage(string language)
    {
        _settings.Language = language;
        SaveSettings();
        Loc.SetLanguage(language);
    }

    internal void SetTheme(string theme)
    {
        _settings.Theme = theme is "dark" or "light" ? theme : "system";
        SaveSettings();
        ApplyTheme();
    }

    internal bool SetStartup(bool enabled)
    {
        try
        {
            SettingsStore.SetStartup(enabled);
            _settings.StartWithWindows = enabled;
            SaveSettings();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            MessageBox.Show(Loc.Format("StartupFailed", exception.Message), Loc.Get("Brand"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return SettingsStore.IsStartupEnabled();
    }

    internal void SetNotifications(bool enabled)
    {
        _settings.ShowNotifications = enabled;
        SaveSettings();
    }

    internal void SetNotificationDelay(int seconds)
    {
        _settings.NotificationDelaySeconds = Math.Clamp(seconds, 3, 60);
        if (_activeMonitor is not null)
            _activeMonitor.NotificationDelaySeconds = _settings.NotificationDelaySeconds;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try { SettingsStore.Save(_settings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Loc.Format("SettingsSaveFailed", exception.Message), Loc.Get("Brand"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    internal void OpenDetails(LeftoverEvent leftover, Window? origin = null)
    {
        var details = new DetailsWindow(leftover, origin);
        details.LeftoversEnded += ended =>
        {
            if (ended > 0 && _historyIds.TryGetValue(leftover, out var id))
                _history.MarkEnded(id.Value);
        };
        details.Show();
    }

    internal void OpenHistory()
    {
        if (_historyWindow is null)
        {
            _historyWindow = new HistoryWindow(_history.Entries);
            _historyWindow.Closed += (_, _) => _historyWindow = null;
        }
        _historyWindow.Show();
        _historyWindow.Activate();
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

        var monitor = new PassiveMonitor
        {
            WriteToConsole = false,
            NotificationDelaySeconds = _settings.NotificationDelaySeconds
        };
        _activeMonitor = monitor;
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
                    MessageBox.Show(Loc.Format("MonitoringStopped", exception.Message), Loc.Get("Brand"),
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
        _activeMonitor = null;
        UpdateMonitoringState();
    }

    private void UpdateMonitoringState()
    {
        UpdateMenuLocalization();
        _main?.UpdateStatus();
    }

    private void UpdateMenuLocalization()
    {
        if (_openMenuItem is not null) _openMenuItem.Text = Loc.Get("TrayOpen");
        if (_pauseMenuItem is not null)
            _pauseMenuItem.Text = Loc.Get(_paused ? "ResumeMonitoring" : "PauseMonitoring");
        if (_settingsMenuItem is not null) _settingsMenuItem.Text = Loc.Get("TraySettings");
        if (_exitMenuItem is not null) _exitMenuItem.Text = Loc.Get("TrayExit");
    }

    private void ShowLeftover(LeftoverEvent leftover)
    {
        _historyIds.Add(leftover, new StrongBox<Guid>(_history.Record(leftover)));
        _eventCount++;
        _main?.UpdateStatus();
        if (_ignored.Contains(leftover.AppName) || !_settings.ShowNotifications)
            return;

        var window = new NotificationWindow(this, leftover);
        _notifications.Add(window);
        window.SourceInitialized += (_, _) => PositionNotifications();
        window.Show();
    }

    private void PositionNotifications()
    {
        var screen = Forms.Screen.FromPoint(Forms.Control.MousePosition);
        for (var index = 0; index < _notifications.Count; index++)
        {
            var window = _notifications[index];
            var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var bottomRight = transform.Transform(new System.Windows.Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
            window.Left = bottomRight.X - window.Width - 16;
            window.Top = bottomRight.Y - (index + 1) * (window.Height + 8) - 8;
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
        Loc.LanguageChanged -= UpdateMenuLocalization;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _activateCancellation?.Cancel();
        _trayClickTimer?.Stop();
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
        _lightTheme = _settings.Theme switch
        {
            "light" => true,
            "dark" => false,
            _ => key?.GetValue("AppsUseLightTheme") is not int value || value != 0
        };
        var colors = _lightTheme
            ? new[] { "#F5F7F8", "#FFFFFF", "#24343C", "#60737B", "#D8E1E4", "#397579", "#FFFFFF", "#EBF1F2", "#398365" }
            : new[] { "#151F26", "#202E36", "#F0F3F3", "#A7B8BB", "#34464E", "#84ADA7", "#14232A", "#24343C", "#78C6A2" };
        var names = new[] { "BackgroundBrush", "SurfaceBrush", "TextBrush", "MutedBrush",
            "BorderBrush", "AccentBrush", "ButtonTextBrush", "SecondaryBrush", "SuccessBrush" };
        for (var index = 0; index < names.Length; index++)
            Resources[names[index]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index])!);
        Resources["DangerBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            _lightTheme ? "#B42318" : "#FF7B72")!);
        Resources["DangerButtonTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            _lightTheme ? "#FFFFFF" : "#14232A")!);
        Resources["FocusBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            _lightTheme ? "#397579" : "#84ADA7")!);
        var extra = _lightTheme
            ? new[] { "#ECF2F3", "#F7FAFA", "#E5ECEE", "#829CA3", "#365B65", "#5C7C84", "#F3F7F7" }
            : new[] { "#1C2B33", "#2A3D46", "#2B3C44", "#D3E2E5", "#8FA2AA", "#B4C5CA", "#293B44" };
        var extraNames = new[] { "MainBackgroundBrush", "EchoFrontFillBrush", "HoverBrush", "EchoFrontBrush",
            "EchoBackBrush", "EchoMiddleBrush", "CardHoverBrush" };
        for (var index = 0; index < extraNames.Length; index++)
            Resources[extraNames[index]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(extra[index])!);
        Resources["NotificationBackgroundBrush"] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(_lightTheme ? "#FFFFFF" : "#1E2B33")!,
            (Color)ColorConverter.ConvertFromString(_lightTheme ? "#F5F8F9" : "#1A262E")!, 90);
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
            MessageBox.Show(Loc.Format("IgnoredSaveFailed", exception.Message), Loc.Get("Brand"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
