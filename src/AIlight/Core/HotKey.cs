using System.Windows.Input;
using System.Windows.Interop;

namespace AIlight.Core;

/// <summary>
/// Globaler Hotkey über Win32 <c>RegisterHotKey</c> (kein Tastatur-Hook, kein
/// Mithören). Die Nachricht kommt an einem unsichtbaren Nachrichtenfenster an —
/// die Leiste muss dafür nicht existieren und im Leerlauf kostet es nichts.
/// </summary>
public sealed class HotKey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotKeyId = 0x41;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _window;
    private bool _registered;

    public HotKey()
    {
        var parameters = new HwndSourceParameters("AIlightHotKeySink")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            ParentWindow = HWND_MESSAGE,
            Width = 0,
            Height = 0,
        };
        _window = new HwndSource(parameters);
        _window.AddHook(MessageHook);
    }

    /// <summary>Wird bei jedem Drücken ausgelöst.</summary>
    public event Action? Pressed;

    // ── Modifikatoren (Win32) ────────────────────────────────────────────────
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr MessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            try
            {
                Pressed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("Hotkey-Verarbeitung fehlgeschlagen", ex);
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>Hotkey setzen. Gibt bei einem Fehler einen lesbaren Satz zurück.</summary>
    public bool Register(string spec, out string error)
    {
        error = "";
        if (!HotKeySpec.TryParse(spec, out var modifiers, out var virtualKey, out error))
        {
            return false;
        }

        Unregister();
        if (!RegisterHotKey(_window.Handle, HotKeyId, modifiers | MOD_NOREPEAT, virtualKey))
        {
            var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            error = code switch
            {
                1409 => "bereits von einem anderen Programm belegt",
                5 => "von Windows gesperrt (Zugriff verweigert)",
                _ => $"von Windows abgelehnt (Fehler {code})",
            };
            Log.Warn($"Hotkey {spec} nicht registriert: {error}");
            return false;
        }

        _registered = true;
        return true;
    }

    private void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(_window.Handle, HotKeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }
}

/// <summary>Umrechnung „Alt+Space“ ⇄ Win32-Codes. Reine Logik, deshalb prüfbar.</summary>
public static class HotKeySpec
{
    private static readonly Dictionary<string, uint> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALT"] = HotKey.MOD_ALT,
        ["CTRL"] = HotKey.MOD_CONTROL,
        ["STRG"] = HotKey.MOD_CONTROL,
        ["SHIFT"] = HotKey.MOD_SHIFT,
        ["UMSCHALT"] = HotKey.MOD_SHIFT,
        ["WIN"] = HotKey.MOD_WIN,
        ["WINDOWS"] = HotKey.MOD_WIN,
    };

    private static readonly Dictionary<string, uint> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = 0x20, ["LEERTASTE"] = 0x20, ["TAB"] = 0x09, ["ENTER"] = 0x0D,
        ["ESC"] = 0x1B, ["BACKSPACE"] = 0x08, ["DEL"] = 0x2E, ["ENTF"] = 0x2E,
        ["INS"] = 0x2D, ["HOME"] = 0x24, ["END"] = 0x23, ["PGUP"] = 0x21, ["PGDN"] = 0x22,
        ["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25, ["RIGHT"] = 0x27,
        ["."] = 0xBE, [","] = 0xBC, ["-"] = 0xBD, ["/"] = 0xBF, [";"] = 0xBA,
    };

    public static bool TryParse(string spec, out uint modifiers, out uint virtualKey, out string error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = "";
        var text = (spec ?? "").Trim();
        if (text.Length == 0)
        {
            error = "Es ist kein Hotkey angegeben.";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "Ein globaler Hotkey braucht eine Zusatztaste (Strg, Alt, Umschalt oder Win) "
                + "und eine Taste — zum Beispiel „Alt+Space“.";
            return false;
        }

        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!Modifiers.TryGetValue(parts[index], out var value))
            {
                error = $"„{parts[index]}“ ist keine bekannte Zusatztaste (Strg, Alt, Umschalt oder Win).";
                return false;
            }

            modifiers |= value;
        }

        if (!TryParseKey(parts[^1], out virtualKey))
        {
            error = $"Unbekannte Taste „{parts[^1]}“ — erlaubt sind Buchstaben, Ziffern, F1–F24 "
                + "und Sondertasten wie Leertaste, Tab, Enter, Esc.";
            return false;
        }

        return true;
    }

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;
        var upper = token.Trim().ToUpperInvariant();
        if (upper.Length == 0)
        {
            return false;
        }

        if (Keys.TryGetValue(upper, out virtualKey))
        {
            return true;
        }

        if (upper.Length >= 2 && upper[0] == 'F' && int.TryParse(upper[1..], out var function) && function is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + function - 1);
            return true;
        }

        if (upper.Length == 1 && (char.IsAsciiLetterOrDigit(upper[0])))
        {
            virtualKey = upper[0];
            return true;
        }

        return false;
    }

    /// <summary>Anzeigetext aus Win32-Codes, z. B. „Ctrl+Alt+Space“.</summary>
    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & HotKey.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & HotKey.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & HotKey.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & HotKey.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(virtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(uint virtualKey) => virtualKey switch
    {
        0x20 => "Space",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x08 => "Backspace",
        0x2E => "Del",
        0x2D => "Ins",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PgUp",
        0x22 => "PgDn",
        0x26 => "Up",
        0x28 => "Down",
        0x25 => "Left",
        0x27 => "Right",
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x70 + 1}",
        _ => ((char)virtualKey).ToString(),
    };

    /// <summary>Aus einer Tastatureingabe im Einstellungsfenster einen Hotkey-Text bauen.</summary>
    public static string FromKeyEvent(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

        var name = key switch
        {
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Enter or Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            >= Key.F1 and <= Key.F24 => key.ToString(),
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            _ => "",
        };

        if (name.Length == 0 || parts.Count == 0)
        {
            return "";
        }

        parts.Add(name);
        return string.Join("+", parts);
    }
}
