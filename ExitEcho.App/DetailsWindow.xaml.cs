using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using ExitEcho.Core;
using MessageBox = System.Windows.MessageBox;

namespace ExitEcho.App;

public partial class DetailsWindow : Window
{
    private readonly LeftoverEvent _leftover;

    public DetailsWindow(LeftoverEvent leftover)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ((App)System.Windows.Application.Current).ApplyWindowTheme(this);
        _leftover = leftover;
        AppTitle.Text = leftover.AppName;
        SummaryText.Text = $"{leftover.Processes.Count} related processes remained after the window closed.";
        ProcessesList.ItemsSource = leftover.Processes.Select(process => new
        {
            process.Name,
            process.Pid,
            RamText = $"{process.WorkingSetBytes / 1_000_000d:N1} MB"
        }).ToArray();
    }

    private void OnEndLeftovers(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            $"End { _leftover.Processes.Count } related processes from {_leftover.AppName}?",
            "Confirm ending processes", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
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

        MessageBox.Show(this, $"Ended {ended} of {_leftover.Processes.Count} listed processes.",
            "ExitEcho", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }
}
