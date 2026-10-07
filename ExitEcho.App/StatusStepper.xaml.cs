using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public enum LeftoverStatus { Detected, Ended, Ignored }

public partial class StatusStepper : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(LeftoverStatus), typeof(StatusStepper),
        new PropertyMetadata(LeftoverStatus.Detected, (target, _) => ((StatusStepper)target).UpdateStatus()));

    public LeftoverStatus Status
    {
        get => (LeftoverStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public StatusStepper() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loc.LanguageChanged += UpdateLabels;
        UpdateStatus(animate: false);
        if (!SystemParameters.ClientAreaAnimation)
            return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ((ScaleTransform)LineReveal.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
        ActionVisual.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
            { BeginTime = TimeSpan.FromMilliseconds(100), EasingFunction = ease });
        if (Status != LeftoverStatus.Detected)
            AnimateProgress(0, 1);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Loc.LanguageChanged -= UpdateLabels;

    private void UpdateLabels()
    {
        DetectedLabel.Text = Loc.Get("StepperDetected");
        ActionLabel.Text = Loc.Get(Status switch
        {
            LeftoverStatus.Ended => "EndedBy",
            LeftoverStatus.Ignored => "StepperIgnored",
            _ => "LeftRunning"
        });
    }

    private void UpdateStatus(bool animate = true)
    {
        if (!IsLoaded)
            return;
        UpdateLabels();
        var completed = Status != LeftoverStatus.Detected;
        PendingDot.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
        CompletedMark.Visibility = completed ? Visibility.Visible : Visibility.Collapsed;
        if (!SystemParameters.ClientAreaAnimation || !animate)
        {
            ((ScaleTransform)AccentProgress.RenderTransform).ScaleX = completed ? 1 : 0;
            return;
        }
        AnimateProgress(((ScaleTransform)AccentProgress.RenderTransform).ScaleX, completed ? 1 : 0);
        ActionVisual.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void AnimateProgress(double from, double to) =>
        ((ScaleTransform)AccentProgress.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(230))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
}
