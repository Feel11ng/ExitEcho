using System.Windows;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class ConfirmationWindow : Window
{
    private readonly string _appName;
    private readonly int _processCount;

    public ConfirmationWindow(string appName, int processCount)
    {
        InitializeComponent();
        _appName = appName;
        _processCount = processCount;
        RefreshLocalization();
        Loc.LanguageChanged += RefreshLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= RefreshLocalization;
        SourceInitialized += (_, _) => ((App)System.Windows.Application.Current).ApplyWindowTheme(this);
    }

    private void RefreshLocalization() => MessageText.Text = Loc.Format(
        Loc.PluralKey("ConfirmationMessage", _processCount), _processCount, _appName);

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
