using System.IO;
using System.Text.Json;
using ExitEcho.App.Localization;
using ExitEcho.Core;

namespace ExitEcho.App;

public sealed record IgnoredRule(string AppName, string? ExecutablePath = null, string? ProcessName = null)
{
    public bool IsProcess => ProcessName is not null;

    public bool Matches(LeftoverEvent leftover, LeftoverProcess process) =>
        IsProcess &&
        string.Equals(AppName, leftover.AppName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ProcessName, process.Name, StringComparison.OrdinalIgnoreCase) &&
        (ExecutablePath is null
            ? leftover.ExecutablePath is null
            : string.Equals(ExecutablePath, leftover.ExecutablePath, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => IsProcess
        ? Loc.Format("IgnoredProcessRule", ProcessName!, AppName)
        : Loc.Format("IgnoredAppRule", AppName);
}

public static class IgnoreStore
{
    private sealed record Document(string[] Apps, IgnoredRule[] Processes);

    public static IReadOnlyList<IgnoredRule> Load(string path, string? legacyPath = null)
    {
        var source = File.Exists(path) ? path : legacyPath;
        if (source is null || !File.Exists(source))
            return [];
        using var document = JsonDocument.Parse(File.ReadAllText(source));
        var rules = new List<IgnoredRule>();
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in JsonSerializer.Deserialize<string[]>(document.RootElement.GetRawText()) ?? [])
                if (!string.IsNullOrWhiteSpace(name))
                    rules.Add(new IgnoredRule(name));
        }
        else
        {
            var data = JsonSerializer.Deserialize<Document>(document.RootElement.GetRawText())
                ?? throw new JsonException("Invalid ignored rules document.");
            foreach (var name in data.Apps ?? [])
                if (!string.IsNullOrWhiteSpace(name))
                    rules.Add(new IgnoredRule(name));
            foreach (var rule in data.Processes ?? [])
                if (!string.IsNullOrWhiteSpace(rule.AppName) && !string.IsNullOrWhiteSpace(rule.ProcessName))
                    rules.Add(new IgnoredRule(rule.AppName, rule.ExecutablePath, rule.ProcessName));
        }
        return rules.DistinctBy(rule => RuleKey(rule), StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void Save(string path, IEnumerable<IgnoredRule> rules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var entries = rules.ToArray();
        var document = new Document(entries.Where(rule => !rule.IsProcess).Select(rule => rule.AppName).ToArray(),
            entries.Where(rule => rule.IsProcess).ToArray());
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(document));
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static LeftoverEvent? Filter(LeftoverEvent leftover, IEnumerable<IgnoredRule> rules)
    {
        var processRules = rules.Where(rule => rule.IsProcess).ToArray();
        var processes = leftover.Processes.Where(process => !processRules.Any(rule => rule.Matches(leftover, process)))
            .ToArray();
        return processes.Length == 0 ? null : leftover with { Processes = processes };
    }

    private static string RuleKey(IgnoredRule rule) => rule.IsProcess
        ? $"P\0{rule.AppName}\0{rule.ExecutablePath}\0{rule.ProcessName}"
        : $"A\0{rule.AppName}";
}
