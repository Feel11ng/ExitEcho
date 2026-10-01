using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class CompletionWindow : Window
{
    private readonly int _ended;
    private readonly int _listed;

    public CompletionWindow(int ended, int listed)
    {
        InitializeComponent();
        _ended = ended;
        _listed = listed;
        RefreshLocalization();
        Loc.LanguageChanged += RefreshLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= RefreshLocalization;
        SourceInitialized += (_, _) => ((App)System.Windows.Application.Current).ApplyWindowTheme(this);
        Loaded += (_, _) => AnimateOpen();
    }

    private void RefreshLocalization() =>
        ResultText.Text = Loc.Format("EndedMessage", _ended, _listed);

    private void AnimateOpen()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        { EasingFunction = ease });
        var translate = new TranslateTransform(0, 6);
        PanelRoot.RenderTransform = translate;
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = ease });
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
