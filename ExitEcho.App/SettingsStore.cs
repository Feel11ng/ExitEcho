using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using ExitEcho.App.Localization;

namespace ExitEcho.App;

internal sealed class AppSettings
{
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "system";
    public bool StartWithWindows { get; set; }
    public bool ShowNotifications { get; set; } = true;
    public int NotificationDelaySeconds { get; set; } = 8;
}

internal static class SettingsStore
{
    private const string StartupName = "ExitEcho";
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExitEcho", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            settings.Language = Loc.IsSupported(settings.Language) ? settings.Language : "system";
            settings.Theme = settings.Theme is "dark" or "light" ? settings.Theme : "system";
            settings.NotificationDelaySeconds = Math.Clamp(settings.NotificationDelaySeconds, 3, 60);
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings));
        File.Move(temp, FilePath, true);
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey)
            ?? throw new IOException("Windows startup registry key is unavailable.");
        if (enabled)
            key.SetValue(StartupName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(StartupName, throwOnMissingValue: false);
    }

    public static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
        return key?.GetValue(StartupName) is string value &&
            string.Equals(value, $"\"{Environment.ProcessPath}\"", StringComparison.OrdinalIgnoreCase);
    }
}
