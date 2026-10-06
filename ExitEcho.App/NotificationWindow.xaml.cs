using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ExitEcho.Core;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class NotificationWindow : Window
{
    private static readonly DependencyProperty AnimatedCountProperty = DependencyProperty.Register(
        nameof(AnimatedCount), typeof(double), typeof(NotificationWindow),
        new PropertyMetadata(0d, (owner, args) =>
            ((NotificationWindow)owner).CountText.Text = Math.Round((double)args.NewValue).ToString("N0", Loc.Culture)));

    private readonly App _app;
    private LeftoverEvent _leftover;
    internal string AppName => _leftover.AppName;

    private double AnimatedCount
    {
        get => (double)GetValue(AnimatedCountProperty);
        set => SetValue(AnimatedCountProperty, value);
    }

    public NotificationWindow(App app, LeftoverEvent leftover)
    {
        InitializeComponent();
        _app = app;
        _leftover = leftover;
        AppNameText.Text = leftover.AppName;
        var appIcon = AppIconCache.Get(leftover.ExecutablePath);
        if (appIcon is not null)
        {
            AppIcon.Source = appIcon;
            AppIcon.Visibility = Visibility.Visible;
            AppIconFallback.Visibility = Visibility.Collapsed;
        }
        RefreshLocalization();
        Loc.LanguageChanged += RefreshLocalization;
        SourceInitialized += (_, _) => _app.ApplyWindowTheme(this);
        Loaded += (_, _) => AnimateEntrance();
        Closed += (_, _) => { Loc.LanguageChanged -= RefreshLocalization; _app.NotificationClosed(this); };
    }

    private void RefreshLocalization()
    {
        var count = _leftover.Processes.Count;
        ProcessText.Text = Loc.Format(Loc.PluralKey("ProcessRemain", count), count);
        CountText.Visibility = Loc.Culture.TwoLetterISOLanguageName == "ar"
            ? Visibility.Collapsed : Visibility.Visible;
        RamText.Text = Loc.Format("RamValue", Math.Ceiling(_leftover.Processes.Sum(process => process.WorkingSetBytes) / 1_000_000d));
        CountText.Text = Math.Round(AnimatedCount).ToString("N0", Loc.Culture);
    }

    internal void ApplyIgnoredRules(IEnumerable<IgnoredRule> rules)
    {
        var filtered = IgnoreStore.Filter(_leftover, rules);
        if (filtered is null)
        {
            Close();
            return;
        }
        if (filtered.Processes.Count == _leftover.Processes.Count)
            return;
        _app.TransferHistoryId(_leftover, filtered);
        _leftover = filtered;
        BeginAnimation(AnimatedCountProperty, null);
        AnimatedCount = filtered.Processes.Count;
        RefreshLocalization();
    }

    private void AnimateEntrance()
    {
        PreludeWindow.BeginAnimation(OpacityProperty, Fade(0.7, 0, 360, 230));
        var closeScale = (ScaleTransform)PreludeWindow.RenderTransform;
        closeScale.BeginAnimation(ScaleTransform.ScaleXProperty, Move(1, 0.97, 360, 230));
        closeScale.BeginAnimation(ScaleTransform.ScaleYProperty, Move(1, 0.97, 360, 230));

        AnimateTrail(PreludeMiddle, 400, 500, 0.34, 0, 11, 4, -8);
        AnimateTrail(PreludeBack, 470, 540, 0.2, 0, 16, 6, -11);
        ToastCard.BeginAnimation(OpacityProperty, Fade(0, 1, 360, 730));
        ToastScale.BeginAnimation(ScaleTransform.ScaleXProperty, Move(0.982, 1, 360, 730));
        ToastScale.BeginAnimation(ScaleTransform.ScaleYProperty, Move(0.982, 1, 360, 730));
        ToastSlide.BeginAnimation(TranslateTransform.YProperty, Move(5, 0, 360, 730));
        AnimateTrail(EchoMiddle, 810, 1100, 0.48, 0.16, 9, 6, -7);
        AnimateTrail(EchoBack, 880, 1200, 0.29, 0.09, 14, 8, -10);
        TextContent.BeginAnimation(OpacityProperty, Fade(0, 1, 300, 970));
        BeginAnimation(AnimatedCountProperty, Move(0, _leftover.Processes.Count, 360, 1090));
        RamText.BeginAnimation(OpacityProperty, Fade(0, 1, 300, 1160));
        DetectedStatus.BeginAnimation(OpacityProperty, Fade(0, 1, 300, 1230));
    }

    private static DoubleAnimation Move(double from, double to, int duration, int delay = 0) => new(from, to,
        TimeSpan.FromMilliseconds(duration))
    {
        BeginTime = TimeSpan.FromMilliseconds(delay),
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.HoldEnd
    };

    private static DoubleAnimation Fade(double from, double to, int duration, int delay = 0) =>
        Move(from, to, duration, delay);

    private static DoubleAnimation EaseTo(double value) => new()
    {
        To = value,
        Duration = TimeSpan.FromMilliseconds(200),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private static void AnimateTrail(Border layer, int delay, int duration, double peak, double finalOpacity,
        double endX, double startY, double endY)
    {
        var opacity = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delay) };
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration * 0.3)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(finalOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        layer.BeginAnimation(OpacityProperty, opacity);
        var offset = (TranslateTransform)layer.RenderTransform;
        offset.BeginAnimation(TranslateTransform.XProperty, Move(-8, endX, duration, delay));
        offset.BeginAnimation(TranslateTransform.YProperty, Move(startY, endY, duration, delay));
    }

    private void OnDetailsEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        ((TranslateTransform)((TransformGroup)DetailsArrow.RenderTransform).Children[1])
        .BeginAnimation(TranslateTransform.XProperty, EaseTo(3));

    private void OnDetailsLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        ((TranslateTransform)((TransformGroup)DetailsArrow.RenderTransform).Children[1])
        .BeginAnimation(TranslateTransform.XProperty, EaseTo(0));

    private void OnDetails(object sender, RoutedEventArgs e)
    {
        _app.OpenDetails(_leftover, this);
        var exit = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130));
        exit.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, exit);
    }

    private void OnIgnore(object sender, RoutedEventArgs e) => _app.Ignore(_leftover.AppName);
}
