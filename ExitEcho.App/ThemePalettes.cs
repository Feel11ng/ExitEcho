namespace ExitEcho.App;

internal sealed record ThemePalette(
    string Background, string Surface, string Text, string Muted, string Border,
    string Accent, string ButtonText, string Secondary, string Success,
    string Danger, string DangerButtonText, string Focus, string MainBackground,
    string EchoFrontFill, string Hover, string EchoFront, string EchoBack,
    string EchoMiddle, string CardHover, string NotificationTop, string NotificationBottom)
{
    internal IEnumerable<(string Key, string Color)> Brushes()
    {
        yield return ("BackgroundBrush", Background);
        yield return ("SurfaceBrush", Surface);
        yield return ("TextBrush", Text);
        yield return ("MutedBrush", Muted);
        yield return ("BorderBrush", Border);
        yield return ("AccentBrush", Accent);
        yield return ("ButtonTextBrush", ButtonText);
        yield return ("SecondaryBrush", Secondary);
        yield return ("SuccessBrush", Success);
        yield return ("DangerBrush", Danger);
        yield return ("DangerButtonTextBrush", DangerButtonText);
        yield return ("FocusBrush", Focus);
        yield return ("MainBackgroundBrush", MainBackground);
        yield return ("EchoFrontFillBrush", EchoFrontFill);
        yield return ("HoverBrush", Hover);
        yield return ("EchoFrontBrush", EchoFront);
        yield return ("EchoBackBrush", EchoBack);
        yield return ("EchoMiddleBrush", EchoMiddle);
        yield return ("CardHoverBrush", CardHover);
    }
}

internal static class ThemePalettes
{
    internal static readonly (string Id, string LabelKey)[] Options =
    [
        ("system", "ThemeSystem"), ("light", "ThemeLight"), ("dark", "ThemeDark"),
        ("oled", "ThemeOledBlack"), ("graphite", "ThemeGraphite"), ("midnight", "ThemeMidnight")
    ];

    internal static bool IsSupported(string? theme) => Options.Any(option => option.Id == theme);

    internal static ThemePalette Resolve(string theme, bool systemLight) => theme switch
    {
        "light" => Light,
        "dark" => Dark,
        "oled" => Oled,
        "graphite" => Graphite,
        "midnight" => Midnight,
        _ => systemLight ? Light : Dark
    };

    private static readonly ThemePalette Light = new(
        "#F5F7F8", "#FFFFFF", "#24343C", "#60737B", "#D8E1E4",
        "#397579", "#FFFFFF", "#EBF1F2", "#398365",
        "#B42318", "#FFFFFF", "#397579", "#ECF2F3",
        "#F7FAFA", "#E5ECEE", "#829CA3", "#365B65",
        "#5C7C84", "#F3F7F7", "#FFFFFF", "#F5F8F9");

    private static readonly ThemePalette Dark = new(
        "#151F26", "#202E36", "#F0F3F3", "#A7B8BB", "#34464E",
        "#84ADA7", "#14232A", "#24343C", "#78C6A2",
        "#FF7B72", "#14232A", "#84ADA7", "#1C2B33",
        "#2A3D46", "#2B3C44", "#D3E2E5", "#8FA2AA",
        "#B4C5CA", "#293B44", "#1E2B33", "#1A262E");

    private static readonly ThemePalette Oled = new(
        "#000000", "#111214", "#F5F7F8", "#A4AFB4", "#303439",
        "#83BAB5", "#091718", "#181A1C", "#82CDA7",
        "#FF9088", "#210E0C", "#A9D7D2", "#000000",
        "#1B2022", "#22262A", "#DBE6E6", "#687A80",
        "#9BACB1", "#1B1E21", "#111214", "#111214");

    private static readonly ThemePalette Graphite = new(
        "#191B1E", "#282B2F", "#F1F2F3", "#B0B5BA", "#44484D",
        "#92B8B3", "#102021", "#313439", "#8FCBAA",
        "#FF9690", "#291210", "#B4D2CE", "#191B1E",
        "#34383C", "#3A3E43", "#D9E2E2", "#74858A",
        "#A4B2B5", "#34383C", "#282B2F", "#282B2F");

    private static readonly ThemePalette Midnight = new(
        "#10192B", "#19263C", "#EDF2FA", "#ADBBD0", "#354560",
        "#92AFFF", "#0C1424", "#23324A", "#8BCDAC",
        "#FF958E", "#26100F", "#B0C3FF", "#0C1424",
        "#22324B", "#2A3A55", "#D6E2F6", "#61758F",
        "#91A6C4", "#253650", "#19263C", "#19263C");
}
