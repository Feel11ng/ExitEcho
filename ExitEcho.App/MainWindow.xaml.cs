using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class MainWindow : Window
{
    private readonly App _app;
    private Storyboard? _echoMotion;
    private bool? _lastPaused;
    private bool _ignoredExpanded;
    private int _lastEventCount = -1;
    private Guid? _recentEntryId;
    private int _recentTransition;
    private static bool MotionEnabled => SystemParameters.ClientAreaAnimation;

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;
        Loc.LanguageChanged += OnLanguageChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) =>
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        };
        SourceInitialized += (_, _) => _app.ApplyWindowTheme(this);
        StateChanged += (_, _) => UpdateMainMaximizeIcon();
        IgnoredList.ItemsSource = app.IgnoredApps;
        app.IgnoredApps.CollectionChanged += (_, _) => UpdateIgnoredState();
        app.HistoryEntries.CollectionChanged += (_, _) => UpdateRecentActivity(true);
        UpdateIgnoredState();
        UpdateRecentActivity();
        UpdateStatus();
        UpdateMainMaximizeIcon();
        Loaded += (_, _) => EventCountText.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.74, 1, TimeSpan.FromMilliseconds(MotionEnabled ? 400 : 120))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
    }

    internal void UpdateStatus()
    {
        var paused = _app.IsPaused;
        MonitoringText.Text = Loc.Get(paused ? "MainPaused" : "MainMonitoring");
        MonitoringSubtitle.Text = paused
            ? Loc.Get("MainPauseSubtitle") : Loc.Get("MainWatchSubtitle");
        HeaderStatusText.Text = Loc.Get(paused ? "Paused" : "Monitoring");
        var eventCount = _app.EventCount;
        EventCountText.Text = eventCount.ToString();
        SessionCountLabel.Text = Loc.Get(Loc.PluralKey("MainSessionCount", eventCount));
        if (_lastEventCount >= 0 && eventCount > _lastEventCount && IsLoaded)
            AnimateNewLeftover();
        _lastEventCount = eventCount;
        PauseButtonText.Text = Loc.Get(paused ? "ResumeMonitoring" : "PauseMonitoring");
        PauseButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, PauseButtonText.Text);
        PauseIcon.Data = (Geometry)FindResource(paused ? "IconResume" : "IconPause");

        if (_lastPaused == paused)
            return;
        _lastPaused = paused;
        MonitoringDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            paused ? "MutedBrush" : "SuccessBrush");
        MonitoringDot.BeginAnimation(OpacityProperty, null);
        if (paused)
        {
            _echoMotion?.Stop(this);
            EchoMid.Opacity = 0.07;
            EchoBack.Opacity = 0.04;
            EchoFront.BeginAnimation(OpacityProperty, new DoubleAnimation(0.64, TimeSpan.FromMilliseconds(MotionEnabled ? 240 : 120)));
            MonitoringDot.Opacity = 1;
        }
        else
        {
            EchoFront.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(MotionEnabled ? 240 : 120)));
            if (MotionEnabled)
            {
                StartEchoMotion();
                MonitoringDot.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, 0.78, TimeSpan.FromMilliseconds(2200))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                      EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            }
            else
            {
                _echoMotion?.Stop(this);
                MonitoringDot.Opacity = 1;
            }
        }
    }

    private void OnLanguageChanged()
    {
        UpdateStatus();
        UpdateRecentActivity();
        IgnoredList.Items.Refresh();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        _echoMotion?.Stop(this);
        _lastPaused = null;
        UpdateStatus();
    }

    private void UpdateRecentActivity(bool animate = false)
    {
        var latest = _app.HistoryEntries.FirstOrDefault();
        if (animate && IsLoaded && latest is not null && latest.Id != _recentEntryId && MotionEnabled)
        {
            var transition = ++_recentTransition;
            var fadeOut = new DoubleAnimation(RecentActivityRow.Opacity, 0, TimeSpan.FromMilliseconds(90));
            fadeOut.Completed += (_, _) =>
            {
                if (transition != _recentTransition) return;
                SetRecentActivity(latest);
                RecentActivityRow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                RecentActivityTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(180))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
            RecentActivityRow.BeginAnimation(OpacityProperty, fadeOut);
            return;
        }
        ++_recentTransition;
        RecentActivityRow.BeginAnimation(OpacityProperty, null);
        RecentActivityTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RecentActivityRow.Opacity = 1;
        RecentActivityTranslate.Y = 0;
        SetRecentActivity(latest);
    }

    private void SetRecentActivity(HistoryEntry? latest)
    {
        _recentEntryId = latest?.Id;
        RecentAppText.Text = latest?.AppName ?? Loc.Get("NoLeftovers");
        RecentMetaText.Text = latest is null ? string.Empty : $"{latest.ProcessCountText} · {latest.RamText}";
        RecentMetaText.ToolTip = RecentMetaText.Text;
    }

    private void UpdateMainMaximizeIcon()
    {
        var maximized = WindowState == WindowState.Maximized;
        MainMaximizeIcon.Data = (Geometry)FindResource(maximized ? "IconRestore" : "IconMaximize");
        MainMaximizeButton.ToolTip = Loc.Get(maximized ? "Restore" : "Maximize");
        System.Windows.Automation.AutomationProperties.SetName(MainMaximizeButton,
            Loc.Get(maximized ? "Restore" : "Maximize"));
    }

    private void OnMainMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMainMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnMainClose(object sender, RoutedEventArgs e) => Close();

    private void StartEchoMotion()
    {
        if (!MotionEnabled) return;
        _echoMotion = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        AddTrack(_echoMotion, EchoMid, OpacityProperty, (0, .1), (.4, .1), (1.1, .44), (1.55, .44), (2.75, .08), (3.8, .08));
        AddTrack(_echoMotion, EchoMidTranslate, System.Windows.Media.TranslateTransform.XProperty,
            (0, -12), (.4, -12), (1.1, 0), (1.55, 0), (2.75, 9), (3.8, -12));
        AddTrack(_echoMotion, EchoMidTranslate, System.Windows.Media.TranslateTransform.YProperty,
            (0, 6), (.4, 6), (1.1, 0), (1.55, 0), (2.75, -5), (3.8, 6));
        AddTrack(_echoMotion, EchoBack, OpacityProperty, (0, .05), (.75, .05), (1.45, .28), (1.85, .28), (3, .05), (3.8, .05));
        AddTrack(_echoMotion, EchoBackTranslate, System.Windows.Media.TranslateTransform.XProperty,
            (0, -17), (.75, -17), (1.45, 0), (1.85, 0), (3, 13), (3.8, -17));
        AddTrack(_echoMotion, EchoBackTranslate, System.Windows.Media.TranslateTransform.YProperty,
            (0, 10), (.75, 10), (1.45, 0), (1.85, 0), (3, -7), (3.8, 10));
        _echoMotion.Begin(this, true);
    }

    private void AnimateNewLeftover()
    {
        if (!MotionEnabled) return;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        EventCountText.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        EventCountTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        if (_app.IsPaused)
            return;

        EchoFrontTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(200))
            { AutoReverse = true, EasingFunction = ease });
        EchoFrontTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -2, TimeSpan.FromMilliseconds(200))
            { AutoReverse = true, EasingFunction = ease });
        EchoMid.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 0.18, TimeSpan.FromMilliseconds(340))
            { AutoReverse = true, IsAdditive = true, FillBehavior = FillBehavior.Stop }, HandoffBehavior.Compose);
        EchoBack.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 0.12, TimeSpan.FromMilliseconds(380))
            { AutoReverse = true, IsAdditive = true, FillBehavior = FillBehavior.Stop }, HandoffBehavior.Compose);
    }

    private static void AddTrack(Storyboard storyboard, DependencyObject target, DependencyProperty property,
        params (double Seconds, double Value)[] points)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (seconds, value) in points)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void UpdateIgnoredState()
    {
        var hasItems = _app.IgnoredApps.Count > 0;
        IgnoredCountText.Text = _app.IgnoredApps.Count.ToString();
        IgnoredCountText.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        IgnoredListPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = hasItems && IgnoredList.SelectedItem is not null;
        if (_ignoredExpanded)
        {
            AnimateIgnoredLayout(hasItems ? 120 : 22, hasItems ? 570 : 470, hasItems ? 620 : 470);
        }
    }

    private void OnIgnoredToggle(object sender, RoutedEventArgs e)
    {
        _ignoredExpanded = !_ignoredExpanded;
        var targetContentHeight = _ignoredExpanded ? (_app.IgnoredApps.Count > 0 ? 120 : 22) : 0;
        AnimateIgnoredLayout(targetContentHeight,
            _ignoredExpanded && _app.IgnoredApps.Count > 0 ? 570 : 470,
            _ignoredExpanded && _app.IgnoredApps.Count > 0 ? 620 : 470);
        IgnoredContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(_ignoredExpanded ? 1 : 0, TimeSpan.FromMilliseconds(MotionEnabled ? 180 : 120)));
        IgnoredChevronRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            MotionEnabled ? new DoubleAnimation(_ignoredExpanded ? 180 : 0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } } : null);
        if (!MotionEnabled) IgnoredChevronRotate.Angle = _ignoredExpanded ? 180 : 0;
    }

    private void AnimateIgnoredLayout(double contentHeight, double mainHeight, double windowHeight)
    {
        AnimateHeight(IgnoredContent, contentHeight);
        AnimateHeight(MainContent, mainHeight);
        AnimateHeight(this, windowHeight);
    }

    private static void AnimateHeight(FrameworkElement element, double target)
    {
        var from = element.ActualHeight;
        element.BeginAnimation(HeightProperty, null);
        element.Height = target;
        if (MotionEnabled && Math.Abs(from - target) > 0.5)
            element.BeginAnimation(HeightProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(220))
            { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    internal void ShowIgnoredApps()
    {
        if (!_ignoredExpanded)
            OnIgnoredToggle(this, new RoutedEventArgs());
    }

    private void OnPauseResume(object sender, RoutedEventArgs e) => _app.ToggleMonitoring();

    private void OnOpenHistory(object sender, RoutedEventArgs e) => _app.OpenHistory();

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _app.OpenSettings();

    private void OnRemoveIgnored(object sender, RoutedEventArgs e)
    {
        if (IgnoredList.SelectedItem is not IgnoredRule rule)
            return;
        if (IgnoredList.ItemContainerGenerator.ContainerFromItem(rule) is not FrameworkElement row)
        {
            _app.RemoveIgnored(rule);
            return;
        }
        RemoveButton.IsEnabled = false;
        if (!MotionEnabled) { _app.RemoveIgnored(rule); return; }
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        animation.Completed += (_, _) => _app.RemoveIgnored(rule);
        row.BeginAnimation(OpacityProperty, animation);
        var translation = new TranslateTransform();
        row.RenderTransform = translation;
        translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(160)));
        row.Height = row.ActualHeight;
        row.BeginAnimation(HeightProperty, new DoubleAnimation(row.ActualHeight, 0, TimeSpan.FromMilliseconds(160)));
    }

    private void OnIgnoredSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateIgnoredState();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_app.IsExiting)
            return;
        e.Cancel = true;
        Hide();
    }
}
