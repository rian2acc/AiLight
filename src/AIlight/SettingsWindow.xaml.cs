using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AIlight.Ai;
using AIlight.Core;
using AIlight.Chat;
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

    /// <summary>Modelle, die der Anbieter selbst gemeldet hat („Modelle laden“).</summary>
    private readonly List<string> _extraModels = new();
    private string _page = "konto";

    /// <summary>
    /// Die Akzentfarben von macOS — von Blau bis Graphit. Die Einführung zeigt
    /// dieselbe Liste, damit beide Fenster nicht auseinanderlaufen können.
    /// </summary>
    internal static readonly (string Label, string Hex)[] Accents =
    {
        ("Blau", "#0A84FF"),
        ("Türkis", "#64D2FF"),
        ("Grün", "#30D158"),
        ("Gelb", "#FFD60A"),
        ("Orange", "#FF9F0A"),
        ("Rot", "#FF453A"),
        ("Pink", "#FF375F"),
        ("Lila", "#BF5AF2"),
        ("Graphit", "#8E8E93"),
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

    /// <summary>Die Kennungen aller Einträge einer Auswahlliste (Prüfstand).</summary>
    internal IReadOnlyList<string> ItemTagsForTest(string boxName)
        => ((ComboBox)FindName(boxName)!).Items
            .OfType<ComboBoxItem>()
            .Select(item => (item.Tag as string) ?? "")
            .ToList();

    /// <summary>Steht ein Eintrag mit dieser Kennung in der Liste? (Prüfstand)</summary>
    internal bool HasItemForTest(string boxName, string tag)
        => ItemWithTag((ComboBox)FindName(boxName)!, tag) is not null;

    /// <summary>Einen Text in ein Feld schreiben und die Änderung auslösen (Prüfstand).</summary>
    internal void ApplyTextForTest(string boxName, string value)
    {
        var box = (TextBox)FindName(boxName)!;
        box.Text = value;
        OnControlChanged(box, new RoutedEventArgs());
    }

    internal IReadOnlyList<string> AreaKeysForTest => Areas.Select(area => area.Key).ToList();

    internal bool IsAreaVisibleForTest(string key) => _pages[key].Visibility == Visibility.Visible;

    internal string VisibleAreaForTest => _page;

    internal FrameworkElement PreviewForTest => PreviewCard;

    internal void SelectAreaForTest(string key) => ShowPage(key);

    /// <summary>Von außen auf einen Bereich springen (z. B. aus der Einführung).</summary>
    public void ShowArea(string key) => ShowPage(key);

    /// <summary>Deckkraft des ganzen Fensters (Prüfstand: Auftritt und Grundstellung).</summary>
    internal double RootOpacityForTest => Root.Opacity;

    /// <summary>Auftrieb des Fensters (0 = Grundstellung).</summary>
    internal double RootShiftForTest => RootShift.Y;

    /// <summary>Hängt am Auftritt eine Bewegung? (Prüfstand)</summary>
    internal bool RootAnimatedForTest => Root.HasAnimatedProperties;

    internal double PageOpacityForTest(string key) => _pages[key].Opacity;

    internal double PageShiftForTest(string key)
        => _pages[key].RenderTransform is TranslateTransform shift ? shift.Y : 0;

    /// <summary>Läuft auf dieser Seite eine Bewegung? (Prüfstand)</summary>
    internal bool PageAnimatedForTest(string key) => _pages[key].HasAnimatedProperties;

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
        // Die Anbieter kommen aus dem Katalog — was dort steht, ist auch wählbar.
        Fill(ProviderBox, Providers.All.Select(provider => (provider.Name, provider.Id)).ToArray());

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

        Fill(FontBox,
            ("System (empfohlen)", "system"),
            ("Segoe UI", "segoe"),
            ("Calibri", "calibri"),
            ("Arial", "arial"));

        Fill(HistoryBox,
            ("100 Antworten", "100"),
            ("250 Antworten", "250"),
            ("500 Antworten (Standard)", "500"),
            ("1.000 Antworten", "1000"),
            ("2.500 Antworten", "2500"),
            ("5.000 Antworten", "5000"));
    }

    /// <summary>Der Anbieter, um den es gerade geht.</summary>
    private ProviderInfo Provider => Providers.Find(_settings.Provider);

    /// <summary>
    /// Die Modellliste gehört zum Anbieter: sie wird bei jedem Wechsel neu gebaut.
    /// Steht ein Name nicht im Katalog (oder ist er selbst eingetippt), bleibt er
    /// trotzdem wählbar — kein Anbieter kommt hier vollständig vor.
    /// </summary>
    private void ReloadModels()
    {
        var provider = Provider;
        var chosen = _settings.ProviderModels.TryGetValue(provider.Id, out var stored)
                     && !string.IsNullOrWhiteSpace(stored)
            ? stored.Trim()
            : Providers.Auto;

        var items = new List<(string Label, string Value)>
        {
            ("Automatisch (empfohlen)", Providers.Auto),
        };

        foreach (var model in provider.Catalog)
        {
            var price = model.Priced ? $" · {model.InputPerMillion:0.###}/{model.OutputPerMillion:0.###} $ je Mio." : "";
            items.Add((model.Label + price, model.Id));
        }

        foreach (var name in _extraModels)
        {
            if (items.All(item => item.Value != name))
            {
                items.Add((name + " (vom Anbieter gemeldet)", name));
            }
        }

        if (chosen != Providers.Auto && items.All(item => item.Value != chosen))
        {
            items.Add((chosen + " (eigener Name)", chosen));
        }

        ModelBox.Items.Clear();
        Fill(ModelBox, items.ToArray());
        ModelBox.SelectedItem = ItemWithTag(ModelBox, chosen) ?? ModelBox.Items[0];
        ModelNameBox.Text = chosen == Providers.Auto ? "" : chosen;
        ModelInfoText.Text = provider.Note;
    }

    /// <summary>Alles, was am Anbieterwechsel hängt: Adresse, Schlüssel und Modelle.</summary>
    private void UpdateProviderInfo()
    {
        var provider = Provider;
        ProviderInfoText.Text = provider.NeedsKey
            ? $"Gefragt wird über {provider.Name}. Der Schlüssel liegt getrennt je Anbieter — ein Wechsel lässt die anderen unangetastet."
            : $"{provider.Name} läuft auf diesem Rechner: es genügt, wenn der Server läuft. Ein Schlüssel ist nicht nötig.";
        BaseUrlHint.Text = $"Standard: {provider.BaseUrl}";
        KeyHint.Text = provider.KeyHint;
        KeyBox.IsEnabled = provider.NeedsKey;
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

        ProviderBox.SelectedItem = ItemWithTag(ProviderBox, _settings.Provider) ?? ProviderBox.Items[0];
        BaseUrlBox.Text = _settings.BaseUrl;
        UpdateProviderInfo();
        ReloadModels();

        LengthBox.SelectedItem = ItemWithTag(LengthBox, _settings.AnswerLength) ?? LengthBox.Items[1];
        ReasoningBox.SelectedItem = ItemWithTag(ReasoningBox, _settings.ReasoningEffort) ?? ReasoningBox.Items[0];
        MemoryBox.SelectedItem = ItemWithTag(MemoryBox, _settings.MemoryRounds.ToString()) ?? MemoryBox.Items[3];
        KeepBox.SelectedItem = ItemWithTag(KeepBox, _settings.KeepHistoryDays.ToString()) ?? KeepBox.Items[0];
        ThemeBox.SelectedItem = ItemWithTag(ThemeBox, _settings.Theme) ?? ThemeBox.Items[0];
        HistoryBox.SelectedItem = ItemWithTag(HistoryBox, _settings.HistoryEntries.ToString())
            ?? ItemWithTag(HistoryBox, "500") ?? HistoryBox.Items[0];

        WebSearchCheck.IsChecked = _settings.WebSearch;
        StreamingCheck.IsChecked = _settings.Streaming;
        ThinkingExtCheck.IsChecked = _settings.ExtendedThinking;
        ClearInputCheck.IsChecked = _settings.ClearInputAfterSend;
        CompactCheck.IsChecked = _settings.CompactRows;
        ShadowCheck.IsChecked = _settings.ShowShadow;
        MaxTokensBox.Text = _settings.MaxTokensOverride > 0
            ? _settings.MaxTokensOverride.ToString()
            : "";
        PromptBox.Text = _settings.SystemPrompt;

        WidthSlider.Value = _settings.WindowWidth;
        TopSlider.Value = _settings.TopPercent;
        FontSlider.Value = _settings.FontSize;
        OpacitySlider.Value = _settings.Opacity;
        CornerSlider.Value = _settings.CornerRadius;
        FontBox.SelectedItem = ItemWithTag(FontBox, _settings.FontFamily) ?? FontBox.Items[0];
        CodeFontSlider.Value = _settings.CodeFontSize;

        TempSlider.Value = _settings.Temperature;
        TopPSlider.Value = _settings.TopP;
        TimeoutSlider.Value = _settings.TimeoutSeconds;
        RetrySlider.Value = _settings.Retries;
        SuggestionSlider.Value = _settings.MaxSuggestions;
        AnimIntensitySlider.Value = _settings.AnimationIntensity;

        AnimateCheck.IsChecked = _settings.AnimateBar;
        ThinkingCheck.IsChecked = _settings.ShowThinking;
        MetaCheck.IsChecked = _settings.ShowMeta;

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
        CornerValue.Text = $"{CornerSlider.Value:0}";
        CodeFontValue.Text = $"{CodeFontSlider.Value:0}";
        TempValue.Text = $"{TempSlider.Value:0.0}";
        TopPValue.Text = $"{TopPSlider.Value:0.00}";
        TimeoutValue.Text = $"{TimeoutSlider.Value:0} s";
        RetryValue.Text = $"{RetrySlider.Value:0}";
        SuggestionValue.Text = $"{SuggestionSlider.Value:0}";
        AnimIntensityValue.Text = $"{AnimIntensitySlider.Value:0.00}×";

        // Die Vorschau folgt auch dem Ecken-Radius — so sieht man sofort,
        // wie rund die Leiste wirklich wird.
        PreviewCard.CornerRadius = new CornerRadius(CornerSlider.Value);

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
        RevealPage(_pages[key]);
    }

    /// <summary>
    /// Die gewechselte Seite taucht auf, statt zu springen: kurzes Einblenden mit
    /// einem Hauch Auftrieb — dieselbe Bewegung wie in der Leiste.
    ///
    /// Nur im echten Fenster: Beim Rendern für Prüfstand und Bilder wären halb
    /// sichtbare Seiten wertlos, deshalb dort ohne Bewegung.
    /// </summary>
    private void RevealPage(FrameworkElement page)
    {
        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 1;

        if (page.RenderTransform is TranslateTransform previous)
        {
            previous.BeginAnimation(TranslateTransform.YProperty, null);
            previous.Y = 0;
        }

        // Ausserhalb eines echten Fensters (Prüfstand, Bilder) und bei
        // abgeschalteter Bewegung bleibt die Seite einfach stehen.
        if (!IsVisible || !_settings.AnimateBar)
        {
            return;
        }

        var shift = page.RenderTransform as TranslateTransform;
        if (shift is null)
        {
            shift = new TranslateTransform();
            page.RenderTransform = shift;
        }

        page.Opacity = 0;
        shift.Y = Motion.PageRise;
        page.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.PageMs));
        shift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(Motion.PageRise, 0, Motion.After(Motion.PageMs))
            {
                EasingFunction = Motion.Out,
            });
    }

    /// <summary>
    /// Der Auftritt des Fensters selbst: kurz einblenden und einen Hauch aufsteigen.
    /// Wird nur beim Öffnen aus dem Programm heraus aufgerufen — nicht beim
    /// Rendern für Prüfstand oder Bilder.
    /// </summary>
    public void PlayEntrance()
    {
        Root.BeginAnimation(OpacityProperty, null);
        RootShift.BeginAnimation(TranslateTransform.YProperty, null);
        Root.Opacity = 1;
        RootShift.Y = 0;

        if (!IsVisible || !_settings.AnimateBar)
        {
            return;
        }

        Root.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.ShowMs));
        RootShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(Motion.PageRise, 0, Motion.ShowDuration) { EasingFunction = Motion.Out });
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

        _settings.AnswerLength = TagOf(LengthBox, "mittel");
        _settings.ReasoningEffort = TagOf(ReasoningBox, "default");
        _settings.MemoryRounds = int.TryParse(TagOf(MemoryBox, "3"), out var rounds) ? rounds : 3;
        _settings.KeepHistoryDays = int.TryParse(TagOf(KeepBox, "0"), out var days) ? days : 0;
        _settings.HistoryEntries = int.TryParse(TagOf(HistoryBox, "500"), out var entries) ? entries : 500;
        _settings.Theme = TagOf(ThemeBox, "system");
        _settings.FontFamily = TagOf(FontBox, "system");
        _settings.AnimateBar = AnimateCheck.IsChecked == true;
        _settings.ShowThinking = ThinkingCheck.IsChecked == true;
        _settings.ShowMeta = MetaCheck.IsChecked == true;
        _settings.ShowShadow = ShadowCheck.IsChecked == true;
        _settings.CompactRows = CompactCheck.IsChecked == true;
        _settings.WebSearch = WebSearchCheck.IsChecked == true;
        _settings.Streaming = StreamingCheck.IsChecked == true;
        _settings.ExtendedThinking = ThinkingExtCheck.IsChecked == true;
        _settings.ClearInputAfterSend = ClearInputCheck.IsChecked == true;
        _settings.MaxTokensOverride = int.TryParse(MaxTokensBox.Text.Trim(), out var max) ? max : 0;
        _settings.BaseUrl = BaseUrlBox.Text.Trim();
        _settings.Provider = Provider.Id;
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
        _settings.CornerRadius = Math.Round(CornerSlider.Value);
        _settings.CodeFontSize = Math.Round(CodeFontSlider.Value);
        _settings.Temperature = Math.Round(TempSlider.Value, 2);
        _settings.TopP = Math.Round(TopPSlider.Value, 2);
        _settings.TimeoutSeconds = (int)Math.Round(TimeoutSlider.Value);
        _settings.Retries = (int)Math.Round(RetrySlider.Value);
        _settings.MaxSuggestions = (int)Math.Round(SuggestionSlider.Value);
        _settings.AnimationIntensity = Math.Round(AnimIntensitySlider.Value, 2);
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

    // ── Anbieter und Modell ────────────────────────────────────────────────

    /// <summary>
    /// Anbieterwechsel: Adresse, Schlüssel und Modellliste hängen daran. Jeder
    /// Anbieter hat seinen eigenen Schlüssel und seine eigene letzte Modellwahl.
    /// </summary>
    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var id = TagOf(ProviderBox, "groq");
        if (_settings.Provider != id)
        {
            _settings.Provider = id;

            // Die eigene Adresse gehört zu dem Anbieter, für den sie eingetragen
            // wurde — für den neuen gilt wieder seine Standardadresse.
            _settings.BaseUrl = "";
            BaseUrlBox.Text = "";
            _extraModels.Clear();
            ModelStatus.Text = "";
            _keyShownOnce = false;
        }

        UpdateProviderInfo();
        ReloadModels();
        UpdateKeyStatus();
    }

    /// <summary>Ein Modell aus der Liste — die Wahl wird je Anbieter gemerkt.</summary>
    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var chosen = TagOf(ModelBox, Providers.Auto);
        _settings.SetModelFor(Provider.Id, chosen);
        ModelNameBox.Text = chosen == Providers.Auto ? "" : chosen;
    }

    /// <summary>Ein selbst eingetippter Modellname schlägt die Liste.</summary>
    private void OnModelNameChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var name = ModelNameBox.Text.Trim();
        _settings.SetModelFor(Provider.Id, name.Length == 0 ? Providers.Auto : name);
        ReloadModels();
    }

    /// <summary>
    /// Die Modelle abfragen, die der Anbieter (oder ein lokaler Server) wirklich
    /// anbietet — damit niemand Namen raten muss.
    /// </summary>
    private async void OnLoadModels(object sender, RoutedEventArgs e)
    {
        var provider = Provider;
        LoadModelsButton.IsEnabled = false;
        ModelStatus.Text = $"Frage die Modellliste von {provider.Name} ab …";

        try
        {
            using var clock = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var names = await _chat.AvailableModelsAsync(clock.Token);
            _extraModels.Clear();
            _extraModels.AddRange(names);
            ReloadModels();
            ModelStatus.Text = names.Count == 0
                ? "Der Anbieter hat keine Liste zurückgegeben — der Name lässt sich von Hand eintippen."
                : $"{names.Count} Modell(e) geladen: {Joined(names)}";
        }
        catch (Exception ex)
        {
            ModelStatus.Text = "Die Liste kam nicht an: " + ex.Message;
        }
        finally
        {
            LoadModelsButton.IsEnabled = true;
        }
    }

    /// <summary>Die ersten Namen einer Liste, damit die Meldung kurz bleibt.</summary>
    private static string Joined(IReadOnlyList<string> names)
    {
        var head = names.Take(4).ToList();
        var text = string.Join(", ", head);
        return names.Count > head.Count ? text + " …" : text;
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

        if (!Provider.NeedsKey)
        {
            KeyStatus.Text = $"{Provider.Name} braucht keinen Schlüssel — das Feld darf leer bleiben.";
            return;
        }

        KeyStatus.Text = _chat.HasKey
            ? $"Für {Provider.Name} ist ein Schlüssel gespeichert."
            : $"Für {Provider.Name} ist noch kein Schlüssel gespeichert — ohne ihn kann AIlight hier nichts fragen.";
    }

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var value = KeyBox.Password.Trim();
        if (value.Length == 0)
        {
            KeyStatus.Text = "Bitte einen Schlüssel eingeben. " + Provider.KeyHint;
            return;
        }

        KeyStatus.Text = SecretStore.Write(Provider.Id, value)
            ? $"Schlüssel für {Provider.Name} gespeichert. Mit „Schlüssel testen“ prüfen wir ihn sofort."
            : "Der Schlüssel konnte nicht gespeichert werden — Details stehen im Protokoll.";
    }

    private void OnDeleteKey(object sender, RoutedEventArgs e)
    {
        SecretStore.Delete(Provider.Id);
        KeyBox.Password = "";
        UpdateKeyStatus();
        KeyStatus.Text = $"Der Schlüssel für {Provider.Name} ist gelöscht — die anderen bleiben.";
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
            var message = await _chat.TestKeyAsync(key, _keyTest.Token);
            KeyStatus.Text = message;
            SecretStore.Write(Provider.Id, key);
            UpdateKeyStatus();
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
            + $"{Providers.FormatCost(cost)} · Datei {bytes / 1024.0:0.0} kB";
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

    private void OnOpenKeys(object sender, RoutedEventArgs e) => Open(Provider.KeyUrl);

    private void OnOpenRepo(object sender, RoutedEventArgs e)
        => Open("https://github.com/rian2acc/AiLight");

    /// <summary>Die Einführung erneut zeigen — ohne die Einstellungen zu schließen.</summary>
    private void OnOpenWelcome(object sender, RoutedEventArgs e)
        => (Application.Current as App)?.OpenWelcome();

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
