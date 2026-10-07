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
    private bool _closing;
    internal string AppName => _leftover.AppName;
    internal LeftoverEvent CurrentLeftover => _leftover;
    internal string IdentityKey => App.NotificationKey(_leftover);
    internal bool IsClosing => _closing;

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
        UpdateAppIdentity();
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

    internal void UpdateLeftover(LeftoverEvent leftover)
    {
        var oldCount = AnimatedCount;
        _leftover = leftover;
        UpdateAppIdentity();
        RefreshLocalization();
        BeginAnimation(AnimatedCountProperty, null);
        AnimatedCount = leftover.Processes.Count;
        if (SystemParameters.ClientAreaAnimation && Math.Abs(oldCount - AnimatedCount) > 0.5)
            BeginAnimation(AnimatedCountProperty, new DoubleAnimation(oldCount, AnimatedCount,
                TimeSpan.FromMilliseconds(140))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        DetectedStatus.BeginAnimation(OpacityProperty, null);
        DetectedStatus.Opacity = 1;
    }

    private void UpdateAppIdentity()
    {
        AppNameText.Text = _leftover.AppName;
        AppIcon.Source = AppIconCache.Get(_leftover.ExecutablePath);
        AppIcon.Visibility = AppIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
        AppIconFallback.Visibility = AppIcon.Source is null ? Visibility.Visible : Visibility.Collapsed;
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
        PreludeWindow.Opacity = 0;
        ToastScale.ScaleX = ToastScale.ScaleY = 1;
        var motion = SystemParameters.ClientAreaAnimation;
        ToastCard.BeginAnimation(OpacityProperty, Fade(0, 1, motion ? 200 : 120));
        if (motion)
            ToastSlide.BeginAnimation(TranslateTransform.YProperty, Move(6, 0, 200));
        else
            ToastSlide.Y = 0;
        EchoMiddle.BeginAnimation(OpacityProperty, Fade(0, 0.16, 140, motion ? 35 : 0));
        EchoBack.BeginAnimation(OpacityProperty, Fade(0, 0.09, 140, motion ? 55 : 0));
        TextContent.BeginAnimation(OpacityProperty, Fade(0, 1, motion ? 160 : 120, motion ? 50 : 0));
        RamText.BeginAnimation(OpacityProperty, Fade(0, 1, motion ? 130 : 120, motion ? 75 : 0));
        DetectedStatus.BeginAnimation(OpacityProperty, Fade(0, 1, motion ? 130 : 120, motion ? 80 : 0));
        BeginAnimation(AnimatedCountProperty, motion
            ? Move(0, _leftover.Processes.Count, 145, 65)
            : new DoubleAnimation(_leftover.Processes.Count, TimeSpan.Zero));
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
        Duration = TimeSpan.FromMilliseconds(140),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private void OnDetailsEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        ((TranslateTransform)((TransformGroup)DetailsArrow.RenderTransform).Children[1])
            .BeginAnimation(TranslateTransform.XProperty, EaseTo(3));
    }

    private void OnDetailsLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var offset = (TranslateTransform)((TransformGroup)DetailsArrow.RenderTransform).Children[1];
        if (!SystemParameters.ClientAreaAnimation) { offset.X = 0; return; }
        offset.BeginAnimation(TranslateTransform.XProperty, EaseTo(0));
    }

    private void OnDetails(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _app.OpenDetails(_leftover, this);
        CloseWithMotion(Close);
    }

    private void OnIgnore(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        CloseWithMotion(() => _app.Ignore(_leftover.AppName, _leftover));
    }

    private void CloseWithMotion(Action action)
    {
        _closing = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            action();
            return;
        }
        var exit = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(190));
        exit.Completed += (_, _) => action();
        ToastCard.BeginAnimation(OpacityProperty, exit);
        ToastSlide.BeginAnimation(TranslateTransform.YProperty, Move(0, 4, 190));
    }
}
