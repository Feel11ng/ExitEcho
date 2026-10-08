using System.Text.Json;
using ExitEcho.App;
using ExitEcho.Core;

var directory = Path.Combine(Path.GetTempPath(), "ExitEchoIgnoreTests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "ignored.json");
    File.WriteAllText(path, "[\"Chrome\",\"chrome\",\"Discord\"]");
    var legacy = IgnoreStore.Load(path);
    Check(legacy.Count == 2 && legacy.All(rule => !rule.IsProcess), "legacy app rules");
    IgnoreStore.Save(path, legacy.Append(new IgnoredRule("Chrome", @"C:\Apps\Chrome\chrome.exe", "worker")));
    var migrated = IgnoreStore.Load(path);
    Check(migrated.Count == 3 && migrated.Count(rule => !rule.IsProcess) == 2,
        "legacy app rules survive migration with a process rule");
    var oldPath = Path.Combine(directory, "old-ignored.json");
    File.WriteAllText(oldPath, "[\"LegacyApp\"]");
    File.Delete(path);
    Check(IgnoreStore.Load(path, oldPath).Single().AppName == "LegacyApp", "legacy path fallback");

    var rules = new[]
    {
        new IgnoredRule("Chrome"),
        new IgnoredRule("Chrome", @"C:\Apps\Chrome\chrome.exe", "worker"),
        new IgnoredRule("Discord", @"C:\Apps\Discord\Discord.exe", "worker")
    };
    IgnoreStore.Save(path, rules);
    var loaded = IgnoreStore.Load(path);
    Check(loaded.Count == 3, "new JSON roundtrip");
    Check(loaded.Any(rule => !rule.IsProcess && rule.AppName == "Chrome"), "app ignore preserved");

    var worker = new LeftoverProcess("worker", 21, 100, 50_000_000);
    var helper = new LeftoverProcess("helper", 22, 101, 30_000_000);
    var chrome = new LeftoverEvent("Chrome", [worker, helper], @"C:\Apps\Chrome\chrome.exe");
    var differentApp = new LeftoverEvent("Other", [worker], @"C:\Apps\Other\Other.exe");
    var sameNameDifferentPath = new LeftoverEvent("Chrome", [worker], @"D:\Portable\Chrome\chrome.exe");
    var filtered = IgnoreStore.Filter(chrome, [loaded[1]]);
    Check(filtered?.Processes.Count == 1 && filtered.Processes[0].Name == "helper", "only selected process removed");
    Check(IgnoreStore.Filter(differentApp, [loaded[1]])?.Processes.Count == 1, "same process name in other app");
    Check(IgnoreStore.Filter(sameNameDifferentPath, [loaded[1]])?.Processes.Count == 1,
        "same app name at different path");
    Check(IgnoreStore.Filter(chrome with { Processes = [worker] }, [loaded[1]]) is null,
        "all ignored suppresses empty notification");
    var nameOnly = new IgnoredRule("Chrome", null, "worker");
    Check(IgnoreStore.Filter(new LeftoverEvent("Chrome", [worker]), [nameOnly]) is null,
        "name-only rule applies to selected app");
    Check(IgnoreStore.Filter(new LeftoverEvent("Other", [worker]), [nameOnly])?.Processes.Count == 1,
        "name-only rule does not affect a different app name");
    Check(IgnoreStore.Filter(chrome with { Processes = [worker] }, [nameOnly])?.Processes.Count == 1,
        "name-only rule does not silently expand to known EXE paths");

    File.WriteAllText(path, "{bad json");
    try { IgnoreStore.Load(path); throw new Exception("invalid JSON accepted"); }
    catch (JsonException) { }
    Check(File.ReadAllText(path) == "{bad json", "invalid JSON kept unchanged");

    var blockedParent = Path.Combine(directory, "not-a-directory");
    File.WriteAllText(blockedParent, "keep");
    try { IgnoreStore.Save(Path.Combine(blockedParent, "ignored.json"), rules); throw new Exception("write error missed"); }
    catch (IOException) { }
    Check(File.ReadAllText(blockedParent) == "keep", "write error did not destroy data");

    // Older settings/history JSON omits properties added in later releases.
    var appAssembly = typeof(IgnoreStore).Assembly;
    var settingsType = appAssembly.GetType("ExitEcho.App.AppSettings", throwOnError: true)!;
    var settings = JsonSerializer.Deserialize("{\"Language\":\"en\",\"NotificationDelaySeconds\":8}", settingsType)!;
    Check((string)settingsType.GetProperty("Language")!.GetValue(settings)! == "en" &&
          (bool)settingsType.GetProperty("ShowNotifications")!.GetValue(settings)! &&
          (string)settingsType.GetProperty("Theme")!.GetValue(settings)! == "system",
        "legacy settings JSON defaults");
    var historyType = appAssembly.GetType("ExitEcho.App.HistoryEntry", throwOnError: true)!;
    var historyJson = "{\"Id\":\"7ee79a0b-9d18-420a-9ab8-74642a81b42e\",\"AppName\":\"LegacyApp\",\"DetectedAt\":\"2025-01-01T12:00:00+00:00\",\"ProcessCount\":1,\"TotalRamBytes\":1024,\"EndedViaExitEcho\":false}";
    var history = JsonSerializer.Deserialize(historyJson, historyType)!;
    Check((string)historyType.GetProperty("AppName")!.GetValue(history)! == "LegacyApp" &&
          !(bool)historyType.GetProperty("Ignored")!.GetValue(history)! &&
          historyType.GetProperty("ExecutablePath")!.GetValue(history) is null,
        "legacy history JSON defaults");

    Console.WriteLine("PASS: legacy settings/history/ignore JSON, roundtrip, scoped matching, empty result, read/write errors");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
}
