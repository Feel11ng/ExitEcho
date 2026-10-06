using System.ComponentModel;
using System.Windows;

namespace ExitEcho.App;

public sealed class MotionPreferences : INotifyPropertyChanged
{
    public static MotionPreferences Current { get; } = new();

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
