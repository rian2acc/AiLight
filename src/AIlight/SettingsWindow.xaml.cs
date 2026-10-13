using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AIlight.Core;
using AIlight.Groq;
using AIlight.Views;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AIlight;

/// <summary>
/// Das Einstellungsfenster: links die Bereiche, rechts eine Seite aus Karten.
/// Jede Änderung wirkt sofort und wird sofort gespeichert — kein „Übernehmen“.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ChatService _chat;
    private readonly Dictionary<string, ToggleButton> _navButtons = new();
    private readonly Dictionary<string, StackPanel> _pages = new();
    private readonly Dictionary<string, ToggleButton> _swatches = new();
    private CancellationTokenSource? _keyTest;
    private bool _loading = true;
    private bool _keyTestActive;
    private bool _keyShownOnce;
    private string _page = "konto";

    private static readonly (string Label, string Hex)[] Accents =
    {
        ("Blau", "#4C8DFF"),
        ("Türkis", "#2ED3B7"),
        ("Grün", "#4CC38A"),
        ("Orange", "#F2A33C"),
        ("Rot", "#F2686B"),
        ("Violett", "#A98BFF"),
    };

    /// <summary>Die Bereiche des Fensters — Reihenfolge wie in der Navigation.</summary>
    private static readonly (string Key, string Title)[] Areas =
    {
        ("konto", "Konto"),
        ("modell", "Modell"),
        ("gespraech", "Gespräch"),
        ("verhalten", "Verhalten"),
        ("aussehen", "Aussehen"),
        ("verlauf", "Verlauf"),
        ("ueber", "Über"),
    };

    public SettingsWindow(AppSettings settings, ChatService chat)
    {
        _settings = settings;
        _chat = chat;

        InitializeComponent();

        // Auf kleinen Bildschirmen (und bei hoher Skalierung) darf das Fenster nicht
        // aus dem sichtbaren Bereich laufen — deshalb nie größer als der Arbeitsbereich.
        var work = SystemParameters.WorkArea;
        MaxWidth = Math.Max(640, work.Width - 60);
        MaxHeight = Math.Max(460, work.Height - 60);
        Width = Math.Min(940, MaxWidth);
        Height = Math.Min(700, MaxHeight);

        CollectAreas();
        BuildChoices();
        BuildAccents();
        LoadValues();
        ShowPage("konto");
        _loading = false;
    }

    // ── Prüfstand (nur für den Selbsttest) ──────────────────────────────────

    internal void ApplyChoiceForTest(string boxName, string tag)
    {
        var box = (ComboBox)FindName(boxName)!;
        box.SelectedItem = ItemWithTag(box, tag);
        OnControlChanged(box, new RoutedEventArgs());
    }

    internal void ApplySliderForTest(string sliderName, double value)
    {
        var slider = (Slider)FindName(sliderName)!;
        slider.Value = value;
        OnSliderChanged(slider, new RoutedPropertyChangedEventArgs<double>(0, value));
    }

    internal IReadOnlyList<string> AreaKeysForTest => Areas.Select(area => area.Key).ToList();

    internal bool IsAreaVisibleForTest(string key) => _pages[key].Visibility == Visibility.Visible;

    internal string VisibleAreaForTest => _page;

    internal FrameworkElement PreviewForTest => PreviewCard;

    internal void SelectAreaForTest(string key) => ShowPage(key);

    // ── Aufbau ──────────────────────────────────────────────────────────────

    private void CollectAreas()
    {
        _navButtons["konto"] = NavKonto;
        _navButtons["modell"] = NavModell;
        _navButtons["gespraech"] = NavChat;
        _navButtons["verhalten"] = NavVerhalten;
        _navButtons["aussehen"] = NavAussehen;
        _navButtons["verlauf"] = NavVerlauf;
        _navButtons["ueber"] = NavAbout;

        _pages["konto"] = PageKonto;
        _pages["modell"] = PageModell;
        _pages["gespraech"] = PageChat;
        _pages["verhalten"] = PageVerhalten;
        _pages["aussehen"] = PageAussehen;
        _pages["verlauf"] = PageVerlauf;
        _pages["ueber"] = PageAbout;
    }

    private static void Fill(ComboBox box, params (string Label, string Value)[] items)
    {
        foreach (var (label, value) in items)
        {
            box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
    }

    private void BuildChoices()
    {
        Fill(ModelBox,
            ("Automatisch (empfohlen)", GroqModels.Auto),
            ("gpt-oss-20b — schnell und günstig", GroqModels.Small.Id),
            ("gpt-oss-120b — stärker", GroqModels.Large.Id));

        Fill(LengthBox, ("Kurz", "kurz"), ("Mittel", "mittel"), ("Lang", "lang"));

        Fill(ReasoningBox,
            ("Standard des Modells", "default"),
            ("Wenig", "niedrig"),
            ("Mittel", "mittel"),
            ("Viel", "hoch"),
            ("Aus", "aus"));

        var memory = new List<(string, string)>();
        for (var rounds = 0; rounds <= 10; rounds++)
        {
            memory.Add((rounds == 0 ? "Kein Gedächtnis" : $"{rounds} Runde(n)", rounds.ToString()));
        }

        Fill(MemoryBox, memory.ToArray());

        Fill(KeepBox,
            ("Unbegrenzt", "0"),
            ("7 Tage", "7"),
            ("30 Tage", "30"),
            ("90 Tage", "90"),
            ("1 Jahr", "365"));

        Fill(ThemeBox, ("Wie Windows", "system"), ("Hell", "hell"), ("Dunkel", "dunkel"));
    }

    private void BuildAccents()
    {
        foreach (var (label, hex) in Accents)
        {
            var swatch = new ToggleButton
            {
                Style = (Style)FindResource("Swatch"),
                ToolTip = label,
                Tag = hex,
                Background = new SolidColorBrush(Theme.Parse(hex) ?? Colors.Gray),
            };
            swatch.Click += (sender, _) =>
            {
                if (sender is ToggleButton { Tag: string value })
                {
                    _settings.Accent = value;
                    UpdateSwatches();
                }
            };

            _swatches[hex] = swatch;
            AccentPanel.Children.Add(swatch);
        }
    }

    private ComboBoxItem? ItemWithTag(ComboBox box, string tag)
        => box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (item.Tag as string) == tag);

    private void LoadValues()
    {
        NavVersion.Text = "Version " + typeof(App).Assembly.GetName().Version;

        ModelBox.SelectedItem = ItemWithTag(ModelBox, _settings.Model) ?? ModelBox.Items[0];
        LengthBox.SelectedItem = ItemWithTag(LengthBox, _settings.AnswerLength) ?? LengthBox.Items[1];
        ReasoningBox.SelectedItem = ItemWithTag(ReasoningBox, _settings.ReasoningEffort) ?? ReasoningBox.Items[0];
        MemoryBox.SelectedItem = ItemWithTag(MemoryBox, _settings.MemoryRounds.ToString()) ?? MemoryBox.Items[3];
        KeepBox.SelectedItem = ItemWithTag(KeepBox, _settings.KeepHistoryDays.ToString()) ?? KeepBox.Items[0];
        ThemeBox.SelectedItem = ItemWithTag(ThemeBox, _settings.Theme) ?? ThemeBox.Items[0];

        WebSearchCheck.IsChecked = _settings.WebSearch;
        PromptBox.Text = _settings.SystemPrompt;

        WidthSlider.Value = _settings.WindowWidth;
        TopSlider.Value = _settings.TopPercent;
        FontSlider.Value = _settings.FontSize;
        OpacitySlider.Value = _settings.Opacity;

        HotkeyBox.Text = _settings.Hotkey;
        StartCheck.IsChecked = _settings.StartWithWindows;
        PreloadCheck.IsChecked = _settings.PreloadBar;
        HideCheck.IsChecked = _settings.HideOnLostFocus;
        LogCheck.IsChecked = _settings.Logging;

        _keyShownOnce = false;
        UpdateSliderLabels();
        UpdateSwatches();
        UpdateKeyStatus();
        UpdateStats();

        AboutText.Text =
            $"Version {typeof(App).Assembly.GetName().Version} — KI-Leiste für Windows. "
            + "Ein Hotkey, eine Frage, eine Antwort; der Verlauf bleibt auf diesem Rechner.\n\n"
            + $"Einstellungen: {Paths.SettingsFile}\n"
            + $"Verlauf: {Paths.HistoryFile}\n"
            + $"Protokoll: {Log.Path_}";
    }

    private void UpdateSwatches()
    {
        foreach (var (hex, swatch) in _swatches)
        {
            swatch.IsChecked = string.Equals(hex, _settings.Accent, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateSliderLabels()
    {
        WidthValue.Text = $"{WidthSlider.Value:0}";
        TopValue.Text = $"{TopSlider.Value:0} %";
        FontValue.Text = $"{FontSlider.Value:0}";
        OpacityValue.Text = $"{OpacitySlider.Value:P0}";

        // Die Vorschau soll wirklich zeigen, was eingestellt ist — deshalb folgt
        // sie Breite, Schriftgröße und Deckkraft direkt.
        var fontSize = FontSlider.Value;
        PreviewText.FontSize = fontSize;
        PreviewMeta.FontSize = Math.Max(10, fontSize * 0.7);
        PreviewHint.FontSize = Math.Max(9, fontSize * 0.6);
        PreviewCard.Width = Math.Clamp(WidthSlider.Value * 0.5, 300, 420);
        PreviewCard.Opacity = OpacitySlider.Value;
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string key })
        {
            ShowPage(key);
        }
    }

    private void ShowPage(string key)
    {
        if (!_pages.ContainsKey(key))
        {
            key = "konto";
        }

        _page = key;
        foreach (var (pageKey, page) in _pages)
        {
            page.Visibility = pageKey == key ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var (navKey, button) in _navButtons)
        {
            button.IsChecked = navKey == key;
        }

        PageScroll.ScrollToTop();
    }

    // ── Fensterrahmen ───────────────────────────────────────────────────────

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Log.Info("Einstellungen geschlossen");
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_keyTestActive)
        {
            e.Handled = true;
            Close();
        }
    }

    // ── Änderungen ──────────────────────────────────────────────────────────

    private void OnControlChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Model = TagOf(ModelBox, GroqModels.Auto);
        _settings.AnswerLength = TagOf(LengthBox, "mittel");
        _settings.ReasoningEffort = TagOf(ReasoningBox, "default");
        _settings.MemoryRounds = int.TryParse(TagOf(MemoryBox, "3"), out var rounds) ? rounds : 3;
        _settings.KeepHistoryDays = int.TryParse(TagOf(KeepBox, "0"), out var days) ? days : 0;
        _settings.Theme = TagOf(ThemeBox, "system");
        _settings.WebSearch = WebSearchCheck.IsChecked == true;
        _settings.SystemPrompt = PromptBox.Text.Trim();
        _settings.StartWithWindows = StartCheck.IsChecked == true;
        _settings.PreloadBar = PreloadCheck.IsChecked == true;
        _settings.HideOnLostFocus = HideCheck.IsChecked == true;
        _settings.Logging = LogCheck.IsChecked == true;

        UpdateStats();
    }

    private static string TagOf(ComboBox box, string fallback)
        => box.SelectedItem is ComboBoxItem { Tag: string value } ? value : fallback;

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        _settings.WindowWidth = Math.Round(WidthSlider.Value);
        _settings.TopPercent = Math.Round(TopSlider.Value);
        _settings.FontSize = Math.Round(FontSlider.Value);
        _settings.Opacity = Math.Round(OpacitySlider.Value, 2);
        UpdateSliderLabels();
    }

    private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var spec = HotKeySpec.FromKeyEvent(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        if (spec.Length == 0)
        {
            HotkeyStatus.Text = "Bitte eine Zusatztaste (Strg/Alt/Umschalt/Win) zusammen mit einer Taste drücken.";
            return;
        }

        if (!HotKeySpec.TryParse(spec, out _, out _, out var error))
        {
            HotkeyStatus.Text = error;
            return;
        }

        HotkeyBox.Text = spec;
        _settings.Hotkey = spec;
        HotkeyStatus.Text = "Gespeichert und sofort aktiv. Konflikte meldet AIlight mit einem Hinweis unten rechts.";
    }

    // ── Konto ───────────────────────────────────────────────────────────────

    private void UpdateKeyStatus()
    {
        // Einmal beim Öffnen den gespeicherten Schlüssel zeigen (maskiert),
        // danach gehört das Feld dem Nutzer.
        if (!_keyShownOnce)
        {
            KeyBox.Password = _chat.ApiKey;
            _keyShownOnce = true;
        }

        KeyStatus.Text = _chat.HasKey
            ? "Ein Schlüssel ist gespeichert."
            : "Es ist noch kein Schlüssel gespeichert — ohne Schlüssel kann AIlight nichts fragen.";
    }

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var value = KeyBox.Password.Trim();
        if (value.Length == 0)
        {
            KeyStatus.Text = "Bitte einen Schlüssel eingeben (beginnt üblicherweise mit „gsk_“).";
            return;
        }

        KeyStatus.Text = SecretStore.Write(value)
            ? "Schlüssel gespeichert. Mit „Schlüssel testen“ prüfen wir ihn sofort."
            : "Der Schlüssel konnte nicht gespeichert werden — Details stehen im Protokoll.";
    }

    private void OnDeleteKey(object sender, RoutedEventArgs e)
    {
        SecretStore.Delete();
        KeyBox.Password = "";
        UpdateKeyStatus();
        KeyStatus.Text = "Schlüssel gelöscht.";
    }

    private async void OnTestKey(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (key.Length == 0)
        {
            key = _chat.ApiKey;
        }

        if (key.Length == 0)
        {
            KeyStatus.Text = "Es ist kein Schlüssel zum Testen da.";
            return;
        }

        _keyTest?.Cancel();
        _keyTest = new CancellationTokenSource();
        _keyTestActive = true;
        TestKeyButton.IsEnabled = false;
        KeyStatus.Text = "Prüfe den Schlüssel …";

        try
        {
            var message = await _chat.Client.TestKeyAsync(key, _keyTest.Token);
            var (model, _) = GroqModels.Resolve(_settings.Model, "Test", _settings.WebSearch);
            KeyStatus.Text = message + $" (Test mit {model.Id})";
            SecretStore.Write(key);
        }
        catch (Exception ex)
        {
            KeyStatus.Text = "Der Test ist fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            // Der Prüftext bleibt stehen — hier nur den Knopf wieder freigeben.
            _keyTestActive = false;
            TestKeyButton.IsEnabled = true;
        }
    }

    // ── Verlauf und Gespräch ────────────────────────────────────────────────

    private void UpdateStats()
    {
        var (count, tokensIn, tokensOut, cost, bytes) = _chat.History.Stats();
        StatsText.Text = $"Gespeichert: {count} Antwort(en) · {tokensIn} → {tokensOut} Tokens · "
            + $"{GroqModels.FormatCost(cost)} · Datei {bytes / 1024.0:0.0} kB";
    }

    private void OnClearConversation(object sender, RoutedEventArgs e)
    {
        _chat.NewChat();
        MessageBox.Show(this, "Das Gedächtnis ist geleert. Der Verlauf bleibt erhalten.",
            "AIlight", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        var (count, _, _, _, _) = _chat.History.Stats();
        if (count == 0)
        {
            MessageBox.Show(this, "Der Verlauf ist schon leer.", "AIlight",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Wirklich alle {count} gespeicherten Antworten löschen? Das lässt sich nicht rückgängig machen.",
            "Verlauf löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
        {
            _chat.History.Delete("");
            UpdateStats();
        }
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => Open(Paths.DataFolder);

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        var path = Log.Path_;
        if (path.Length > 0 && File.Exists(path))
        {
            Open(path);
        }
        else
        {
            Open(Paths.LogFolder);
        }
    }

    private static void Open(string path)
    {
        try
        {
            if (path.Length == 0)
            {
                return;
            }

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Ordner konnte nicht geöffnet werden: {ex.Message}");
        }
    }
}
