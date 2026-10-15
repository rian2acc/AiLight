using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AIlight.Views;

/// <summary>Ein Halt des Rundgangs: was hervorgehoben wird und was dazu gesagt wird.</summary>
public sealed record TourStop(string Key, string Title, string Text, Rect Target);

/// <summary>
/// Der Rundgang: der Bildschirm wird abgedunkelt und ein Loch wandert von
/// Element zu Element — die Leiste selbst bleibt dabei sichtbar und unangetastet.
///
/// Das Vorbild ist das iOS-Rahmenwerk LRSpotlight
/// (<see href="https://github.com/lekshmiraveendranath/Spotlight"/>, MIT): eine
/// Fläche mit einer Aussparung, gezeichnet als Loch in einer Abdunklung
/// (<c>FillRule=EvenOdd</c>), und zwischen zwei Halten wird genau <b>eine</b>
/// Eigenschaft bewegt — das Rechteck der Aussparung.
///
/// Genau darin liegt die Ruhe: kein Element wird neu angeordnet, nichts skaliert,
/// nichts blendet gleichzeitig. Die Karte mit dem Text steht nach jedem Schritt
/// sofort an ihrer Stelle; sie bewegt sich nicht mit. Ein einziger Übergang, eine
/// Kurve (<see cref="Motion.InOut"/>), dieselbe Dauer wie ein Seitenwechsel.
/// </summary>
public static class Spotlight
{
    /// <summary>Rand zwischen hervorgehobenem Element und Loch.</summary>
    public const double Padding = 10;

    /// <summary>Kleinste Kantenlänge des Lochs — ein 1-px-Strich allein wäre kein Blickfang.</summary>
    public const double MinSize = 46;

    /// <summary>Abstand zwischen Loch und Karte.</summary>
    public const double Gap = 14;

    /// <summary>Feste Breite der Karte, damit der Text nicht bei jedem Schritt umbricht.</summary>
    public const double CardWidth = 330;

    /// <summary>Wie stark abgedunkelt wird (0 = gar nicht, 1 = schwarz).</summary>
    public const double Dim = 0.62;

    /// <summary>Abstand, den die Karte vom Bildrand hält.</summary>
    public const double Margin = 12;

    /// <summary>
    /// Ein Ziel von Bildschirmkoordinaten in die Ebene holen. Die Ebene liegt
    /// über einem Rechteck — auf einem zweiten Bildschirm beginnt dieses
    /// Rechteck nicht bei null, und genau daran verschiebt sich sonst alles.
    /// </summary>
    public static Rect InLayer(Rect target, Rect bounds)
        => new(target.Left - bounds.Left, target.Top - bounds.Top, target.Width, target.Height);

    /// <summary>
    /// Das Loch um ein Ziel herum: mit Rand, mindestens <see cref="MinSize"/>
    /// groß und auf ganze Pixel gelegt. Ganze Pixel sind kein Schönheitsfehler,
    /// sondern der Grund, warum die Kante beim Gleiten nicht flimmert.
    /// </summary>
    public static Rect Hole(Rect target)
    {
        var width = Math.Max(MinSize, target.Width + (2 * Padding));
        var height = Math.Max(MinSize, target.Height + (2 * Padding));

        // Immer um die Mitte des Ziels herum wachsen, auch wenn die Mindestgröße greift.
        var centerX = target.Left + (target.Width / 2);
        var centerY = target.Top + (target.Height / 2);
        var left = centerX - (width / 2);
        var top = centerY - (height / 2);

        left = Math.Floor(left);
        top = Math.Floor(top);
        return new Rect(left, top, Math.Ceiling(width), Math.Ceiling(height));
    }

    /// <summary>
    /// Wohin die Karte gehört: unter das Loch, wenn dort Platz ist, sonst
    /// darüber. Seitlich bleibt sie im Bild — Vorbild ist dieselbe Regel wie im
    /// iOS-Rahmenwerk („animate the info box around if it intersects“), nur
    /// ruhiger: sie springt einmal, statt während der Bewegung mitzulaufen.
    /// </summary>
    public static Rect PlaceCard(Rect hole, Size card, Rect bounds)
    {
        double top;
        var below = hole.Bottom + Gap;
        if (below + card.Height <= bounds.Bottom - Margin)
        {
            top = below;
        }
        else
        {
            var above = hole.Top - Gap - card.Height;
            top = above >= bounds.Top + Margin
                ? above
                : Math.Max(bounds.Top + Margin, bounds.Bottom - Margin - card.Height);
        }

        var highest = Math.Max(bounds.Left + Margin, bounds.Right - Margin - card.Width);
        var left = Math.Clamp(hole.Left, bounds.Left + Margin, Math.Max(bounds.Left + Margin, highest));
        return new Rect(Math.Floor(left), Math.Floor(top), card.Width, card.Height);
    }
}

/// <summary>
/// Die Ebene über allem: Abdunklung mit Loch, dazu die Karte. Sie ist ein
/// eigenes Fenster über den ganzen Bildschirm und endet per Klick, Enter oder Esc.
/// </summary>
public sealed class SpotlightOverlay : Window
{
    private readonly IReadOnlyList<TourStop> _stops;
    private readonly Rect _bounds;
    private readonly RectangleGeometry _hole = new();
    private readonly Canvas _canvas = new();
    private readonly Border _card = new();
    private readonly TextBlock _title;
    private readonly TextBlock _text;
    private readonly TextBlock _count;
    private readonly Action _finished;

    private DispatcherTimer? _bye;
    private int _index = -1;
    private bool _closing;

    public SpotlightOverlay(Rect bounds, IReadOnlyList<TourStop> stops, Action? finished = null)
    {
        _bounds = bounds;
        _stops = stops;
        _finished = finished ?? (() => { });

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height)));
        group.Children.Add(_hole);

        // Die Abdunklung selbst: eine Fläche mit Loch. Sie ist nicht anklickbar —
        // Klicks gehören der Ebene, damit ein Klick überall weiterblättert.
        var dim = new System.Windows.Shapes.Path
        {
            Data = group,
            Fill = new SolidColorBrush(Color.FromRgb(5, 7, 11)),
            Opacity = Spotlight.Dim,
            IsHitTestVisible = false,
        };

        _count = new TextBlock { FontSize = 11, Opacity = 0.7 };
        _title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _text = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Opacity = 0.86, Margin = new Thickness(0, 5, 0, 0) };
        var footer = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.62,
            Margin = new Thickness(0, 10, 0, 0),
            Text = "Klick oder → weiter · Esc beendet",
        };

        var lines = new StackPanel();
        lines.Children.Add(_count);
        lines.Children.Add(_title);
        lines.Children.Add(_text);
        lines.Children.Add(footer);

        _card.Width = Spotlight.CardWidth;
        _card.Padding = new Thickness(16, 13, 16, 13);
        _card.CornerRadius = new CornerRadius(12);
        _card.Child = lines;
        _card.SetResourceReference(Border.BackgroundProperty, "Brush.Code");
        _card.SetResourceReference(Border.BorderBrushProperty, "Brush.Separator");
        _card.BorderThickness = new Thickness(1);
        _card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 26,
            ShadowDepth = 6,
            Opacity = 0.45,
        };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        footer.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");

        // Ein durchsichtiger Hintergrund macht die Ebene anklickbar: ohne ihn
        // gingen Klicks an das Fenster darunter.
        _canvas.Width = bounds.Width;
        _canvas.Height = bounds.Height;
        _canvas.Background = Brushes.Transparent;
        _canvas.Children.Add(dim);
        _canvas.Children.Add(_card);
        Content = _canvas;

        PreviewKeyDown += OnKey;
        MouseLeftButtonUp += (_, _) => Next();
        Closed += (_, _) =>
        {
            _bye?.Stop();
            _finished();
        };
    }

    /// <summary>Der Halt, der gerade gezeigt wird (0-basiert).</summary>
    public int Index => _index;

    /// <summary>Der aktuelle Stand des Lochs — für den Prüfstand.</summary>
    public Rect HoleRect => _hole.Rect;

    /// <summary>Die Halte dieses Rundgangs.</summary>
    public IReadOnlyList<TourStop> Stops => _stops;

    /// <summary>Deckkraft der ganzen Ebene (Auftritt und Abgang) — für den Prüfstand.</summary>
    internal double RootOpacity => _canvas.Opacity;

    /// <summary>Der Aufbau ohne Fenster — für Bilder, die Ebene und Leiste zusammen zeigen.</summary>
    internal FrameworkElement Layer => _canvas;

    /// <summary>
    /// Ein Bild des Rundgangs über einem Bild der Leiste: zwei Fenster lassen
    /// sich nicht in ein Bild bringen, deshalb wird hier zusammengesetzt, was
    /// sonst übereinander liegt. Der Ausschnitt ist ein Stück Bildschirm um die
    /// Leiste herum — so bleibt das Bild klein und die Wirkung dieselbe.
    /// </summary>
    public static void SaveShot(
        string path, string barImagePath, Rect barOnScreen, Rect crop, IReadOnlyList<TourStop> stops, int index)
    {
        var overlay = new SpotlightOverlay(crop, stops);
        var layer = overlay.Layer;
        overlay.Content = null;
        overlay.Step(index);

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(System.IO.Path.GetFullPath(barImagePath), UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();

        var stage = new Grid
        {
            Width = crop.Width,
            Height = crop.Height,
            Background = new SolidColorBrush(Color.FromRgb(13, 15, 20)),
        };
        stage.Children.Add(new Image
        {
            Source = image,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(barOnScreen.Left - crop.Left, barOnScreen.Top - crop.Top, 0, 0),
        });
        stage.Children.Add(layer);
        Shot.SaveElement(stage, path);
    }

    /// <summary>Die Karte, wie sie gerade steht (Platz und Breite; Höhe, sobald gemessen).</summary>
    public Rect CardRect => new(
        Canvas.GetLeft(_card),
        Canvas.GetTop(_card),
        _card.Width,
        _card.ActualHeight > 0.5 ? _card.ActualHeight : MeasureCard().Height);

    /// <summary>Größe der Karte, gemessen statt geraten.</summary>
    public Size CardSize => MeasureCard();

    internal void NextForTest() => Next();

    internal void BackForTest() => Back();

    /// <summary>
    /// Den Rundgang beginnen: die Ebene erscheint weich, der erste Halt steht
    /// sofort (ohne Wanderung) — eine Bewegung ohne Weg ist keine.
    /// </summary>
    public void Begin()
    {
        Anim.FadeNow(_canvas, 0);
        Anim.Fade(_canvas, 1, Motion.ShowMs, Motion.Soft);
        Step(0);
    }

    /// <summary>
    /// Einen Halt zeigen. Beim ersten steht das Loch still, danach wandert es
    /// auf der Hauskurve zum nächsten Ziel — und die Karte springt einmal an ihre
    /// neue Stelle, ohne selbst zu wandern.
    /// </summary>
    public void Step(int index)
    {
        if (_stops.Count == 0)
        {
            Close();
            return;
        }

        index = Math.Clamp(index, 0, _stops.Count - 1);
        var first = _index < 0;
        _index = index;

        var stop = _stops[index];
        _count.Text = $"Schritt {index + 1} von {_stops.Count}";
        _title.Text = stop.Title;
        _text.Text = stop.Text;

        var hole = Spotlight.Hole(Spotlight.InLayer(stop.Target, _bounds));
        var from = _hole.Rect;
        _hole.Rect = hole;

        if (!first && Motion.Moves && IsVisible)
        {
            // Ein einziger Übergang: das Rechteck der Aussparung. Sonst nichts.
            _hole.BeginAnimation(
                RectangleGeometry.RectProperty,
                new RectAnimation(from, hole, Motion.After(Motion.PageMs))
                {
                    EasingFunction = Motion.InOut,
                });
        }
        else if (!first)
        {
            _hole.BeginAnimation(RectangleGeometry.RectProperty, null);
        }

        PlaceCard(hole);
    }

    /// <summary>Weiter. Am Ende schließt sich der Rundgang.</summary>
    public void Next()
    {
        if (_index >= _stops.Count - 1)
        {
            Finish();
            return;
        }

        Step(_index + 1);
    }

    /// <summary>Zurück — im ersten Halt bleibt er stehen.</summary>
    public void Back()
    {
        if (_index > 0)
        {
            Step(_index - 1);
        }
    }

    /// <summary>
    /// Schluss: das Ganzes geht weich weg, dann erst schließt das Fenster — sonst
    /// verschwände die Abdunklung mit einem Schlag.
    /// </summary>
    public void Finish()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        if (!Motion.Moves || !IsVisible)
        {
            Close();
            return;
        }

        Anim.Fade(_canvas, 0, Motion.HideMs, Motion.In);

        // Geschlossen wird über einen Zeitgeber, nicht über den Abschluss der
        // Animation: der Zeitgeber hängt allein am Nachrichtenumlauf und zieht
        // auch dann durch, wenn gerade niemand zeichnet (dasselbe Vorgehen wie
        // beim Ausblenden der Leiste).
        _bye ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(Motion.HideMs + 30),
        };
        _bye.Stop();
        _bye.Tick -= OnBye;
        _bye.Tick += OnBye;
        _bye.Start();
    }

    private void OnBye(object? sender, EventArgs e)
    {
        _bye?.Stop();
        Close();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Finish();
                break;
            case Key.Enter:
            case Key.Space:
            case Key.Right:
            case Key.Down:
                Next();
                break;
            case Key.Left:
            case Key.Up:
                Back();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void PlaceCard(Rect hole)
    {
        var size = MeasureCard();
        var place = Spotlight.PlaceCard(hole, size, new Rect(0, 0, _bounds.Width, _bounds.Height));
        Canvas.SetLeft(_card, place.Left);
        Canvas.SetTop(_card, place.Top);
        _card.Width = place.Width;
    }

    /// <summary>
    /// Die Karte messen, ohne dass sie schon im Bild stehen muss. Ohne Fenster
    /// gibt es kein gezeichnetes Bild, aber messen lässt sich trotzdem.
    /// </summary>
    private Size MeasureCard()
    {
        _card.Measure(new Size(Spotlight.CardWidth, double.PositiveInfinity));
        var height = _card.DesiredSize.Height;
        if (height <= 0.5)
        {
            // Noch nie angeordnet: eine brauchbare Höhe annehmen, damit die Karte
            // nicht am Bildrand klebt, bevor sie das erste Mal gezeichnet wurde.
            height = 118;
        }

        return new Size(Spotlight.CardWidth, Math.Ceiling(height));
    }
}
