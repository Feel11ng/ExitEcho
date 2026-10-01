using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class ChromeTitleBar : System.Windows.Controls.UserControl
{
    public ChromeTitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window is null) return;
            void RefreshLocalization()
            {
                TitleLabel.Text = window.Title;
                MinimizeButton.ToolTip = Loc.Get("Minimize");
                System.Windows.Automation.AutomationProperties.SetName(MinimizeButton, Loc.Get("Minimize"));
                UpdateMaximizeIcon(window);
            }
            RefreshLocalization();
            Loc.LanguageChanged += RefreshLocalization;
            window.Closed += (_, _) => Loc.LanguageChanged -= RefreshLocalization;
            MaximizeButton.Visibility = window.ResizeMode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible;
            MinimizeButton.Visibility = window.ShowInTaskbar ? Visibility.Visible : Visibility.Collapsed;
            window.StateChanged += (_, _) => UpdateMaximizeIcon(window);
            UpdateMaximizeIcon(window);
        };
    }

    private void UpdateMaximizeIcon(Window window)
    {
        var restored = window.WindowState == WindowState.Maximized;
        MaximizeIcon.Data = (Geometry)FindResource(restored ? "IconRestore" : "IconMaximize");
        MaximizeButton.ToolTip = Loc.Get(restored ? "Restore" : "Maximize");
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, Loc.Get(restored ? "Restore" : "Maximize"));
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this)!;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void OnClose(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.Close();
}
