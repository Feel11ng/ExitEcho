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
    private readonly ObservableCollection<HistoryEntry> _displayEntries;

    internal HistoryWindow(ObservableCollection<HistoryEntry> entries)
    {
        InitializeComponent();
        _displayEntries = new ObservableCollection<HistoryEntry>(entries);
        SourceInitialized += (_, _) => ((App)System.Windows.Application.Current).ApplyWindowTheme(this);

        void RefreshLocalization()
        {
            var view = new ListCollectionView(_displayEntries);
            view.SortDescriptions.Add(new SortDescription(nameof(HistoryEntry.DetectedAt), ListSortDirection.Descending));
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(HistoryEntry.DayLabel)));
            HistoryList.ItemsSource = view;
        }
        RefreshLocalization();
        Loc.LanguageChanged += RefreshLocalization;

        void UpdateEmptyState() => EmptyState.Visibility = _displayEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState();
        _displayEntries.CollectionChanged += (_, _) => UpdateEmptyState();
        NotifyCollectionChangedEventHandler onChanged = (_, change) =>
        {
            if (change.Action == NotifyCollectionChangedAction.Add && change.NewItems is not null)
                foreach (HistoryEntry entry in change.NewItems) _displayEntries.Add(entry);
            else if (change.Action == NotifyCollectionChangedAction.Remove && change.OldItems is not null)
                foreach (HistoryEntry entry in change.OldItems) RemoveEntry(entry);
            else if (change.Action == NotifyCollectionChangedAction.Replace && change.OldItems is not null && change.NewItems is not null)
            {
                for (var index = 0; index < change.OldItems.Count; index++)
                {
                    var oldEntry = (HistoryEntry)change.OldItems[index]!;
                    var position = _displayEntries.IndexOf(oldEntry);
                    if (position >= 0) _displayEntries[position] = (HistoryEntry)change.NewItems[index]!;
                }
            }
            else if (change.Action == NotifyCollectionChangedAction.Reset)
            {
                _displayEntries.Clear();
                foreach (var entry in entries) _displayEntries.Add(entry);
            }
        };
        entries.CollectionChanged += onChanged;
        Closed += (_, _) => { entries.CollectionChanged -= onChanged; Loc.LanguageChanged -= RefreshLocalization; };
    }

    private void RemoveEntry(HistoryEntry entry)
    {
        if (!SystemParameters.ClientAreaAnimation || FindCard(HistoryList, entry.Id) is not { } card)
        {
            _displayEntries.Remove(entry);
            return;
        }
        var height = card.ActualHeight;
        card.Height = height;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => _displayEntries.Remove(entry);
        card.BeginAnimation(OpacityProperty, fade);
        if (card.RenderTransform is TranslateTransform offset)
            offset.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(180)));
        card.BeginAnimation(HeightProperty, new DoubleAnimation(height, 0, TimeSpan.FromMilliseconds(180)));
    }

    private static Border? FindCard(DependencyObject root, Guid id)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Border { Name: "EventCard", DataContext: HistoryEntry entry } card && entry.Id == id)
                return card;
            if (FindCard(child, id) is { } found) return found;
        }
        return null;
    }

    private void OnHistoryCardLoaded(object sender, RoutedEventArgs e)
    {
        var card = (Border)sender;
        if (!SystemParameters.ClientAreaAnimation)
        {
            card.Opacity = 1;
            card.RenderTransform = new TranslateTransform();
            return;
        }
        card.RenderTransform = new TranslateTransform(0, 4);
        var delay = TimeSpan.FromMilliseconds(Math.Min(_animatedCards++, 5) * 40);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        { BeginTime = delay, EasingFunction = ease });
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(200)) { BeginTime = delay, EasingFunction = ease });
    }
}
