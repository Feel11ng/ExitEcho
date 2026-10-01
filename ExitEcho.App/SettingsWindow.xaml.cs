using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private bool _loading;

    internal SettingsWindow(App app)
    {
        InitializeComponent();
        _app = app;
        SourceInitialized += (_, _) => _app.ApplyWindowTheme(this);
        Loc.LanguageChanged += RefreshLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= RefreshLocalization;
        _loading = true;
        LanguageCombo.Items.Add(new ComboBoxItem { Tag = "system" });
        foreach (var (code, name) in Loc.Languages)
            LanguageCombo.Items.Add(new ComboBoxItem { Tag = code, Content = name });
        ThemeCombo.Items.Add(new ComboBoxItem { Tag = "system" });
        ThemeCombo.Items.Add(new ComboBoxItem { Tag = "dark" });
        ThemeCombo.Items.Add(new ComboBoxItem { Tag = "light" });
        LanguageCombo.SelectedIndex = Math.Max(0, Loc.Languages.ToList().FindIndex(
            x => x.Code.Equals(app.Settings.Language, StringComparison.OrdinalIgnoreCase)) + 1);
        ThemeCombo.SelectedIndex = app.Settings.Theme switch { "dark" => 1, "light" => 2, _ => 0 };
        StartupToggle.IsChecked = SettingsStore.IsStartupEnabled();
        NotificationsToggle.IsChecked = app.Settings.ShowNotifications;
        DelayBox.Text = app.Settings.NotificationDelaySeconds.ToString(CultureInfo.InvariantCulture);
        _loading = false;
        RefreshLocalization();
    }

    private void RefreshLocalization()
    {
        VersionText.Text = Loc.Format("Version",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");
        ((ComboBoxItem)LanguageCombo.Items[0]).Content = Loc.Get("SystemDefault");
        ((ComboBoxItem)ThemeCombo.Items[0]).Content = Loc.Get("ThemeSystem");
        ((ComboBoxItem)ThemeCombo.Items[1]).Content = Loc.Get("ThemeDark");
        ((ComboBoxItem)ThemeCombo.Items[2]).Content = Loc.Get("ThemeLight");
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedItem is not ComboBoxItem item)
            return;
        _app.SetLanguage((string)item.Tag);
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeCombo.SelectedItem is ComboBoxItem item)
            _app.SetTheme((string)item.Tag);
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            StartupToggle.IsChecked = _app.SetStartup(StartupToggle.IsChecked == true);
    }

    private void OnNotificationsToggle(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            _app.SetNotifications(NotificationsToggle.IsChecked == true);
    }

    private void OnDelayKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitDelay();
            Keyboard.ClearFocus();
        }
    }

    private void OnDelayCommit(object sender, RoutedEventArgs e) => CommitDelay();

    private void CommitDelay()
    {
        if (int.TryParse(DelayBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds is >= 3 and <= 60)
            _app.SetNotificationDelay(seconds);
        DelayBox.Text = _app.Settings.NotificationDelaySeconds.ToString(CultureInfo.InvariantCulture);
    }

    private void OnManageIgnored(object sender, RoutedEventArgs e) => _app.OpenIgnoredApps();
}
