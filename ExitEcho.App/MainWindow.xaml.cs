using System.ComponentModel;
using System.Windows;

namespace ExitEcho.App;

public partial class MainWindow : Window
{
    private readonly App _app;

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;
        SourceInitialized += (_, _) => _app.ApplyWindowTheme(this);
        IgnoredList.ItemsSource = app.IgnoredApps;
        app.IgnoredApps.CollectionChanged += (_, _) => UpdateIgnoredState();
        UpdateIgnoredState();
        UpdateStatus();
    }

    internal void UpdateStatus()
    {
        MonitoringText.Text = _app.IsPaused ? "Monitoring paused" : "Monitoring active";
        MonitoringSubtitle.Text = _app.IsPaused
            ? "ExitEcho is not watching apps right now."
            : "ExitEcho is watching apps in the background.";
        HeaderStatusText.Text = _app.IsPaused ? "Paused" : "Monitoring";
        MonitoringDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            _app.IsPaused ? "MutedBrush" : "SuccessBrush");
        EventCountText.Text = _app.EventCount.ToString();
        PauseButton.Content = _app.IsPaused ? "Resume monitoring" : "Pause monitoring";
    }

    private void UpdateIgnoredState()
    {
        var hasItems = _app.IgnoredApps.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        IgnoredListBorder.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = hasItems && IgnoredList.SelectedItem is not null;
    }

    private void OnPauseResume(object sender, RoutedEventArgs e) => _app.ToggleMonitoring();

    private void OnRemoveIgnored(object sender, RoutedEventArgs e) =>
        _app.RemoveIgnored(IgnoredList.SelectedItem as string);

    private void OnIgnoredSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateIgnoredState();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_app.IsExiting)
            return;
        e.Cancel = true;
        Hide();
    }
}
