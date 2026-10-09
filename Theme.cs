using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ScreenSearchOverlay;

// Colors of the Prompt Builder and the ChatGPT window, in two themes.
//
// Like Loc: Apply() publishes the current table as an application resource
// dictionary, so every {DynamicResource key} in XAML follows a theme change
// live. The overlay keeps its own dark look: it sits over any screen.
//
// Light is deliberately soft: warm greys, no pure white surface, text a
// little short of black — comfortable for long sessions, not glaring.
// Night Dev: the dark greys, orange actions, fuchsia focus and selection.
// VS Code: the editor's Dark Modern surfaces and text, orange actions, its
// link blue for focus.
internal static class Theme
{
    internal const string Dark = "dark";
    internal const string Light = "light";
    internal const string NightDev = "nightdev";
    internal const string VsCode = "vscode";
    internal static readonly string[] Names = [Dark, Light, NightDev, VsCode];

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        [Dark] = new()
        {
            ["WindowBg"] = "#1E1E1E",
            ["ChromeBg"] = "#181818",
            ["SidebarBg"] = "#191919",
            ["Line"] = "#2C2C2C",
            ["OuterBorder"] = "#3A3A3A",
            ["InputBg"] = "#232324",
            ["InputBorder"] = "#363636",
            ["InputBorderHover"] = "#4A4A4A",
            ["PopupBg"] = "#252526",
            ["PopupBorder"] = "#3A3A3A",
            ["Accent"] = "#2563EB",
            ["AccentHover"] = "#3B82F6",
            ["AccentPressed"] = "#1D4ED8",
            ["AccentSoft"] = "#60A5FA",
            ["LinkHover"] = "#93C5FD",
            ["TextPrimary"] = "#E8E8E8",
            ["TextSecondary"] = "#B4B4B4",
            ["TextMuted"] = "#8A8A8A",
            ["Placeholder"] = "#6F6F6F",
            ["HoverBg"] = "#262626",
            ["SelectedBg"] = "#2A2F38",
            ["MenuHoverBg"] = "#30343B",
            ["Danger"] = "#F47174",
            ["ScrollThumb"] = "#5A5A5A",
            ["ScrollThumbHover"] = "#787878",
            ["ScrollThumbDrag"] = "#9A9A9A",
            ["CheckBorder"] = "#5C5C5C",
            ["StrongBorder"] = "#8A8A8A",
            ["KeyCapBg"] = "#242424",
            ["KeyCapBorder"] = "#383838",
            ["PillBg"] = "#1A2B45",
            ["PillBorder"] = "#2F4E7E",
            ["PillText"] = "#BFDBFE",
            ["ChatPageBg"] = "#212121",
            ["StatusInfo"] = "#93C5FD",
            ["StatusSuccess"] = "#4ADE80",
            ["StatusWarning"] = "#FACC15",
            ["StatusError"] = "#F47174",
        },
        [Light] = new()
        {
            ["WindowBg"] = "#EDEDEA",
            ["ChromeBg"] = "#E3E3DF",
            ["SidebarBg"] = "#E6E6E2",
            ["Line"] = "#D3D3CD",
            ["OuterBorder"] = "#C4C4BD",
            ["InputBg"] = "#F6F6F4",
            ["InputBorder"] = "#CBCBC4",
            ["InputBorderHover"] = "#AEAEA7",
            ["PopupBg"] = "#F3F3F0",
            ["PopupBorder"] = "#CBCBC4",
            ["Accent"] = "#2563EB",
            ["AccentHover"] = "#3B82F6",
            ["AccentPressed"] = "#1D4ED8",
            ["AccentSoft"] = "#2F64D6",
            ["LinkHover"] = "#1D4ED8",
            ["TextPrimary"] = "#2A2B2E",
            ["TextSecondary"] = "#4C4D52",
            ["TextMuted"] = "#707177",
            ["Placeholder"] = "#94959A",
            ["HoverBg"] = "#DCDCD7",
            ["SelectedBg"] = "#D6DEEB",
            ["MenuHoverBg"] = "#D6DEEB",
            ["Danger"] = "#C93B3B",
            ["ScrollThumb"] = "#BDBDB6",
            ["ScrollThumbHover"] = "#A2A29B",
            ["ScrollThumbDrag"] = "#88887F",
            ["CheckBorder"] = "#9E9E97",
            ["StrongBorder"] = "#707177",
            ["KeyCapBg"] = "#E0E0DB",
            ["KeyCapBorder"] = "#C9C9C2",
            ["PillBg"] = "#DCE6F8",
            ["PillBorder"] = "#A8BFEA",
            ["PillText"] = "#1E4FB8",
            ["ChatPageBg"] = "#FFFFFF",
            ["StatusInfo"] = "#2F64D6",
            ["StatusSuccess"] = "#15803D",
            ["StatusWarning"] = "#B45309",
            ["StatusError"] = "#C93B3B",
        },
        [NightDev] = new()
        {
            ["WindowBg"] = "#1E1E1E",
            ["ChromeBg"] = "#181818",
            ["SidebarBg"] = "#191919",
            ["Line"] = "#2C2C2C",
            ["OuterBorder"] = "#3A3A3A",
            ["InputBg"] = "#232324",
            ["InputBorder"] = "#363636",
            ["InputBorderHover"] = "#4A4A4A",
            ["PopupBg"] = "#252526",
            ["PopupBorder"] = "#3A3A3A",
            ["Accent"] = "#EA580C",
            ["AccentHover"] = "#F97316",
            ["AccentPressed"] = "#C2410C",
            ["AccentSoft"] = "#E879F9",
            ["LinkHover"] = "#F0ABFC",
            ["TextPrimary"] = "#E8E8E8",
            ["TextSecondary"] = "#B4B4B4",
            ["TextMuted"] = "#8A8A8A",
            ["Placeholder"] = "#6F6F6F",
            ["HoverBg"] = "#262626",
            ["SelectedBg"] = "#30262F",
            ["MenuHoverBg"] = "#372B36",
            ["Danger"] = "#FB7185",
            ["ScrollThumb"] = "#5A5A5A",
            ["ScrollThumbHover"] = "#787878",
            ["ScrollThumbDrag"] = "#9A9A9A",
            ["CheckBorder"] = "#5C5C5C",
            ["StrongBorder"] = "#8A8A8A",
            ["KeyCapBg"] = "#242424",
            ["KeyCapBorder"] = "#383838",
            ["PillBg"] = "#3B1F12",
            ["PillBorder"] = "#7C3A16",
            ["PillText"] = "#FDBA74",
            ["ChatPageBg"] = "#212121",
            ["StatusInfo"] = "#F0ABFC",
            ["StatusSuccess"] = "#4ADE80",
            ["StatusWarning"] = "#FDBA74",
            ["StatusError"] = "#FB7185",
        },
        [VsCode] = new()
        {
            ["WindowBg"] = "#1F1F1F",
            ["ChromeBg"] = "#181818",
            ["SidebarBg"] = "#181818",
            ["Line"] = "#2B2B2B",
            ["OuterBorder"] = "#2B2B2B",
            ["InputBg"] = "#313131",
            ["InputBorder"] = "#3C3C3C",
            ["InputBorderHover"] = "#4F4F4F",
            ["PopupBg"] = "#202020",
            ["PopupBorder"] = "#454545",
            ["Accent"] = "#E8651A",
            ["AccentHover"] = "#F57C33",
            ["AccentPressed"] = "#C9540F",
            ["AccentSoft"] = "#4DAAFC",
            ["LinkHover"] = "#7CC0FD",
            ["TextPrimary"] = "#CCCCCC",
            ["TextSecondary"] = "#ADADAD",
            ["TextMuted"] = "#8B8B8B",
            ["Placeholder"] = "#6E6E6E",
            ["HoverBg"] = "#2A2D2E",
            ["SelectedBg"] = "#37373D",
            ["MenuHoverBg"] = "#04395E",
            ["Danger"] = "#F14C4C",
            ["ScrollThumb"] = "#434343",
            ["ScrollThumbHover"] = "#575757",
            ["ScrollThumbDrag"] = "#6B6B6B",
            ["CheckBorder"] = "#6B6B6B",
            ["StrongBorder"] = "#8B8B8B",
            ["KeyCapBg"] = "#2B2B2B",
            ["KeyCapBorder"] = "#3C3C3C",
            ["PillBg"] = "#3A2414",
            ["PillBorder"] = "#7A3E17",
            ["PillText"] = "#FFB27A",
            ["ChatPageBg"] = "#212121",
            ["StatusInfo"] = "#4DAAFC",
            ["StatusSuccess"] = "#89D185",
            ["StatusWarning"] = "#CCA700",
            ["StatusError"] = "#F14C4C",
        },
    };

    // Title palette, picked for the dark list: on the light one each color
    // shows as a deeper shade of itself. prompts.json keeps the dark value.
    private static readonly Dictionary<string, string> LightTitleColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#F87171"] = "#DC2626",
        ["#FB923C"] = "#EA580C",
        ["#FACC15"] = "#B98A04",
        ["#4ADE80"] = "#16A34A",
        ["#2DD4BF"] = "#0D9488",
        ["#38BDF8"] = "#0284C7",
        ["#818CF8"] = "#4F46E5",
        ["#C084FC"] = "#9333EA",
        ["#F472B6"] = "#DB2777",
    };

    private static ResourceDictionary? _published;
    private static readonly Dictionary<string, SolidColorBrush> TitleBrushes = [];

    internal static string Current { get; private set; } = Dark;
    internal static bool IsLight => Current == Light;

    internal static event Action? Changed;

    internal static void Apply(string name)
    {
        if (!Tables.ContainsKey(name)) name = Dark;
        Current = name;
        TitleBrushes.Clear();

        var dictionary = new ResourceDictionary();
        foreach (var (key, hex) in Tables[name])
            dictionary[key] = Frozen(Parse(hex));

        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (_published is not null) merged.Remove(_published);
        merged.Add(dictionary);
        _published = dictionary;

        Changed?.Invoke();
    }

    // For code: the current theme's brush or color.
    internal static SolidColorBrush Brush(string key) => (SolidColorBrush)_published![key];
    internal static Color ColorOf(string key) => Brush(key).Color;

    // "#RRGGBB", for CSS.
    internal static string Hex(string key)
    {
        var c = ColorOf(key);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    // A prompt's title color as shown in this theme; null for an unknown or
    // malformed value (a hand-edited prompts.json).
    internal static SolidColorBrush? TitleBrush(string hex)
    {
        if (TitleBrushes.TryGetValue(hex, out var brush)) return brush;
        var shown = IsLight && LightTitleColors.TryGetValue(hex, out var deeper) ? deeper : hex;
        try { brush = Frozen(Parse(shown)); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException) { return null; }
        return TitleBrushes[hex] = brush;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
