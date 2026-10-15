using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIlight.Core;

namespace AIlight;

/// <summary>
/// Alle Einstellungen an einer Stelle: <c>%APPDATA%\AIlight\settings.json</c>.
/// Jede Änderung wirkt sofort (kein „OK“-Zwang) — die Oberfläche schreibt direkt hierher.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    // ── Allgemein ────────────────────────────────────────────────────────────
    private string _hotkey = "Alt+Space";
    private bool _startWithWindows = true;
    private bool _preloadBar = true;
    private bool _hideOnLostFocus = true;

    /// <summary>
    /// Wurde die Einführung schon gezeigt? Sie läuft genau einmal — danach ist
    /// sie über /welcome jederzeit wieder aufrufbar. Deshalb steht sie hier und
    /// nicht im Programm: Ein Neuinstallieren soll sie nicht erneut aufzwingen.
    /// </summary>
    private bool _welcomeSeen;

    // ── Modell ───────────────────────────────────────────────────────────────
    private string _model = "auto";
    private string _answerLength = "mittel";
    private string _reasoningEffort = "default";
    private bool _webSearch;
    private string _systemPrompt = "";

    // ── Gespräch ─────────────────────────────────────────────────────────────
    private int _memoryRounds = 3;

    // ── Aussehen ─────────────────────────────────────────────────────────────
    private string _theme = "system";
    private string _accent = Views.Theme.DefaultAccent;
    private double _windowWidth = 680;
    private double _topPercent = 14;
    private double _fontSize = 16;
    private double _opacity = 0.98;
    private double _cornerRadius = 16;
    private string _fontFamily = "system";
    private bool _animateBar = true;
    private bool _showThinking;
    private bool _showMeta = true;

    // ── Verlauf ──────────────────────────────────────────────────────────────
    private int _maxSuggestions = 6;
    private int _keepHistoryDays;

    // ── Erweitert ────────────────────────────────────────────────────────────
    private bool _logging = true;
    private string _answersFolder = "";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Datei, aus der diese Einstellungen kamen — dorthin wird auch gespeichert.</summary>
    private string _file = Paths.SettingsFile;

    /// <summary>
    /// Beim Lesen setzt der JSON-Parser jede Eigenschaft einzeln. Ohne diese
    /// Sperre würde dabei die halbfertige Datei sofort wieder geschrieben.
    /// </summary>
    private static bool _reading;

    /// <summary>Wird bei jeder geänderten Eigenschaft ausgelöst (Oberfläche folgt sofort).</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Wird nach jeder gespeicherten Änderung mit dem Eigenschaftsnamen aufgerufen.</summary>
    public event Action<string>? Changed;

    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool WelcomeSeen { get => _welcomeSeen; set => Set(ref _welcomeSeen, value); }
    public bool PreloadBar { get => _preloadBar; set => Set(ref _preloadBar, value); }
    public bool HideOnLostFocus { get => _hideOnLostFocus; set => Set(ref _hideOnLostFocus, value); }

    public string Model { get => _model; set => Set(ref _model, value); }
    public string AnswerLength { get => _answerLength; set => Set(ref _answerLength, value); }
    public string ReasoningEffort { get => _reasoningEffort; set => Set(ref _reasoningEffort, value); }
    public bool WebSearch { get => _webSearch; set => Set(ref _webSearch, value); }
    public string SystemPrompt { get => _systemPrompt; set => Set(ref _systemPrompt, value); }

    public int MemoryRounds { get => _memoryRounds; set => Set(ref _memoryRounds, value); }

    public string Theme { get => _theme; set => Set(ref _theme, value); }
    public string Accent { get => _accent; set => Set(ref _accent, value); }
    public double WindowWidth { get => _windowWidth; set => Set(ref _windowWidth, value); }
    public double TopPercent { get => _topPercent; set => Set(ref _topPercent, value); }
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, value); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, value); }
    public double CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, value); }
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public bool AnimateBar { get => _animateBar; set => Set(ref _animateBar, value); }
    public bool ShowThinking { get => _showThinking; set => Set(ref _showThinking, value); }
    public bool ShowMeta { get => _showMeta; set => Set(ref _showMeta, value); }

    public int MaxSuggestions { get => _maxSuggestions; set => Set(ref _maxSuggestions, value); }
    public int KeepHistoryDays { get => _keepHistoryDays; set => Set(ref _keepHistoryDays, value); }

    public bool Logging { get => _logging; set => Set(ref _logging, value); }
    public string AnswersFolder { get => _answersFolder; set => Set(ref _answersFolder, value); }

    // ── Abgeleitete Werte ────────────────────────────────────────────────────

    [JsonIgnore]
    public string EffectiveAnswersFolder =>
        string.IsNullOrWhiteSpace(AnswersFolder) ? Paths.DefaultAnswersFolder : AnswersFolder;

    /// <summary>Maximale Antwortlänge in Tokens.</summary>
    [JsonIgnore]
    public int MaxCompletionTokens => AnswerLength switch
    {
        "kurz" => 512,
        "lang" => 8192,
        _ => 2048,
    };

    /// <summary>Denk-Aufwand für die API (<c>null</c> = Parameter weglassen).</summary>
    [JsonIgnore]
    public string? Reasoning => ReasoningEffort switch
    {
        "aus" => "low",       // gpt-oss kennt kein „aus“; die niedrigste Stufe ist low
        "niedrig" => "low",
        "hoch" => "high",
        "default" => null,
        _ => "medium",
    };

    [JsonIgnore] public bool HasCustomSystemPrompt => !string.IsNullOrWhiteSpace(SystemPrompt);

    // ── Laden und Speichern ──────────────────────────────────────────────────

    public static AppSettings Load(string? path = null)
    {
        var file = path ?? Paths.SettingsFile;
        try
        {
            if (File.Exists(file))
            {
                var text = File.ReadAllText(file);
                AppSettings? loaded;
                _reading = true;
                try
                {
                    loaded = JsonSerializer.Deserialize<AppSettings>(text, Json);
                }
                finally
                {
                    _reading = false;
                }

                if (loaded is not null)
                {
                    loaded._file = file;
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Einstellungen konnten nicht gelesen werden ({ex.Message}) — Standardwerte werden benutzt");
        }

        var fresh = new AppSettings();
        if (path is not null)
        {
            fresh._file = path;
        }

        return fresh;
    }

    /// <summary>Ungültige Werte auf vernünftige Grenzen ziehen (handgeschriebene Dateien!).</summary>
    public void Normalize()
    {
        _windowWidth = Math.Clamp(_windowWidth, 420, 1600);
        _topPercent = Math.Clamp(_topPercent, 0, 60);
        _fontSize = Math.Clamp(_fontSize, 12, 28);
        _opacity = Math.Clamp(_opacity, 0.6, 1.0);
        _cornerRadius = Math.Clamp(_cornerRadius, 8, 22);
        if (_fontFamily is not ("system" or "segoe" or "calibri" or "arial"))
        {
            _fontFamily = "system";
        }
        _maxSuggestions = Math.Clamp(_maxSuggestions, 0, 12);
        _memoryRounds = Math.Clamp(_memoryRounds, 0, 20);
        _keepHistoryDays = Math.Clamp(_keepHistoryDays, 0, 3650);
        if (_theme is not ("system" or "hell" or "dunkel"))
        {
            _theme = "system";
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? _file;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            AtomicFile.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            Log.Error("Einstellungen konnten nicht gespeichert werden", ex);
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (_reading)
        {
            return;
        }

        Save();
        Changed?.Invoke(name);
    }
}
