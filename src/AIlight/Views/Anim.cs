using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AIlight.Views;

/// <summary>
/// Die wenigen Bewegungen, die in mehr als einem Fenster vorkommen — an einer
/// Stelle zusammengefasst und immer gleich aufgeräumt.
///
/// Zwei Regeln gelten hier für alles:
/// <list type="number">
///   <item><b>Keine Bewegung ohne sichtbares Fenster.</b> Steht das Element gar
///   nicht auf dem Bildschirm (Bilderzeugung, Prüfstand, versteckte Leiste),
///   wird der Endzustand sofort gesetzt statt animiert. Vorher liefen die
///   Animationen trotzdem — und weil ohne Zeichnen kein Bild vergeht, sah eine
///   Vorschlagszeile dann halb durchsichtig aus, auf einem Bildschirmfoto ganz
///   unsichtbar.</item>
///   <item><b>Nach jeder Bewegung wird aufgeräumt.</b> Eine WPF-Animation bleibt
///   sonst auf ihrem Endwert stehen (<c>FillBehavior.HoldEnd</c>) und
///   überschreibt jede spätere Änderung derselben Eigenschaft — genau daher kam
///   das klebrige Gefühl: halb durchsichtige Bereiche, feste Höhen, Zeilen, die
///   sich nicht mehr bewegten.</item>
/// </list>
/// </summary>
public static class Anim
{
    /// <summary>Darf sich am Ende überhaupt etwas bewegen? (Einstellung + Windows.)</summary>
    public static bool On => Motion.Moves;

    /// <summary>Läuft hier wirklich Bewegung — also auch sichtbar?</summary>
    public static bool Live(UIElement element)
        => Motion.Moves && (element as FrameworkElement)?.IsVisible != false;

    /// <summary>Ein- oder ausblenden.</summary>
    public static void Fade(UIElement element, double to, int milliseconds, IEasingFunction? easing = null, int delayMs = 0)
    {
        if (!Live(element))
        {
            FadeNow(element, to);
            return;
        }

        // Der Grundwert ist der Startwert (0 beim Einblenden, 1 beim Ausblenden).
        // Erst dadurch lässt sich eine Bewegung verzögern: während der Verzögerung
        // steht der Startzustand, und danach läuft sie zum Zielwert.
        var from = 1 - to;
        element.Opacity = from;
        element.BeginAnimation(
            UIElement.OpacityProperty,
            Motion.Tween(from, to, milliseconds, easing, delayMs));
    }

    /// <summary>Setzt die Deckkraft ohne Bewegung.</summary>
    public static void FadeNow(UIElement element, double value)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = value;
    }

    /// <summary>
    /// Vertikal bewegen. Die Verschiebung sitzt in einem eigenen
    /// <see cref="TranslateTransform"/>: sie kostet kein neues Messen und
    /// Verschieben, sondern läuft beim Zeichnen.
    /// </summary>
    public static void Slide(FrameworkElement element, double fromY, double toY, int milliseconds,
        IEasingFunction? easing = null, int delayMs = 0)
    {
        if (!Live(element))
        {
            SlideNow(element, toY);
            return;
        }

        var shift = EnsureShift(element);
        shift.Y = toY;
        shift.BeginAnimation(
            TranslateTransform.YProperty,
            Motion.Tween(fromY, toY, milliseconds, easing, delayMs));
    }

    /// <summary>Setzt die Verschiebung ohne Bewegung.</summary>
    public static void SlideNow(FrameworkElement element, double value)
    {
        var shift = EnsureShift(element);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        shift.Y = value;
    }

    /// <summary>Kurz größer werden und zurück — eine Rückmeldung auf ein Ereignis.</summary>
    public static void Pulse(FrameworkElement element, double scale, int milliseconds, IEasingFunction? easing = null)
    {
        if (!Live(element))
        {
            return;
        }

        var transform = EnsureScale(element);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transform.ScaleX = 1;
        transform.ScaleY = 1;

        var half = Math.Max(40, milliseconds / 2);
        var ease = easing ?? Motion.Spring;
        var grow = Motion.Tween(1, scale, half, ease);
        grow.FillBehavior = FillBehavior.Stop;
        var shrink = Motion.Tween(scale, 1, milliseconds, Motion.Out);
        shrink.BeginTime = TimeSpan.FromMilliseconds(half);
        shrink.FillBehavior = FillBehavior.Stop;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, shrink, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, shrink, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Einen Bereich zuklappen: von der gemessenen Höhe auf null, dazu ein
    /// kurzes Zurückweichen. Danach ist er weg — und hinterlässt keine feste Höhe.
    /// </summary>
    public static void Collapse(FrameworkElement element, int milliseconds, Action? done = null)
    {
        if (element.Visibility != Visibility.Visible)
        {
            done?.Invoke();
            return;
        }

        var from = element.ActualHeight;
        if (!Live(element) || from <= 0.5)
        {
            Close(element);
            done?.Invoke();
            return;
        }

        element.Height = from;
        var animation = Motion.Tween(from, 0, milliseconds, Motion.In);
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += (_, _) => Close(element);
        element.BeginAnimation(FrameworkElement.HeightProperty, animation);
        Fade(element, 0, milliseconds, Motion.In);
        Slide(element, 0, -6, milliseconds, Motion.In);
        done?.Invoke();
    }

    /// <summary>
    /// Einen Bereich aufklappen: von null auf seine natürliche Höhe. Danach wird
    /// die feste Höhe wieder freigegeben, damit der Inhalt wachsen darf.
    /// </summary>
    public static void Expand(FrameworkElement element, int milliseconds)
    {
        if (!On)
        {
            FadeNow(element, 1);
            SlideNow(element, 0);
            element.Visibility = Visibility.Visible;
            element.BeginAnimation(FrameworkElement.HeightProperty, null);
            element.Height = double.NaN;
            return;
        }

        element.Visibility = Visibility.Visible;
        FadeNow(element, 0);
        SlideNow(element, 0);

        // Natürliche Höhe messen: feste Höhe wegnehmen und neu anordnen lassen.
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.Height = double.NaN;
        var target = element.ActualHeight;
        if (target <= 0.5)
        {
            element.Measure(new Size(element.ActualWidth, double.PositiveInfinity));
            target = element.DesiredSize.Height;
        }

        if (!Live(element) || target <= 0.5)
        {
            FadeNow(element, 1);
            element.Height = double.NaN;
            return;
        }

        element.Height = target;
        var animation = Motion.Tween(0, target, milliseconds, Motion.Soft);
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += (_, _) =>
        {
            // Danach gehört die Höhe wieder dem Inhalt.
            element.BeginAnimation(FrameworkElement.HeightProperty, null);
            element.Height = double.NaN;
        };
        element.BeginAnimation(FrameworkElement.HeightProperty, animation);
        Fade(element, 1, milliseconds, Motion.Soft);
        Slide(element, 6, 0, milliseconds, Motion.Soft);
    }

    /// <summary>
    /// Eine Liste auftauchen lassen: jede Zeile folgt der vorigen mit einem
    /// kleinen Abstand. Das ist der Unterschied zwischen „da“ und „kommt herein“.
    /// </summary>
    public static void Stagger(IReadOnlyList<UIElement> items, int stepMs = Motion.StaggerMs, int milliseconds = Motion.BlockMs)
    {
        var live = items.Count > 0 && Live(items[0]);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var move = item as FrameworkElement;
            if (!live)
            {
                FadeNow(item, 1);
                if (move is not null)
                {
                    SlideNow(move, 0);
                }

                continue;
            }

            FadeNow(item, 0);
            if (move is not null)
            {
                SlideNow(move, Motion.RowRise);
                Slide(move, Motion.RowRise, 0, milliseconds, Motion.Soft, index * stepMs);
            }

            Fade(item, 1, milliseconds, Motion.Soft, index * stepMs);
        }
    }

    /// <summary>Alle Bewegungen eines Elements abräumen und es in Grundstellung setzen.</summary>
    public static void Reset(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.Opacity = 1;
        if (element.RenderTransform is TranslateTransform shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        }
    }

    private static void Close(FrameworkElement element)
    {
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.Height = double.NaN;
        element.Visibility = Visibility.Collapsed;
        FadeNow(element, 1);
        SlideNow(element, 0);
    }

    private static TranslateTransform EnsureShift(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform existing)
        {
            return existing;
        }

        // Eine Skalierung darf nicht verloren gehen: sie liegt dann mit in der Gruppe.
        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform fromGroup)
                {
                    return fromGroup;
                }
            }

            var shift = new TranslateTransform();
            group.Children.Add(shift);
            return shift;
        }

        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }

    private static ScaleTransform EnsureScale(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform existing)
        {
            return existing;
        }

        var scale = new ScaleTransform(1, 1);
        element.RenderTransform = scale;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return scale;
    }
}
