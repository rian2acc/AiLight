using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace AIlight.Views;

/// <summary>
/// Setzt die Farben zur Laufzeit. Alle Fenster benutzen <c>DynamicResource</c>,
/// dadurch wirkt eine Änderung (Hell/Dunkel, Akzentfarbe) sofort überall.
/// </summary>
public static class Theme
{
    /// <summary>
    /// Eine vollständige Farbstimmung. Jeder Eintrag entspricht genau einem
    /// Pinsel in <c>Views/Theme.xaml</c> — so gibt es nur eine Quelle der Wahrheit.
    /// </summary>
    public sealed record Palette(
        string Surface,
        string Card,
        string CardBorder,
        string Border,
        string Hover,
        string Text,
        string Muted,
        string Selection,
        string Code,
        string Track,
        string Field,
        string FieldBorder,
        string Separator);

    private static readonly Palette Dark = new(
        Surface: "#F01E1F22",
        Card: "#0DFFFFFF",
        CardBorder: "#1FFFFFFF",
        Border: "#33FFFFFF",
        Hover: "#14FFFFFF",
        Text: "#FFE8EAED",
        Muted: "#FF9AA0A6",
        Selection: "#33FFFFFF",
        Code: "#59000000",
        Track: "#33FFFFFF",
        Field: "#59000000",
        FieldBorder: "#26FFFFFF",
        Separator: "#1AFFFFFF");

    private static readonly Palette Light = new(
        Surface: "#F5F3F3F3",
        Card: "#FFFFFFFF",
        CardBorder: "#1A000000",
        Border: "#22000000",
        Hover: "#0F000000",
        Text: "#FF1F1F1F",
        Muted: "#FF5F6368",
        Selection: "#22000000",
        Code: "#08000000",
        Track: "#33000000",
        Field: "#FFFFFFFF",
        FieldBorder: "#22000000",
        Separator: "#14000000");

    /// <summary>Wirklich benutzter Modus („system“ wird zu Hell oder Dunkel aufgelöst).</summary>
    public static bool IsDarkMode(string setting)
    {
        if (setting == "dunkel")
        {
            return true;
        }

        if (setting == "hell")
        {
            return false;
        }

        return !IsLightWindows();
    }

    private static bool IsLightWindows()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(string themeSetting, string accentHex, double opacity)
    {
        var dark = IsDarkMode(themeSetting);
        var palette = dark ? Dark : Light;
        var resources = Application.Current.Resources;
        var accent = Parse(accentHex) ?? Color.FromRgb(0x4C, 0x8D, 0xFF);

        Set(resources, "Brush.Surface", WithOpacity(palette.Surface, opacity));
        Set(resources, "Brush.Card", Parse(palette.Card) ?? Colors.Transparent);
        Set(resources, "Brush.CardBorder", Parse(palette.CardBorder) ?? Colors.Transparent);
        Set(resources, "Brush.Border", Parse(palette.Border) ?? Colors.Transparent);
        Set(resources, "Brush.Hover", Parse(palette.Hover) ?? Colors.Transparent);
        Set(resources, "Brush.Text", Parse(palette.Text) ?? Colors.White);
        Set(resources, "Brush.Muted", Parse(palette.Muted) ?? Colors.Gray);
        Set(resources, "Brush.Selection", Blend(accent, dark ? 0.20 : 0.14));
        Set(resources, "Brush.Code", Parse(palette.Code) ?? Colors.Transparent);
        Set(resources, "Brush.Track", Parse(palette.Track) ?? Colors.Transparent);
        Set(resources, "Brush.Field", Parse(palette.Field) ?? Colors.Transparent);
        Set(resources, "Brush.FieldBorder", Parse(palette.FieldBorder) ?? Colors.Transparent);
        Set(resources, "Brush.Separator", Parse(palette.Separator) ?? Colors.Transparent);
        Set(resources, "Brush.Ring", Blend(accent, 0.35));

        Set(resources, "Brush.Accent", accent);
        Set(resources, "Brush.Danger", Color.FromRgb(0xFF, 0x6B, 0x6B));
        Set(resources, "Brush.Shadow", dark ? Color.FromArgb(0x66, 0, 0, 0) : Color.FromArgb(0x33, 0, 0, 0));
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
        => resources[key] = new SolidColorBrush(color);

    /// <summary>Akzent mit Alpha — für Auswahl und Ringe.</summary>
    private static Color Blend(Color accent, double alpha)
        => Color.FromArgb((byte)(alpha * 255), accent.R, accent.G, accent.B);

    /// <summary>Farbe aus <c>#AARRGGBB</c>/<c>#RRGGBB</c>; die Deckkraft-Einstellung ersetzt Alpha.</summary>
    private static Color WithOpacity(string hex, double opacity)
    {
        var color = Parse(hex) ?? Color.FromRgb(0x1E, 0x1F, 0x22);
        if (opacity >= 1.0)
        {
            return color;
        }

        var alpha = (byte)Math.Clamp(opacity * color.A, 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    public static Color? Parse(string hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return null;
            }

            return (Color)ColorConverter.ConvertFromString(hex.Trim());
        }
        catch
        {
            return null;
        }
    }
}
