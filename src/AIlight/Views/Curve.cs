using System.Windows;
using System.Windows.Media.Animation;

namespace AIlight.Views;

/// <summary>
/// Eine kubische Bézier-Kurve als WPF-Kurve — dieselbe Rechnung, die auch eine
/// Website mit <c>cubic-bezier(...)</c> benutzt.
///
/// Warum überhaupt selbst rechnen: WPF bringt nur <c>CubicEase</c> mit, und die
/// ist deutlich zahmer als die Kurven, die macOS für Fenster und Listen benutzt
/// (steil los, langer weicher Auslauf). Genau dieser Auslauf ist es, der eine
/// Oberfläche „flüssig“ wirken lässt — mit einer zu flachen Kurve wirkt jede
/// Bewegung entweder träge oder abrupt.
///
/// Die Kurve ist eine reine Funktion: <see cref="Solve"/> bekommt den Fortschritt
/// und gibt dieselbe Zahl für dieselbe Eingabe zurück. Deshalb kann der Selbsttest
/// nachrechnen, dass sie bei 0 beginnt, bei 1 endet, nie zurückläuft und nie
/// überschwingt — statt sich auf ein gutes Gefühl auf einem Bildschirm zu verlassen.
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    /// <summary>Zwei Stützpunkte, wie bei <c>cubic-bezier(x1, y1, x2, y2)</c>.</summary>
    public double X1 { get; set; }

    public double Y1 { get; set; }

    public double X2 { get; set; }

    public double Y2 { get; set; }

    public CubicBezierEase()
    {
    }

    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;

        // Die Kurve rechnet sich selbst aus; die Grundklasse darf nichts mehr
        // umdeuten. EaseIn bedeutet dort „nimm EaseInCore unverändert“.
        EasingMode = EasingMode.EaseIn;
    }

    /// <summary>Der Wert der Kurve an der Stelle <paramref name="progress"/> (0 … 1).</summary>
    public double Value(double progress) => Solve(progress, X1, Y1, X2, Y2);

    protected override double EaseInCore(double normalizedTime) => Value(normalizedTime);

    protected override Freezable CreateInstanceCore()
        => new CubicBezierEase { X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2, EasingMode = EasingMode.EaseIn };

    /// <summary>
    /// Wert einer kubischen Bézier-Kurve mit den Endpunkten (0,0) und (1,1).
    ///
    /// Eine Bézier-Kurve ist über den Kurvenparameter t beschrieben, gesucht ist
    /// aber der Wert über dem Fortschritt x. Deshalb wird zuerst das t bestimmt,
    /// bei dem x(t) = Fortschritt gilt (Newton, mit Intervallhalbierung als
    /// Rückfall), und danach y(t) ausgerechnet. Genau so arbeiten Browser.
    /// </summary>
    public static double Solve(double progress, double x1, double y1, double x2, double y2)
    {
        var x = Math.Clamp(progress, 0, 1);
        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        var t = x;
        for (var step = 0; step < 8; step++)
        {
            var slope = Slope(t, x1, x2);
            if (Math.Abs(slope) < 1e-9)
            {
                break;
            }

            var error = Position(t, x1, x2) - x;
            if (Math.Abs(error) < 1e-7)
            {
                break;
            }

            t -= error / slope;
        }

        // Newton kann aus dem Intervall laufen — dann klassisch halbieren.
        if (t < 0 || t > 1)
        {
            double low = 0;
            double high = 1;
            t = x;
            for (var step = 0; step < 40; step++)
            {
                t = (low + high) / 2;
                var value = Position(t, x1, x2);
                if (Math.Abs(value - x) < 1e-7)
                {
                    break;
                }

                if (value < x)
                {
                    low = t;
                }
                else
                {
                    high = t;
                }
            }
        }

        return Position(t, y1, y2);
    }

    /// <summary>Kurvenpunkt (eine Achse) beim Kurvenparameter t.</summary>
    private static double Position(double t, double first, double second)
    {
        var rest = 1 - t;
        return (3 * rest * rest * t * first) + (3 * rest * t * t * second) + (t * t * t);
    }

    /// <summary>Ableitung von <see cref="Position"/> nach t.</summary>
    private static double Slope(double t, double first, double second)
    {
        var rest = 1 - t;
        return (3 * rest * rest * first)
            + (6 * rest * t * (second - first))
            + (3 * t * t * (1 - second));
    }
}
