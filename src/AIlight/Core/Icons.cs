using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Brush = System.Drawing.Brush;
using Color = System.Drawing.Color;
using FontFamily = System.Drawing.FontFamily;
using FontStyle = System.Drawing.FontStyle;
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

    /// <summary>Notfallsymbol: abgerundete Fläche mit einem hellen Funken.</summary>
    public static Icon Draw(int size, string accent = "#4C8DFF")
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Color.Transparent);

            var color = Views.Theme.Parse(accent) is { } parsed
                ? Color.FromArgb(parsed.R, parsed.G, parsed.B)
                : Color.FromArgb(0x4C, 0x8D, 0xFF);

            using var brush = new SolidBrush(color);
            var inset = size * 0.06f;
            graphics.FillRoundedRectangle(brush, inset, inset, size - (2 * inset), size - (2 * inset), size * 0.22f);

            using var family = new FontFamily("Segoe UI Symbol");
            using var font = new Font(family, size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString("✦", font, white, new RectangleF(0, -size * 0.03f, size, size), format);
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

    private static void FillRoundedRectangle(
        this Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
