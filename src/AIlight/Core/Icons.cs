using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Brush = System.Drawing.Brush;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace AIlight.Core;

/// <summary>
/// Das Programmsymbol. Bevorzugt wird die mitgelieferte Datei
/// <c>ailight.ico</c>; fehlt sie, wird ein einfaches Zeichen gezeichnet —
/// so sieht das Programm nie „kapitutt“ aus.
/// </summary>
public static class Icons
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Load(int size = 32)
    {
        var path = Paths.IconFile;
        if (path.Length > 0)
        {
            try
            {
                return new Icon(path, new Size(size, size));
            }
            catch (Exception ex)
            {
                Log.Warn($"Symbol konnte nicht geladen werden: {ex.Message}");
            }
        }

        // Keine Datei daneben: das im Programm eingebettete Symbol benutzen —
        // dann sieht auch das Infobereich-Symbol überall gleich aus.
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                if (Icon.ExtractAssociatedIcon(exe) is { } embedded)
                {
                    using (embedded)
                    {
                        return new Icon(embedded, new Size(size, size));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Eingebettetes Symbol nicht nutzbar: {ex.Message}");
        }

        return Draw(size);
    }

    /// <summary>
    /// Notfallsymbol, falls die Datei fehlt: dieselbe Marke wie im Programmsymbol —
    /// abgerundete Fläche, darin ein Lichtstrahl, der aus einem Punkt aufgeht.
    /// </summary>
    public static Icon Draw(int size, string accent = Views.Theme.DefaultAccent)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var color = Views.Theme.Parse(accent) is { } parsed
                ? Color.FromArgb(parsed.R, parsed.G, parsed.B)
                : Color.FromArgb(0x0A, 0x84, 0xFF);

            // Fläche mit sanftem Verlauf (oben heller) — wie im gezeichneten Symbol.
            var inset = size * 0.055f;
            using var face = RoundedRectangle(inset, inset, size - (2 * inset), size - (2 * inset), size * 0.23f);
            using (var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                       new RectangleF(0, 0, size, size), Lighten(color, 0.35f), color, 90f))
            {
                graphics.FillPath(gradient, face);
            }

            // Alles Licht bleibt innerhalb der Fläche — sonst franst der Schein am
            // Rand des Symbols aus und wird hart abgeschnitten.
            var clip = graphics.Clip;
            graphics.SetClip(face);

            var centerX = size * 0.5f;
            var orbY = size * 0.80f;
            var orbRadius = size * 0.108f;

            // Strahl: unten schmal, nach oben offen, halbtransparent und nach oben verblassend.
            using (var beam = new GraphicsPath())
            {
                beam.AddPolygon(new[]
                {
                    new PointF(centerX - (size * 0.20f), size * 0.15f),
                    new PointF(centerX + (size * 0.20f), size * 0.15f),
                    new PointF(centerX + (size * 0.042f), orbY - (orbRadius * 0.55f)),
                    new PointF(centerX - (size * 0.042f), orbY - (orbRadius * 0.55f)),
                });

                using var fade = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new RectangleF(0, 0, size, size),
                    Color.FromArgb(80, 255, 255, 255),
                    Color.FromArgb(190, 255, 255, 255),
                    90f);
                graphics.FillPath(fade, beam);
            }

            // Der Lichtpunkt: weicher Hof, dann der helle Kern.
            using (var halo = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
            {
                graphics.FillEllipse(halo,
                    centerX - (orbRadius * 2.3f), orbY - (orbRadius * 2.3f),
                    orbRadius * 4.6f, orbRadius * 4.6f);
            }

            using (var core = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(core,
                    centerX - orbRadius, orbY - orbRadius, orbRadius * 2, orbRadius * 2);
            }

            graphics.Clip = clip;
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Eine Farbe Richtung Weiß aufhellen (für den Verlauf der Fläche).</summary>
    private static Color Lighten(Color color, float amount) => Color.FromArgb(
        color.R + (int)((255 - color.R) * amount),
        color.G + (int)((255 - color.G) * amount),
        color.B + (int)((255 - color.B) * amount));

    /// <summary>Ein abgerundetes Rechteck als Pfad (Fläche und Beschnitt).</summary>
    private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
