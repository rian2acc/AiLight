using System.Windows;
using System.Windows.Media.Animation;

namespace AIlight.Views;

/// <summary>
/// Bewegung an genau einer Stelle: wie lange etwas dauert, wie es sich anfühlt
/// und wie es gezeichnet wird.
///
/// Drei Dinge sind hier bewusst festgelegt:
/// <list type="bullet">
///   <item><b>Zeiten</b> — jede Bewegung hat einen Namen und einen Wert, statt als
///   Zahl im Aufbau zu stehen. Ein Detail darf kurz sein, ein Fensterwechsel
///   ruhiger; alles andere wäre Willkür.</item>
///   <item><b>Kurven</b> — <see cref="CubicBezierEase"/> mit den Stützpunkten, die
///   macOS benutzt. Etwas erscheint schnell und kommt dann langsam an; etwas
///   verschwindet umgekehrt. Das ist der Unterschied zwischen „weich“ und „hüpft“.</item>
///   <item><b>Systemschalter</b> — wer in Windows „Animationen“ abschaltet (oder in
///   AIlight die Bewegung), bekommt weiterhin alles, nur ohne Weg dorthin.</item>
/// </list>
/// </summary>
public static class Motion
{
    // ── Zeiten ──────────────────────────────────────────────────────────────

    /// <summary>Auftritt: die Leiste steigt einen Hauch auf und wird sichtbar.</summary>
    public const int ShowMs = 200;

    /// <summary>Abgang: kürzer als der Auftritt — sofort, aber nicht hart.</summary>
    public const int HideMs = 140;

    /// <summary>Kurze Rückmeldungen (Antwort erscheint, Zeile wird gewählt).</summary>
    public const int QuickMs = 140;

    /// <summary>Scrollen zu einer Stelle.</summary>
    public const int GlideMs = 260;

    /// <summary>Seitenwechsel in den Einstellungen und in der Einführung.</summary>
    public const int PageMs = 260;

    /// <summary>Die Vorschläge klappen weg, wenn gefragt wird.</summary>
    public const int CollapseMs = 220;

    /// <summary>Abstand zwischen zwei Zeilen, wenn eine Liste auftaucht.</summary>
    public const int StaggerMs = 26;

    /// <summary>Ein Absatz der Antwort blendet auf.</summary>
    public const int BlockMs = 320;

    /// <summary>Hover blendet ein.</summary>
    public const int HoverMs = 130;

    /// <summary>Hover blendet wieder aus — etwas ruhiger als hin.</summary>
    public const int LeaveMs = 220;

    /// <summary>Drücken eines Knopfes.</summary>
    public const int PressMs = 90;

    /// <summary>Ein halber Atemzug eines Punktes der Laufanzeige.</summary>
    public const int DotMs = 420;

    // ── Maße ────────────────────────────────────────────────────────────────

    /// <summary>Wie weit eine neue Seite beim Erscheinen aufsteigt.</summary>
    public const double PageRise = 10;

    /// <summary>Wie weit die Leiste beim Erscheinen aufsteigt (WPF-Einheiten).</summary>
    public const double Rise = 10;

    /// <summary>Wie weit eine Vorschlagszeile beim Auftauchen aufsteigt.</summary>
    public const double RowRise = 6;

    /// <summary>Wie klein die Leiste beim Erscheinen beginnt (1.0 = unverändert).</summary>
    public const double Grow = 0.984;

    /// <summary>Wie weit die Leiste beim Verlassen zusammengeht.</summary>
    public const double Shrink = 0.992;

    public static readonly Duration ShowDuration = After(ShowMs);

    public static readonly Duration HideDuration = After(HideMs);

    // ── Kurven ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Hausgriff für alles, was erscheint: sofort da, dann langer Auslauf.
    /// Dieselbe Kurve benutzt macOS für Blätter und Fenster.
    /// </summary>
    public static IEasingFunction Soft { get; } = Frozen(new CubicBezierEase(0.32, 0.72, 0, 1));

    /// <summary>Ausklingend, weniger steil — für kleine Details und Hover.</summary>
    public static IEasingFunction Out { get; } = Frozen(new CubicBezierEase(0.22, 1, 0.36, 1));

    /// <summary>Anfahrend — für alles, was verschwindet.</summary>
    public static IEasingFunction In { get; } = Frozen(new CubicBezierEase(0.55, 0, 1, 0.45));

    /// <summary>Anfahren und ausklingen — für Bewegungen aus dem Stand.</summary>
    public static IEasingFunction InOut { get; } = Frozen(new CubicBezierEase(0.65, 0, 0.35, 1));

    /// <summary>Ein Hauch Überschwingen — nur für kurze Rückmeldungen.</summary>
    public static IEasingFunction Spring { get; } = Frozen(new BackEase
    {
        Amplitude = 0.24,
        EasingMode = EasingMode.EaseOut,
    });

    /// <summary>
    /// Darf sich überhaupt etwas bewegen? Die Einstellungen der Leiste und der
    /// Systemschalter von Windows („Animationen im Clientbereich“) zählen beide.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Soll der Systemschalter mitzählen? (Standard: ja.)</summary>
    public static bool FollowSystem { get; set; } = true;

    /// <summary>
    /// Wie lebendig die Bewegung ist. 1 ist das Haus; darunter wird alles ruhiger
    /// (0,6 = fast nur noch ein Überblenden), darüber lebhafter. Die Einstellung
    /// liegt in den Einstellungen unter „Aussehen“.
    /// </summary>
    public static double Intensity { get; set; } = 1;

    /// <summary>
    /// Eine Hauszeit auf die eingestellte Lebendigkeit umrechnen. Alles läuft
    /// durch diese eine Stelle — deshalb wirkt die Einstellung überall gleich.
    /// </summary>
    public static int Scaled(int milliseconds)
        => Math.Abs(Intensity - 1) < 0.005
            ? milliseconds
            : (int)Math.Round(Math.Clamp(milliseconds * Intensity, 40, 2000));

    /// <summary>Meldet Windows, dass Animationen erwünscht sind?</summary>
    public static bool SystemAllows
    {
        get
        {
            try
            {
                return SystemParameters.ClientAreaAnimation;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>Darf jetzt bewegt werden? (Einstellung, System und Zeitgeber zusammen.)</summary>
    public static bool Moves => Enabled && (!FollowSystem || SystemAllows);

    /// <summary>Eine Dauer in WPF-Zeit — mit der eingestellten Lebendigkeit.</summary>
    public static Duration After(int milliseconds) => new(TimeSpan.FromMilliseconds(Scaled(milliseconds)));

    /// <summary>Ein Ein-/Ausblenden mit der Hauskurve.</summary>
    public static DoubleAnimation Fade(double from, double to, int milliseconds, IEasingFunction? easing = null)
        => new(from, to, After(milliseconds)) { EasingFunction = easing ?? Soft };

    /// <summary>Eine Zahl von A nach B, mit Kurve und optionaler Verzögerung.</summary>
    public static DoubleAnimation Tween(
        double from, double to, int milliseconds, IEasingFunction? easing = null, int delayMs = 0)
    {
        var animation = new DoubleAnimation(from, to, After(milliseconds)) { EasingFunction = easing ?? Soft };
        if (delayMs > 0)
        {
            animation.BeginTime = TimeSpan.FromMilliseconds(delayMs);
        }

        return animation;
    }

    /// <summary>
    /// Fortschritt der Hauskurve (0…1). Der Prüfstand rechnet damit nach, dass
    /// die Bewegung wirklich weich ist und nicht linear oder rückwärts läuft.
    /// </summary>
    public static double Ease(double progress) => HouseCurve.Value(progress);

    /// <summary>Zwischen zwei Werten entlang der Hauskurve.</summary>
    public static double Between(double from, double to, double progress)
        => from + ((to - from) * Ease(progress));

    /// <summary>
    /// Dieselbe Kurve noch einmal als reine Funktion: <see cref="Soft"/> ist eine
    /// eingefrorene WPF-Kurve, hier steht die Rechnung für Prüfstand und Scrollen.
    /// </summary>
    private static readonly CubicBezierEase HouseCurve = new(0.32, 0.72, 0, 1);

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
