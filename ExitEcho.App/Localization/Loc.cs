using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace ExitEcho.App.Localization;

internal static class Loc
{
    public static readonly (string Code, string NativeName)[] Languages =
    [
        ("en", "English"), ("ru", "Русский"), ("de", "Deutsch"), ("fr", "Français"),
        ("es", "Español"), ("pt-BR", "Português (Brasil)"), ("pt-PT", "Português"),
        ("it", "Italiano"), ("pl", "Polski"), ("uk", "Українська"), ("tr", "Türkçe"),
        ("nl", "Nederlands"), ("cs", "Čeština"), ("sv", "Svenska"), ("nb", "Norsk"),
        ("da", "Dansk"), ("fi", "Suomi"), ("zh-CN", "简体中文"), ("zh-TW", "繁體中文"),
        ("ja", "日本語"), ("ko", "한국어"), ("ar", "العربية"), ("he", "עברית"),
        ("hi", "हिन्दी"), ("id", "Bahasa Indonesia"), ("vi", "Tiếng Việt"), ("th", "ไทย")
    ];

    private static readonly HashSet<string> Codes = Languages.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(() => Load("en"));
    private static IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();

    public static string Language { get; private set; } = "system";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public static event Action? LanguageChanged;

    public static string Get(string key) =>
        _current.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : English.Value[key];

    public static string Format(string key, params object[] arguments) =>
        string.Format(Culture, Get(key), arguments);

    public static string PluralKey(string prefix, int count)
    {
        var n = Math.Abs((long)count);
        var form = Culture.TwoLetterISOLanguageName switch
        {
            "ar" => n switch
            {
                0 => "Zero", 1 => "One", 2 => "Two",
                _ when n % 100 is >= 3 and <= 10 => "Few",
                _ when n % 100 is >= 11 and <= 99 => "Many", _ => "Other"
            },
            "ru" or "uk" => n % 100 is >= 11 and <= 14 ? "Many" : (n % 10) switch
            {
                1 => "One", >= 2 and <= 4 => "Few", _ => "Many"
            },
            "pl" => n == 1 ? "One" : n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14)
                ? "Few" : "Many",
            "cs" => n == 1 ? "One" : n is >= 2 and <= 4 ? "Few" : "Many",
            "fr" => n is 0 or 1 ? "One" : "Many",
            _ => n == 1 ? "One" : "Many"
        };
        var key = prefix + form;
        return _current.ContainsKey(key) ? key : _current.ContainsKey(prefix + "Many") ? prefix + "Many" : prefix;
    }

    public static bool IsSupported(string language) => Codes.Contains(language);

    public static void SetLanguage(string language)
    {
        Language = IsSupported(language) ? Languages.First(x => x.Code.Equals(language, StringComparison.OrdinalIgnoreCase)).Code : "system";
        var resolved = Language == "system" ? ResolveSystemLanguage(CultureInfo.CurrentUICulture) : Language;
        Culture = CultureInfo.GetCultureInfo(resolved);
        _current = resolved == "en" ? English.Value
            : Cache.TryGetValue(resolved, out var strings) ? strings : Cache[resolved] = Load(resolved);

        if (System.Windows.Application.Current is { } app)
        {
            foreach (var (key, value) in English.Value)
                app.Resources["L_" + key] = Get(key);
            app.Resources["LocalFlowDirection"] = resolved is "ar" or "he"
                ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        }
        LanguageChanged?.Invoke();
    }

    private static string ResolveSystemLanguage(CultureInfo system)
    {
        if (Codes.Contains(system.Name))
            return Languages.First(x => x.Code.Equals(system.Name, StringComparison.OrdinalIgnoreCase)).Code;
        var language = system.TwoLetterISOLanguageName;
        if (language is "no" or "nn") return "nb";
        if (language == "pt") return system.Name.Contains("BR", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "pt-PT";
        if (language == "zh")
            return system.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                   system.Name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                   system.Name.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                   system.Name.Contains("MO", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
        return Languages.FirstOrDefault(x => x.Code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase) ||
                                             x.Code.Equals(language, StringComparison.OrdinalIgnoreCase)).Code ?? "en";
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        var uri = new Uri($"pack://application:,,,/Localization/strings.{language}.json");
        using var stream = System.Windows.Application.GetResourceStream(uri).Stream;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Missing {language} localization.");
    }
}
