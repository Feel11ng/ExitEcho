using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public enum LeftoverStatus { Detected, Ended, Ignored }

public partial class StatusStepper : System.Windows.Controls.UserControl
{
    private LeftoverStatus _displayedStatus = LeftoverStatus.Detected;

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
        PendingLabel.Text = Loc.Get("LeftRunning");
        EndedLabel.Text = Loc.Get("EndedBy");
        IgnoredLabel.Text = Loc.Get("StepperIgnored");
    }

    private void UpdateStatus(bool animate = true)
    {
        if (!IsLoaded)
            return;
        UpdateLabels();
        var completed = Status != LeftoverStatus.Detected;
        var wasCompleted = _displayedStatus != LeftoverStatus.Detected;
        _displayedStatus = Status;
        if (!SystemParameters.ClientAreaAnimation || !animate)
        {
            var line = (ScaleTransform)AccentProgress.RenderTransform;
            line.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            line.ScaleX = completed ? 1 : 0;
            PendingDot.BeginAnimation(OpacityProperty, null);
            CompletedMark.BeginAnimation(OpacityProperty, null);
            ((ScaleTransform)CompletedMark.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ((ScaleTransform)CompletedMark.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PendingLabel.BeginAnimation(OpacityProperty, null);
            EndedLabel.BeginAnimation(OpacityProperty, null);
            IgnoredLabel.BeginAnimation(OpacityProperty, null);
            PendingDot.Opacity = completed ? 0 : 1;
            CompletedMark.Opacity = completed ? 1 : 0;
            PendingLabel.Opacity = Status == LeftoverStatus.Detected ? 1 : 0;
            EndedLabel.Opacity = Status == LeftoverStatus.Ended ? 1 : 0;
            IgnoredLabel.Opacity = Status == LeftoverStatus.Ignored ? 1 : 0;
            ((ScaleTransform)CompletedMark.RenderTransform).ScaleX = 1;
            ((ScaleTransform)CompletedMark.RenderTransform).ScaleY = 1;
            return;
        }
        AnimateProgress(((ScaleTransform)AccentProgress.RenderTransform).ScaleX, completed ? 1 : 0);
        if (completed != wasCompleted)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PendingDot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(PendingDot.Opacity, completed ? 0 : 1,
                    TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
            CompletedMark.BeginAnimation(OpacityProperty,
                new DoubleAnimation(CompletedMark.Opacity, completed ? 1 : 0,
                    TimeSpan.FromMilliseconds(140))
                { BeginTime = TimeSpan.FromMilliseconds(85), EasingFunction = ease });
            var scale = (ScaleTransform)CompletedMark.RenderTransform;
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                scale.BeginAnimation(property, new DoubleAnimation((double)scale.GetValue(property),
                    completed ? 1 : 0.85, TimeSpan.FromMilliseconds(140))
                { BeginTime = TimeSpan.FromMilliseconds(85), EasingFunction = ease });
        }
        AnimateLabel(PendingLabel, Status == LeftoverStatus.Detected);
        AnimateLabel(EndedLabel, Status == LeftoverStatus.Ended);
        AnimateLabel(IgnoredLabel, Status == LeftoverStatus.Ignored);
    }

    private static void AnimateLabel(UIElement label, bool visible) =>
        label.BeginAnimation(OpacityProperty, new DoubleAnimation(label.Opacity, visible ? 1 : 0,
            TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    private void AnimateProgress(double from, double to) =>
        ((ScaleTransform)AccentProgress.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(MotionTiming.LayoutMs))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
}
