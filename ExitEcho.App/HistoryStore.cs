using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using ExitEcho.Core;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

internal sealed record HistoryEntry(
    Guid Id,
    string AppName,
    DateTimeOffset DetectedAt,
    int ProcessCount,
    long TotalRamBytes,
    bool EndedViaExitEcho)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutablePath { get; init; }

    [JsonIgnore]
    public BitmapSource? IconSource => AppIconCache.Get(ExecutablePath);

    [JsonIgnore]
    public string DayLabel => DetectedAt.LocalDateTime.Date == DateTime.Today ? Loc.Get("Today")
        : DetectedAt.LocalDateTime.Date == DateTime.Today.AddDays(-1) ? Loc.Get("Yesterday")
        : DetectedAt.LocalDateTime.ToString("D", Loc.Culture);

    [JsonIgnore]
    public string TimeLabel => DetectedAt.LocalDateTime.ToString("t", Loc.Culture);
    [JsonIgnore]
    public string ProcessCountText => Loc.Format(Loc.PluralKey("Process", ProcessCount), ProcessCount);
    [JsonIgnore]
    public string RamText => Loc.Format("RamValue", Math.Ceiling(TotalRamBytes / 1_000_000d));
    [JsonIgnore]
    public string EndStatusText => Loc.Get(EndedViaExitEcho ? "EndedBy" : "LeftRunning");
}

internal sealed class HistoryStore
{
    private const int MaxEntries = 200;
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExitEcho", "history.json");

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public HistoryStore()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            var saved = (JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? [])
                .OrderByDescending(item => item.DetectedAt).ToArray();
            foreach (var entry in saved.Take(MaxEntries))
                Entries.Add(entry);
            if (saved.Length > MaxEntries)
                Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"ExitEcho history could not be loaded: {exception.Message}");
        }
    }

    public Guid Record(LeftoverEvent leftover)
    {
        var entry = new HistoryEntry(Guid.NewGuid(), leftover.AppName, DateTimeOffset.Now,
            leftover.Processes.Count, leftover.Processes.Sum(process => process.WorkingSetBytes), false)
        { ExecutablePath = leftover.ExecutablePath };
        Entries.Insert(0, entry);
        Save();
        return entry.Id;
    }

    public void MarkEnded(Guid id)
    {
        for (var index = 0; index < Entries.Count; index++)
        {
            if (Entries[index].Id != id || Entries[index].EndedViaExitEcho)
                continue;
            Entries[index] = Entries[index] with { EndedViaExitEcho = true };
            Save();
            return;
        }
    }

    private void Save()
    {
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Entries));
            File.Move(temp, FilePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"ExitEcho history could not be saved: {exception.Message}");
        }
    }
}
