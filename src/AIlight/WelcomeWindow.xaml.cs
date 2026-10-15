using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using AIlight.Core;
using AIlight.Groq;
using AIlight.Views;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AIlight;

/// <summary>Eine Zeile der Befehlsliste in der Einführung.</summary>
public sealed record WelcomeCommand(string Name, string Summary);

/// <summary>Eine Zeile der Tastenliste in der Einführung.</summary>
public sealed record WelcomeKey(string Keys, string What);

/// <summary>
/// Die Einführung: sieben Schritte, die alles Nötige erklären — vom Schlüssel
/// über den Hotkey bis zu Befehlen und Aussehen. Sie erscheint genau einmal:
/// beim ersten Start nach der Installation.
///
/// Aufbau und Bewegung folgen dem Haus: links die Schritte, rechts die Seite,
/// unten das Weiter. Jeder Schrittwechsel blendet die neue Seite ein und lässt
/// sie einen Hauch aufsteigen — in Laufrichtung, damit „zurück“ sich auch wie
/// zurück anfühlt.
/// </summary>
public partial class WelcomeWindow : Window
{
    /// <summary>Die Beispiel-Frage auf der Seite „Fragen stellen“.</summary>
    private const string DemoText = "Was ist Strom?";

    private readonly AppSettings _settings;
    private readonly ChatService _chat;
    private readonly Dictionary<string, ToggleButton> _nav = new();
    private readonly Dictionary<string, StackPanel> _pages = new();
    private readonly List<string> _order = new();
    private readonly List<Ellipse> _dots = new();
    private readonly DispatcherTimer _demo;
    private readonly List<WelcomeCommand> _commands = new();

    private string _step = "willkommen";
    private int _demoChars;
    private int _demoPhase;
    private DateTime _demoUntil = DateTime.Now;
    private bool _loading = true;

    public WelcomeWindow(AppSettings settings, ChatService chat)
    {
        _settings = settings;
        _chat = chat;

        InitializeComponent();

        // Nie größer als der Bildschirm — auf kleinen Anzeigen bleibt sonst der
        // untere Rand samt „Weiter“ außerhalb des Bildes.
        var work = SystemParameters.WorkArea;
        MaxWidth = Math.Max(720, work.Width - 60);
        MaxHeight = Math.Max(500, work.Height - 60);
        Width = Math.Min(900, MaxWidth);
        Height = Math.Min(600, MaxHeight);

        Collect();
        BuildAccents();
        BuildCommands();
        BuildDots();
        LoadValues();
        ShowStep("willkommen", forward: true, animate: false);
        _loading = false;

        // Die Vorführung auf der Fragen-Seite: tippen, antworten, warten, von vorn.
        _demo = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60),
        };
        _demo.Tick += OnDemoTick;
        _demo.Start();
    }

    // ── Prüfstand (nur für den Selbsttest) ──────────────────────────────────

    internal IReadOnlyList<string> StepKeysForTest => _order.ToList();

    internal string VisibleStepForTest => _step;

    internal bool IsStepVisibleForTest(string key)
        => _pages.TryGetValue(key, out var page) && page.Visibility == Visibility.Visible;

    internal void GoToStepForTest(string key) => ShowStep(key, forward: true, animate: false);

    internal void NextForTest() => OnNext(this, new RoutedEventArgs());

    internal void BackForTest() => OnBack(this, new RoutedEventArgs());

    internal string NextLabelForTest => NextButton.Content as string ?? "";

    internal bool BackEnabledForTest => BackButton.IsEnabled;

    internal double StepOpacityForTest(string key) => _pages[key].Opacity;

    internal bool StepAnimatedForTest(string key) => _pages[key].HasAnimatedProperties;

    internal FrameworkElement MarkForTest => BrandMark;

    internal IReadOnlyList<string> CommandNamesForTest => _commands.Select(line => line.Name).ToList();

    internal bool DemoRunningForTest => _demo.IsEnabled && _step == "fragen";

    internal string DemoQuestionForTest => DemoQuestion.Text;

    internal void DemoStepForTest() => OnDemoTick(this, EventArgs.Empty);

    /// <summary>
    /// Die Vorführung fertig hinstellen — für Bilder und Prüfstand, wo nichts
    /// tippen kann. Im Betrieb tippt sie sich selbst.
    /// </summary>
    internal void FillDemoForShot()
    {
        _demoChars = DemoText.Length;
        _demoPhase = 1;
        DemoQuestion.Text = DemoText;
        Anim.FadeNow(DemoAnswer, 1);
        Anim.SlideNow(DemoAnswer, 0);
    }

    internal double RootOpacityForTest => Root.Opacity;

    internal double RootShiftForTest => RootShift.Y;

    // ── Aufbau ──────────────────────────────────────────────────────────────

    private void Collect()
    {
        _nav["willkommen"] = NavWillkommen;
        _nav["schluessel"] = NavSchluessel;
        _nav["hotkey"] = NavHotkey;
        _nav["fragen"] = NavFragen;
        _nav["befehle"] = NavBefehle;
        _nav["aussehen"] = NavAussehen;
        _nav["fertig"] = NavFertig;

        _pages["willkommen"] = PageWillkommen;
        _pages["schluessel"] = PageSchluessel;
        _pages["hotkey"] = PageHotkey;
        _pages["fragen"] = PageFragen;
        _pages["befehle"] = PageBefehle;
        _pages["aussehen"] = PageAussehen;
        _pages["fertig"] = PageFertig;

        _order.AddRange(new[]
        {
            "willkommen", "schluessel", "hotkey", "fragen", "befehle", "aussehen", "fertig",
        });
    }

    /// <summary>
    /// Die Akzentfarben zum Anklicken — dieselbe Liste wie in den Einstellungen,
    /// damit beide Fenster nie auseinanderlaufen.
    /// </summary>
    private void BuildAccents()
    {
        foreach (var (label, hex) in SettingsWindow.Accents)
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
                    // Eine Änderung wirkt sofort im ganzen Programm (DynamicResource).
                    _settings.Accent = value;
                    UpdateSwatches();
                }
            };

            AccentPanel.Children.Add(swatch);
        }

        UpdateSwatches();
    }

    private void UpdateSwatches()
    {
        foreach (var child in AccentPanel.Children.OfType<ToggleButton>())
        {
            child.IsChecked = string.Equals(child.Tag as string, _settings.Accent,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Befehle und Tasten werden aus der Quelle gebaut, nicht abgeschrieben:
    /// Ein neuer Befehl steht damit automatisch auch in der Einführung.
    /// </summary>
    private void BuildCommands()
    {
        foreach (var command in Commands.All)
        {
            var argument = command.ArgumentHint.Length > 0 ? " " + command.ArgumentHint : "";
            _commands.Add(new WelcomeCommand($"/{command.Name}{argument}", command.Summary));
        }

        CommandList.ItemsSource = _commands;
        KeyList.ItemsSource = new[]
        {
            new WelcomeKey("Enter", "Frage senden oder markierten Vorschlag ausführen"),
            new WelcomeKey("Esc", "Auswahl löschen · laufende Antwort abbrechen · Leiste schließen"),
            new WelcomeKey("↑ ↓", "Vorschlag wählen · Tab übernimmt ihn ins Eingabefeld"),
            new WelcomeKey("Bild ↑ ↓", "in der Liste seitenweise springen"),
            new WelcomeKey("Strg + ↑ ↓", "frühere Fragen durchblättern"),
            new WelcomeKey("Strg + C", "Antwort in die Zwischenablage kopieren"),
            new WelcomeKey("Strg + N", "neues Gespräch beginnen"),
            new WelcomeKey("Strg + H", "Verlauf durchsuchen"),
            new WelcomeKey("Strg + ,", "Einstellungen öffnen"),
            new WelcomeKey("Esc (zweimal)", "leeres Feld schließt die Leiste"),
        };
    }

    private void BuildDots()
    {
        for (var index = 0; index < _order.Count; index++)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0) };
            dot.SetResourceReference(Shape.FillProperty, "Brush.Separator");
            _dots.Add(dot);
            DotPanel.Children.Add(dot);
        }
    }

    private void LoadValues()
    {
        StartCheck.IsChecked = _settings.StartWithWindows;
        PreloadCheck.IsChecked = _settings.PreloadBar;
        UpdateKeyStatus();
    }

    private void UpdateKeyStatus()
    {
        KeyStatus.Text = _chat.HasKey
            ? "✓ Ein Schlüssel ist gespeichert — du kannst sofort fragen."
            : "Noch kein Schlüssel gespeichert. Ohne ihn beantwortet AIlight keine Fragen.";
    }

    // ── Blättern ────────────────────────────────────────────────────────────

    private int IndexOf(string key) => Math.Max(0, _order.IndexOf(key));

    private void OnStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string key })
        {
            ShowStep(key, forward: IndexOf(key) >= IndexOf(_step));
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        var next = IndexOf(_step) + 1;
        if (next >= _order.Count)
        {
            Close();
            return;
        }

        ShowStep(_order[next], forward: true);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        var previous = IndexOf(_step) - 1;
        if (previous < 0)
        {
            return;
        }

        ShowStep(_order[previous], forward: false);
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        // Überspringen ist keine Absage: der Stand bleibt, gesehen ist gesehen.
        Log.Info("Einführung übersprungen");
        Close();
    }

    /// <summary>
    /// Einen Schritt zeigen. Die neue Seite kommt aus der Richtung, in die
    /// geblättert wurde: vorwärts von unten, rückwärts von oben.
    /// </summary>
    private void ShowStep(string key, bool forward, bool animate = true)
    {
        if (!_pages.ContainsKey(key))
        {
            key = _order[0];
        }

        var page = _pages[key];
        _step = key;

        foreach (var (stepKey, other) in _pages)
        {
            if (stepKey != key)
            {
                other.Visibility = Visibility.Collapsed;
                Anim.Reset(other);
            }
        }

        page.Visibility = Visibility.Visible;
        foreach (var (stepKey, button) in _nav)
        {
            button.IsChecked = stepKey == key;
        }

        PageScroll.ScrollToTop();
        UpdateFooter();
        Anim.Reset(page);

        if (key != "fragen")
        {
            // Die Vorführung gehört nur auf ihre Seite.
            ResetDemo();
        }
        else if (!IsVisible)
        {
            // Ohne Fenster (Bilder, Prüfstand) steht die Vorführung fertig da.
            FillDemoForShot();
        }

        if (!animate || !IsVisible || !Motion.Moves)
        {
            return;
        }

        var rise = forward ? Motion.PageRise + 4 : -(Motion.PageRise + 4);
        Anim.FadeNow(page, 0);
        Anim.SlideNow(page, rise);
        Anim.Fade(page, 1, Motion.PageMs, Motion.Soft);
        Anim.Slide(page, rise, 0, Motion.PageMs, Motion.Soft);
    }

    private void UpdateFooter()
    {
        var index = IndexOf(_step);
        StepCount.Text = $"Schritt {index + 1} von {_order.Count}";
        NextButton.Content = index == _order.Count - 1 ? "Fertig" : "Weiter";
        BackButton.IsEnabled = index > 0;
        SkipButton.Visibility = index == _order.Count - 1 ? Visibility.Collapsed : Visibility.Visible;

        for (var dot = 0; dot < _dots.Count; dot++)
        {
            _dots[dot].SetResourceReference(
                Shape.FillProperty, dot == index ? "Brush.Accent" : "Brush.Separator");
        }
    }

    // ── Fensterrahmen ───────────────────────────────────────────────────────

    /// <summary>
    /// Der Auftritt des Fensters — nur beim echten Öffnen, damit Prüfstand und
    /// Bilder ein fertiges Fenster vorfinden.
    /// </summary>
    public void PlayEntrance()
    {
        Anim.Reset(Root);
        Anim.FadeNow(Root, 1);
        Anim.SlideNow(Root, 0);

        if (!IsVisible || !Motion.Moves)
        {
            return;
        }

        Anim.FadeNow(Root, 0);
        Anim.Fade(Root, 1, Motion.ShowMs, Motion.Soft);
        Anim.Slide(Root, Motion.PageRise + 6, 0, Motion.ShowMs, Motion.Soft);
        Anim.Pulse(BrandMark, 1.12, Motion.BlockMs, Motion.Spring);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateKeyStatus();
        StartCaret();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                return;

            case Key.Right:
            case Key.PageDown:
                e.Handled = true;
                OnNext(this, new RoutedEventArgs());
                return;

            case Key.Left:
            case Key.PageUp:
                e.Handled = true;
                OnBack(this, new RoutedEventArgs());
                return;

            // Enter blättert weiter, außer ein Knopf hat gerade die Tastatur —
            // dann gehört das Enter ihm.
            case Key.Enter when Keyboard.FocusedElement is not Button:
                e.Handled = true;
                OnNext(this, new RoutedEventArgs());
                return;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _demo.Stop();
        _demo.Tick -= OnDemoTick;
        Log.Info("Einführung geschlossen");
        base.OnClosed(e);
    }

    // ── Die Vorführung ──────────────────────────────────────────────────────

    private void StartCaret()
    {
        DemoCaret.BeginAnimation(OpacityProperty, null);
        DemoCaret.Opacity = 1;
        if (!Motion.Moves)
        {
            return;
        }

        var blink = new DoubleAnimation(1, 0.1, Motion.After(Motion.DotMs))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        DemoCaret.BeginAnimation(OpacityProperty, blink);
    }

    private void ResetDemo()
    {
        _demoChars = 0;
        _demoPhase = 0;
        DemoQuestion.Text = "";
        Anim.FadeNow(DemoAnswer, 0);
        Anim.SlideNow(DemoAnswer, 0);
    }

    /// <summary>
    /// Die kleine Vorführung: die Frage tippt sich selbst, danach kommt die
    /// Antwort, dann fängt es von vorn an. Sie läuft nur, wenn man sie sieht —
    /// und nur, wenn Bewegung erwünscht ist.
    /// </summary>
    private void OnDemoTick(object? sender, EventArgs e)
    {
        if (_step != "fragen" || !IsVisible || !Motion.Moves || WindowState != WindowState.Normal)
        {
            return;
        }

        switch (_demoPhase)
        {
            case 0 when _demoChars < DemoText.Length:
                _demoChars++;
                DemoQuestion.Text = DemoText[.._demoChars];
                return;

            case 0:
                _demoPhase = 1;
                Anim.FadeNow(DemoAnswer, 0);
                Anim.SlideNow(DemoAnswer, Motion.RowRise);
                Anim.Fade(DemoAnswer, 1, Motion.BlockMs, Motion.Soft);
                Anim.Slide(DemoAnswer, Motion.RowRise, 0, Motion.BlockMs, Motion.Soft);
                _demoUntil = DateTime.Now.AddMilliseconds(3200);
                return;

            case 1 when DateTime.Now >= _demoUntil:
                _demoPhase = 2;
                Anim.Fade(DemoAnswer, 0, Motion.QuickMs, Motion.In);
                _demoUntil = DateTime.Now.AddMilliseconds(600);
                return;

            case 2 when DateTime.Now >= _demoUntil:
                ResetDemo();
                return;
        }
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.StartWithWindows = StartCheck.IsChecked == true;
        _settings.PreloadBar = PreloadCheck.IsChecked == true;
    }

    // ── Verweise nach draußen ───────────────────────────────────────────────

    private void OnOpenKeys(object sender, RoutedEventArgs e)
        => Open("https://console.groq.com/keys");

    private void OnOpenAccount(object sender, RoutedEventArgs e) => OpenSettings("konto");

    private void OnOpenBehaviour(object sender, RoutedEventArgs e) => OpenSettings("verhalten");

    private void OnOpenAppearance(object sender, RoutedEventArgs e) => OpenSettings("aussehen");

    private void OpenSettings(string area)
    {
        var app = Application.Current as App;
        app?.OpenSettings(area);
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Verweis konnte nicht geöffnet werden: {ex.Message}");
        }
    }
}
