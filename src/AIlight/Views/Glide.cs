using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIlight.Views;

/// <summary>
/// Weiches Scrollen: nichts springt, alles gleitet.
///
/// Zwei Fälle braucht die Leiste — und beide standen früher für einen harten
/// Sprung:
/// <list type="bullet">
///   <item><see cref="To"/> — zu einer Stelle gleiten (Auswahl mit den Pfeiltasten).</item>
///   <item><see cref="Chase"/> — einem Text nachziehen, der beim Schreiben wächst.</item>
/// </list>
///
/// Beides hängt jetzt am Zeichentakt von WPF (<c>CompositionTarget.Rendering</c>)
/// statt an einem Zeitgeber mit 16 ms: der Zeitgeber läuft neben dem Zeichnen her,
/// deshalb ruckelte das Nachziehen bei jedem zweiten Bild. Der Zeichentakt liefert
/// außerdem die Zeit des Bildes mit — daraus wird der Fortschritt gerechnet, nicht
/// aus einem Zähler. So sieht die Bewegung auf einem 60-Hz- und auf einem
/// 144-Hz-Bildschirm gleich aus, und sie hängt nicht mehr davon ab, wie lange die
/// letzte Anordnung gedauert hat.
/// </summary>
public sealed class Glide : IDisposable
{
    /// <summary>Zeitkonstante des Nachziehens: nach dieser Zeit ist der Restweg zu ~63 % weg.</summary>
    private const double ChaseTauMs = 120;

    private readonly ScrollViewer _view;
    private bool _hooked;
    private bool _chasing;
    private double _from;
    private double _target;
    private double _elapsed;
    private int _duration;
    private TimeSpan? _lastFrame;

    public Glide(ScrollViewer view) => _view = view;

    /// <summary>Zurzeit wird sanft nachgezogen (Antwort wächst gerade).</summary>
    public bool IsChasing => _chasing;

    /// <summary>Läuft gerade eine Bewegung?</summary>
    public bool IsRunning => _hooked;

    /// <summary>In fester Zeit an eine Stelle gleiten.</summary>
    public void To(double target, int durationMs = Motion.GlideMs)
    {
        var clamped = Clamp(target);
        var from = _view.VerticalOffset;
        _chasing = false;
        _target = clamped;
        _from = from;
        _elapsed = 0;
        // Dieselbe Lebendigkeit wie überall: die Einstellung wirkt auch hier.
        _duration = Math.Max(1, Motion.Scaled(durationMs));

        if (Math.Abs(clamped - from) < 0.5)
        {
            Stop();
            return;
        }

        if (!Motion.Moves)
        {
            Jump(clamped);
            return;
        }

        Hook();
    }

    /// <summary>
    /// Sanft ans Ende nachziehen. Wird während des Schreibens in kurzen Abständen
    /// aufgerufen; das Ziel wird dabei in jedem Bild neu bestimmt, damit der Text
    /// am unteren Rand bleibt, ohne zu ruckeln.
    /// </summary>
    public void Chase()
    {
        if (!Motion.Moves)
        {
            Jump(Clamp(_view.ScrollableHeight));
            return;
        }

        _chasing = true;
        Hook();
    }

    public void Stop()
    {
        Unhook();
        _chasing = false;
    }

    public void Dispose() => Unhook();

    private void Hook()
    {
        if (_hooked)
        {
            return;
        }

        _lastFrame = null;
        CompositionTarget.Rendering += OnFrame;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnFrame;
        _hooked = false;
        _lastFrame = null;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        // Die Zeit des Bildes, nicht die Uhrzeit: so passt der Fortschritt genau
        // zu dem, was gleich gezeichnet wird.
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var delta = _lastFrame is { } previous
            ? Math.Clamp((now - previous).TotalMilliseconds, 1, 64)
            : 16.0;
        _lastFrame = now;

        if (_chasing)
        {
            var current = _view.VerticalOffset;
            var target = Clamp(_view.ScrollableHeight);
            var difference = target - current;
            if (Math.Abs(difference) < 0.6)
            {
                // Angekommen: genau setzen und den Takt wieder abhängen.
                Jump(target);
                Unhook();
                _chasing = false;
                return;
            }

            // Ein Anteil des Restwegs, der von der Bilddauer abhängt: die Bewegung
            // sieht bei jedem Bildaufbau gleich aus, statt bei 144 Hz zu flitzen.
            var share = 1 - Math.Exp(-delta / ChaseTauMs);
            _view.ScrollToVerticalOffset(current + (difference * share));
            return;
        }

        _elapsed += delta;
        var progress = _elapsed / _duration;
        if (progress >= 1)
        {
            Jump(_target);
            Unhook();
            return;
        }

        _view.ScrollToVerticalOffset(Motion.Between(_from, _target, progress));
    }

    private void Jump(double offset) => _view.ScrollToVerticalOffset(Clamp(offset));

    private double Clamp(double offset) => Math.Clamp(offset, 0, Math.Max(0, _view.ScrollableHeight));
}
