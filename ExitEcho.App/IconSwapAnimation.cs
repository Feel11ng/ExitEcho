using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ExitEcho.App;

internal static class IconSwapAnimation
{
    internal static void Set(Path current, Path incoming, Geometry geometry, bool animate, int totalMilliseconds)
    {
        if (ReferenceEquals(current.Data, geometry) && incoming.Opacity < 0.01)
            return;

        if (!animate || !SystemParameters.ClientAreaAnimation || !current.IsLoaded ||
            (ReferenceEquals(current.Data, geometry) && incoming.Opacity < 0.01))
        {
            Stop(current);
            Stop(incoming);
            current.Data = geometry;
            current.Opacity = 1;
            incoming.Opacity = 0;
            SetScale(current, 1);
            SetScale(incoming, 1);
            return;
        }

        var currentOpacity = current.Opacity;
        var incomingOpacity = incoming.Opacity;
        var currentScale = GetScale(current);
        var incomingScale = GetScale(incoming);
        Stop(current);
        Stop(incoming);
        current.Opacity = currentOpacity;
        incoming.Opacity = incomingOpacity;
        SetScale(current, currentScale);
        SetScale(incoming, incomingScale);
        var targetIsCurrent = ReferenceEquals(current.Data, geometry);
        if (!targetIsCurrent)
            incoming.Data = geometry;
        var token = new object();
        incoming.Tag = token;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(totalMilliseconds);
        current.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(currentOpacity, targetIsCurrent ? 1 : 0, duration) { EasingFunction = ease });
        var incomingFade = new DoubleAnimation(incomingOpacity, targetIsCurrent ? 0 : 1, duration)
        { EasingFunction = ease };
        AnimateScale(current, currentScale, targetIsCurrent ? 1 : 0.9, duration, ease);
        AnimateScale(incoming, incomingScale, targetIsCurrent ? 0.9 : 1, duration, ease);
        incomingFade.Completed += (_, _) =>
        {
            if (!ReferenceEquals(incoming.Tag, token)) return;
            Stop(current);
            Stop(incoming);
            current.Data = geometry;
            current.Opacity = 1;
            incoming.Opacity = 0;
            SetScale(current, 1);
            SetScale(incoming, 1);
        };
        incoming.BeginAnimation(UIElement.OpacityProperty, incomingFade);
    }

    private static void AnimateScale(Path path, double from, double to, TimeSpan duration,
        IEasingFunction ease)
    {
        if (path.RenderTransform is not ScaleTransform scale) return;
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            scale.BeginAnimation(property, new DoubleAnimation(from, to, duration) { EasingFunction = ease });
    }

    private static double GetScale(Path path) =>
        path.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;

    private static void SetScale(Path path, double value)
    {
        if (path.RenderTransform is ScaleTransform scale)
            scale.ScaleX = scale.ScaleY = value;
    }

    private static void Stop(Path path)
    {
        path.BeginAnimation(UIElement.OpacityProperty, null);
        if (path.RenderTransform is not ScaleTransform scale) return;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }
}
