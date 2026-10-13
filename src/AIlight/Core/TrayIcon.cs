using System.Drawing;
using System.Windows.Forms;

namespace AIlight.Core;

/// <summary>
/// Infobereich-Symbol (Tray) mit Menü. Linksklick öffnet die Leiste,
/// Rechtsklick zeigt das Menü — kein Eintrag in der Taskleiste.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly Icon _iconImage;

    public TrayIcon(Icon icon, string tooltip)
    {
        _iconImage = icon;
        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = tooltip.Length > 63 ? tooltip[..63] : tooltip, // Windows-Grenze
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                Activated?.Invoke();
            }
        };
    }

    /// <summary>Linksklick auf das Symbol.</summary>
    public event Action? Activated;

    public ToolStripMenuItem AddItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Menüeintrag „{text}“ fehlgeschlagen", ex);
            }
        };
        _menu.Items.Add(item);
        return item;
    }

    public ToolStripMenuItem AddCheck(string text, bool isChecked, Action<bool> setter)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = isChecked };
        item.CheckedChanged += (_, _) => setter(item.Checked);
        _menu.Items.Add(item);
        return item;
    }

    public void AddSeparator() => _menu.Items.Add(new ToolStripSeparator());

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.ShowBalloonTip(6000);
        }
        catch (Exception ex)
        {
            Log.Warn($"Hinweis konnte nicht angezeigt werden: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _iconImage.Dispose();
    }
}
