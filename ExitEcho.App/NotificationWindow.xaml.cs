using System.Windows;
using ExitEcho.Core;

namespace ExitEcho.App;

public partial class NotificationWindow : Window
{
    private readonly App _app;
    private readonly LeftoverEvent _leftover;
    internal string AppName => _leftover.AppName;

    public NotificationWindow(App app, LeftoverEvent leftover)
    {
        InitializeComponent();
        _app = app;
        _leftover = leftover;
        SummaryText.Text = $"{leftover.AppName} exited, but {leftover.Processes.Count} related processes are still running";
        RamText.Text = $"{Math.Ceiling(leftover.Processes.Sum(process => process.WorkingSetBytes) / 1_000_000d):N0} MB RAM";
        Closed += (_, _) => _app.NotificationClosed(this);
    }

    private void OnDetails(object sender, RoutedEventArgs e)
    {
        _app.OpenDetails(_leftover);
        Close();
    }

    private void OnIgnore(object sender, RoutedEventArgs e) => _app.Ignore(_leftover.AppName);
}
