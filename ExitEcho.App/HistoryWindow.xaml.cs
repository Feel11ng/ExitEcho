using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

public partial class HistoryWindow : Window
{
    private int _animatedCards;

    internal HistoryWindow(ObservableCollection<HistoryEntry> entries)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ((App)System.Windows.Application.Current).ApplyWindowTheme(this);

        void RefreshLocalization()
        {
            var view = new ListCollectionView(entries);
            view.SortDescriptions.Add(new SortDescription(nameof(HistoryEntry.DetectedAt), ListSortDirection.Descending));
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(HistoryEntry.DayLabel)));
            HistoryList.ItemsSource = view;
        }
        RefreshLocalization();
        Loc.LanguageChanged += RefreshLocalization;

        void UpdateEmptyState() => EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState();
        NotifyCollectionChangedEventHandler onChanged = (_, _) => UpdateEmptyState();
        entries.CollectionChanged += onChanged;
        Closed += (_, _) => { entries.CollectionChanged -= onChanged; Loc.LanguageChanged -= RefreshLocalization; };
    }

    private void OnHistoryCardLoaded(object sender, RoutedEventArgs e)
    {
        var card = (Border)sender;
        card.RenderTransform = new TranslateTransform(0, 5);
        var delay = TimeSpan.FromMilliseconds(45 + Math.Min(_animatedCards++, 5) * 70);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        { BeginTime = delay, EasingFunction = ease });
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = delay, EasingFunction = ease });
    }
}
