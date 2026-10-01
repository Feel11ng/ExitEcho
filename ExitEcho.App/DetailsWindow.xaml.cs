using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ExitEcho.Core;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class DetailsWindow : Window
{
    private readonly LeftoverEvent _leftover;
    private int _animatedCards;
    internal event Action<int>? LeftoversEnded;

    public DetailsWindow(LeftoverEvent leftover, Window? origin = null)
    {
        InitializeComponent();
        if (origin is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = origin.Left + origin.Width - Width;
            Top = origin.Top + origin.Height - Height;
        }
        SourceInitialized += (_, _) => ((App)System.Windows.Application.Current).ApplyWindowTheme(this);
        _leftover = leftover;
        AppTitle.Text = leftover.AppName;
        var appIcon = AppIconCache.Get(leftover.ExecutablePath);
        if (appIcon is not null)
        {
            AppIcon.Source = appIcon;
            AppIcon.Visibility = Visibility.Visible;
            AppIconFallback.Visibility = Visibility.Collapsed;
        }
        RefreshLocalization();
        Loc.LanguageChanged += RefreshLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= RefreshLocalization;
        Loaded += (_, _) => AnimateOpen();
    }

    private void RefreshLocalization()
    {
        SummaryText.Text = Loc.Format(Loc.PluralKey("DetailsSummary", _leftover.Processes.Count), _leftover.Processes.Count);
        MemorySummaryText.Text = Loc.Format("MemorySummary", Math.Ceiling(
            _leftover.Processes.Sum(process => process.WorkingSetBytes) / 1_000_000d));
        ProcessesList.ItemsSource = _leftover.Processes.Select(process => new
        {
            process.Name,
            PidText = Loc.Format("PidValue", process.Pid),
            RamText = Loc.Format("RamValuePrecise", process.WorkingSetBytes / 1_000_000d)
        }).ToArray();
    }

    private void AnimateOpen()
    {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            { EasingFunction = ease });
            var scale = new ScaleTransform(0.98, 0.98);
            DetailsBody.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            DetailsBody.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });

            FadeIn(HeaderOverline, 55, 190);
            FadeIn(AppTitle, 95, 220);
            FadeIn(SummaryGrid, 135, 220);
            FadeIn(EchoMark, 205, 240);

            StartEchoPulse(EchoBack, 0.17, 0.24, 1.5, 2100);
            StartEchoPulse(EchoMiddle, 0.32, 0.39, 0.8, 1700);
    }

    private static void FadeIn(UIElement element, int delayMs, int durationMs)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void StartEchoPulse(Border layer, double initialOpacity, double peakOpacity,
        double travel, int durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(initialOpacity, peakOpacity, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        });
        var offset = new TranslateTransform();
        layer.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, travel, duration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease
        });
    }

    private void OnProcessCardLoaded(object sender, RoutedEventArgs e)
    {
        var card = (Border)sender;
        card.RenderTransform = new TranslateTransform(0, 5);
        var delay = TimeSpan.FromMilliseconds(170 + _animatedCards++ * 75);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        { BeginTime = delay, EasingFunction = ease });
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(250)) { BeginTime = delay, EasingFunction = ease });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnEndLeftovers(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmationWindow(_leftover.AppName, _leftover.Processes.Count) { Owner = this };
        if (confirmation.ShowDialog() != true)
            return;

        var ended = 0;
        foreach (var item in _leftover.Processes)
        {
            try
            {
                using var process = Process.GetProcessById(item.Pid);
                if (process.HasExited ||
                    Math.Abs(process.StartTime.ToUniversalTime().Ticks - item.StartUtcTicks) > TimeSpan.TicksPerMillisecond)
                    continue;
                process.Kill(entireProcessTree: false);
                ended++;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
            }
        }

        LeftoversEnded?.Invoke(ended);

        var result = new CompletionWindow(ended, _leftover.Processes.Count) { Owner = this };
        result.ShowDialog();
        Close();
    }
}
