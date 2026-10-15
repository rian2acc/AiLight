using System.Windows;
using System.Windows.Media.Animation;

namespace AIlight.Views;

/// <summary>
/// Bewegung an genau einer Stelle: wie lange etwas dauert, wie es sich anfühlt
/// und wie es gezeichnet wird. Die Leiste ist kurz da und soll auch kurz
/// verschwinden — deshalb stehen alle Zeiten und Kurven hier beisammen, statt
/// über die Fenster verstreut zu sein.
///
/// Die Kurve ist kubisch ausklingend: schnell los, sanft ankommen. Das ist das
/// Verhalten, das eine Spotlight-artige Leiste lebendig wirken lässt — ein
/// gleichförmiges Einblenden wirkt dagegen träge.
/// </summary>
public static class Motion
{
    /// <summary>Auftritt: die Leiste steigt einen Hauch auf und wird sichtbar.</summary>
    public const int ShowMs = 180;

    /// <summary>Abgang: kürzer als der Auftritt — sofort, aber nicht hart.</summary>
    public const int HideMs = 130;

    /// <summary>Kurze Rückmeldungen (Antwort erscheint, Zeile wird gewählt).</summary>
    public const int QuickMs = 120;

    /// <summary>Scrollen zu einer Stelle.</summary>
    public const int GlideMs = 200;

    /// <summary>Seitenwechsel in den Einstellungen — etwas ruhiger als ein Detail.</summary>
    public const int PageMs = 220;

    /// <summary>Wie weit eine neue Seite beim Erscheinen aufsteigt.</summary>
    public const double PageRise = 8;

    /// <summary>Wie weit die Leiste beim Erscheinen aufsteigt (WPF-Einheiten).</summary>
    public const double Rise = 10;

    /// <summary>Wie klein die Leiste beim Erscheinen beginnt (1.0 = unverändert).</summary>
    public const double Grow = 0.988;

    /// <summary>Einen Hauch größer als <see cref="Rise"/>, aber nicht aus dem Bild heraus.</summary>
    public static readonly Duration ShowDuration = After(ShowMs);

    public static readonly Duration HideDuration = After(HideMs);

    /// <summary>Ausklingend — für alles, was erscheint.</summary>
    public static IEasingFunction Out { get; } = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Anfahrend — für alles, was verschwindet.</summary>
    public static IEasingFunction In { get; } = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });

    /// <summary>Anfahren und ausklingen — für Bewegungen aus dem Stand.</summary>
    public static IEasingFunction InOut { get; } = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>Eine Dauer in WPF-Zeit.</summary>
    public static Duration After(int milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>Ein Ein-/Ausblenden mit der Hauskurve.</summary>
    public static DoubleAnimation Fade(double from, double to, int milliseconds, IEasingFunction? easing = null)
        => new(from, to, After(milliseconds)) { EasingFunction = easing ?? Out };

    /// <summary>
    /// Fortschritt der Hauskurve (0…1). Der Prüfstand rechnet damit nach, dass
    /// die Bewegung wirklich weich ist und nicht linear oder rückwärts läuft.
    /// </summary>
    public static double Ease(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var rest = 1 - t;
        return 1 - (rest * rest * rest);
    }

    /// <summary>Zwischen zwei Werten entlang der Hauskurve.</summary>
    public static double Between(double from, double to, double progress)
        => from + ((to - from) * Ease(progress));

    /// <summary>WPF-Kurven sind einfrierbare Objekte — eingefroren sind sie threadfest.</summary>
    private static IEasingFunction Frozen(IEasingFunction easing)
    {
        if (easing is Freezable freezable && freezable.CanFreeze)
        {
            freezable.Freeze();
        }

        return easing;
    }
}
