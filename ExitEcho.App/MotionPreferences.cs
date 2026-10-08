using System.ComponentModel;
using System.Windows;

namespace ExitEcho.App;

internal static class MotionTiming
{
    internal const int FeedbackMs = 100;
    internal const int StateMs = 200;
    internal const int LayoutMs = 260;
}

public sealed class MotionPreferences : INotifyPropertyChanged
{
    public static MotionPreferences Current { get; } = new();

    public static readonly DependencyProperty EnabledForControlProperty = DependencyProperty.RegisterAttached(
        "EnabledForControl", typeof(bool), typeof(MotionPreferences), new PropertyMetadata(true));

    public static bool GetEnabledForControl(DependencyObject element) =>
        (bool)element.GetValue(EnabledForControlProperty);

    public static void SetEnabledForControl(DependencyObject element, bool value) =>
        element.SetValue(EnabledForControlProperty, value);

    public bool Enabled => SystemParameters.ClientAreaAnimation;

    public event PropertyChangedEventHandler? PropertyChanged;

    private MotionPreferences()
    {
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        };
    }
}
