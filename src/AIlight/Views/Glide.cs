using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
/// Beides läuft über einen eigenen Zeitgeber mit Bildfrequenz; die Deckkraft
/// bleibt dabei unberührt, es wird nur die Scrollposition gesetzt.
/// </summary>
public sealed class Glide
{
    private readonly ScrollViewer _view;
    private readonly DispatcherTimer _timer;

    private double _from;
    private double _target;
    private DateTime _started;
    private int _duration;

    public Glide(ScrollViewer view)
    {
        _view = view;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _timer.Tick += OnTick;
    }

    /// <summary>Zurzeit wird sanft nachgezogen (Antwort wächst gerade).</summary>
    public bool IsChasing { get; private set; }

    /// <summary>In fester Zeit an eine Stelle gleiten.</summary>
    public void To(double target, int durationMs = Motion.GlideMs)
    {
        _target = Clamp(target);
        _from = _view.VerticalOffset;
        _started = DateTime.Now;
        _duration = Math.Max(1, durationMs);
        IsChasing = false;

        if (Math.Abs(_target - _from) < 0.5)
        {
            _timer.Stop();
            return;
        }

        _timer.Start();
    }

    /// <summary>
    /// Sanft ans Ende nachziehen. Wird während des Schreibens in kurzen
    /// Abständen aufgerufen; das Ziel wird dabei jedes Mal neu bestimmt, damit
    /// der Text am unteren Rand bleibt, ohne zu ruckeln.
    /// </summary>
    public void Chase()
    {
        IsChasing = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        IsChasing = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (IsChasing)
        {
            var current = _view.VerticalOffset;
            var difference = Clamp(_view.ScrollableHeight) - current;
            if (Math.Abs(difference) < 0.6)
            {
                Jump(Clamp(_view.ScrollableHeight));
                Stop();
                return;
            }

            // Ein fester Anteil des Restwegs je Bild: die Bewegung ist damit
            // weich und bleibt trotzdem dicht am Textende.
            _view.ScrollToVerticalOffset(current + (difference * 0.34));
            return;
        }

        var progress = (DateTime.Now - _started).TotalMilliseconds / _duration;
        if (progress >= 1)
        {
            Jump(_target);
            _timer.Stop();
            return;
        }

        _view.ScrollToVerticalOffset(Motion.Between(_from, _target, progress));
    }

    private void Jump(double offset) => _view.ScrollToVerticalOffset(Clamp(offset));

    private double Clamp(double offset) => Math.Clamp(offset, 0, Math.Max(0, _view.ScrollableHeight));
}
