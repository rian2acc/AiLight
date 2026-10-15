using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace AIlight.Views;

/// <summary>
/// Setzt die Farben, Eckenradien und die Schrift zur Laufzeit. Alle Fenster
/// benutzen <c>DynamicResource</c>, dadurch wirkt eine Änderung (Hell/Dunkel,
/// Akzentfarbe, Radius, Schrift) sofort überall.
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

    /// <summary>Ruheflächen im macOS-Dunkel: Fenster, Karten, Eingaben klar getrennt.</summary>
    private static readonly Palette Dark = new(
        Surface: "#F02A2A2C",
        Card: "#14FFFFFF",
        CardBorder: "#26FFFFFF",
        Border: "#33FFFFFF",
        Hover: "#1AFFFFFF",
        Text: "#F5F5F7",
        Muted: "#98989D",
        Selection: "#33FFFFFF",
        Code: "#80000000",
        Track: "#40FFFFFF",
        Field: "#66000000",
        FieldBorder: "#2EFFFFFF",
        Separator: "#22FFFFFF");

    /// <summary>Helles Fenster (#F2F2F7) mit weißen Karten und Haarlinien.</summary>
    private static readonly Palette Light = new(
        Surface: "#F0F2F2F7",
        Card: "#FFFFFFFF",
        CardBorder: "#1F000000",
        Border: "#22000000",
        Hover: "#0A000000",
        Text: "#FF1D1D1F",
        Muted: "#FF6E6E73",
        Selection: "#22000000",
        Code: "#0D000000",
        Track: "#33000000",
        Field: "#FFFFFFFF",
        FieldBorder: "#33000000",
        Separator: "#1F000000");

    /// <summary>Der Standardakzent: das Blau von macOS.</summary>
    public const string DefaultAccent = "#0A84FF";

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

    /// <summary>
    /// Schriftauswahl aus den Einstellungen. Immer ein sinnvoller Rückfall:
    /// die Systemschrift ist auf Windows 11 „Segoe UI Variable“.
    /// </summary>
    public static FontFamily FontStack(string key) => key switch
    {
        "segoe" => new FontFamily("Segoe UI"),
        "calibri" => new FontFamily("Calibri"),
        "arial" => new FontFamily("Arial"),
        _ => new FontFamily("Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI"),
    };

    public static bool IsKnownFont(string key)
        => key is "system" or "segoe" or "calibri" or "arial";

    public static void Apply(string themeSetting, string accentHex, double opacity,
        double radius = 16, string font = "system", bool compactRows = false)
    {
        var dark = IsDarkMode(themeSetting);
        var palette = dark ? Dark : Light;
        var resources = Application.Current.Resources;
        var accent = Parse(accentHex) ?? Parse(DefaultAccent) ?? Color.FromRgb(0x0A, 0x84, 0xFF);

        Set(resources, "Brush.Surface", WithOpacity(palette.Surface, opacity));
        Set(resources, "Brush.Card", Parse(palette.Card) ?? Colors.Transparent);
        Set(resources, "Brush.CardBorder", Parse(palette.CardBorder) ?? Colors.Transparent);
        Set(resources, "Brush.Border", Parse(palette.Border) ?? Colors.Transparent);
        Set(resources, "Brush.Hover", Parse(palette.Hover) ?? Colors.Transparent);
        Set(resources, "Brush.Text", Parse(palette.Text) ?? Colors.White);
        Set(resources, "Brush.Muted", Parse(palette.Muted) ?? Colors.Gray);
        Set(resources, "Brush.Selection", Blend(accent, dark ? 0.22 : 0.16));
        Set(resources, "Brush.Code", Parse(palette.Code) ?? Colors.Transparent);
        Set(resources, "Brush.Track", Parse(palette.Track) ?? Colors.Transparent);
        Set(resources, "Brush.Field", Parse(palette.Field) ?? Colors.Transparent);
        Set(resources, "Brush.FieldBorder", Parse(palette.FieldBorder) ?? Colors.Transparent);
        Set(resources, "Brush.Separator", Parse(palette.Separator) ?? Colors.Transparent);
        Set(resources, "Brush.Ring", Blend(accent, 0.35));

        Set(resources, "Brush.Accent", accent);
        Set(resources, "Brush.Danger", Color.FromRgb(0xFF, 0x45, 0x3A));
        Set(resources, "Brush.Shadow", dark ? Color.FromArgb(0x66, 0, 0, 0) : Color.FromArgb(0x33, 0, 0, 0));

        ApplyRadius(resources, radius);
        resources["Font.Ui"] = FontStack(font);

        // Höhe einer Zeile der Vorschlagsliste: die Einstellung „kompakte Zeilen“
        // wirkt damit auf jede Zeile, ohne dass die Vorlage sie kennen muss.
        resources["Suggest.Row.Height"] = compactRows ? 32d : 40d;
    }

    /// <summary>
    /// Eckenradien aus einem Wert ableiten: das Fenster bekommt den vollen
    /// Radius, Karten, Felder und Zeilen etwas weniger — so wirkt alles wie aus
    /// einem Guss (dieselbe Staffelung benutzt macOS).
    /// </summary>
    public static void ApplyRadius(ResourceDictionary resources, double radius)
    {
        var r = Math.Clamp(radius, 8, 22);
        resources["Radius.Shell"] = new CornerRadius(r);
        resources["Radius.Card"] = new CornerRadius(Math.Max(6, r - 4));
        resources["Radius.Field"] = new CornerRadius(Math.Max(5, r - 6));
        resources["Radius.Row"] = new CornerRadius(Math.Max(6, r - 5));
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
        => resources[key] = new SolidColorBrush(color);

    /// <summary>Akzent mit Alpha — für Auswahl und Ringe.</summary>
    private static Color Blend(Color accent, double alpha)
        => Color.FromArgb((byte)(alpha * 255), accent.R, accent.G, accent.B);

    /// <summary>Farbe aus <c>#AARRGGBB</c>/<c>#RRGGBB</c>; die Deckkraft-Einstellung ersetzt Alpha.</summary>
    private static Color WithOpacity(string hex, double opacity)
    {
        var color = Parse(hex) ?? Color.FromRgb(0x2A, 0x2A, 0x2C);
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
