using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AIlight.Core;
using AIlight.Groq;
using AIlight.Views;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;

namespace AIlight;

/// <summary>
/// Die Leiste: eine Zeile zum Fragen, darunter Vorschläge und die gestreamte
/// Antwort. Sie kennt keinen Plugins, keine Verwaltung — nur Fragen und Antworten.
/// </summary>
public partial class MainWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ChatService _chat;
    private readonly AppSettings _settings;
    private readonly HistoryStore _history;
    private readonly ObservableCollection<SuggestionRow> _rows = new();
    private readonly StringBuilder _answer = new();
    private readonly StringBuilder _thinking = new();

    private CancellationTokenSource? _cancel;
    private DispatcherTimer? _paint;
    private DispatcherTimer? _hideLater;
    private bool _thinkingAutoExpanded;
    private string _lastQuestion = "";
    private AskResult? _lastResult;
    private bool _streaming;
    private bool _arrowKeys;
    private int _selected;
    private IntPtr _previousWindow;
    private DateTime _startedAt;

    public MainWindow(ChatService chat)
    {
        _chat = chat;
        _settings = chat.Settings;
        _history = chat.History;

        InitializeComponent();
        SuggestionList.ItemsSource = _rows;
        SuggestionList.MouseMove += OnSuggestionMouseMove;
        SuggestionList.MouseLeftButtonUp += OnSuggestionClick;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    /// <summary>Zeile unter dem Mauszeiger (oder -1).</summary>
    private int RowUnderMouse()
    {
        if (_rows.Count == 0)
        {
            return -1;
        }

        var position = Mouse.GetPosition(SuggestionList);
        var hit = SuggestionList.InputHitTest(position) as DependencyObject;
        while (hit is not null and not ContentPresenter)
        {
            hit = VisualTreeHelper.GetParent(hit);
        }

        return hit is ContentPresenter presenter
            ? SuggestionList.ItemContainerGenerator.IndexFromContainer(presenter)
            : -1;
    }

    /// <summary>Auswahl nur mit der Maus, solange nicht mit den Pfeiltasten gewählt wird.</summary>
    private void OnSuggestionMouseMove(object sender, MouseEventArgs e)
    {
        if (_arrowKeys)
        {
            return;
        }

        var index = RowUnderMouse();
        if (index >= 0 && index != _selected)
        {
            _selected = index;
            UpdateSelectionVisual();
        }
    }

    private void OnSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        var index = RowUnderMouse();
        if (index >= 0 && index < _rows.Count)
        {
            _selected = index;
            UpdateSelectionVisual();
            Activate(_rows[index].Item);
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            _arrowKeys = false;
        }
    }

    // ── Prüfstand (nur für den Selbsttest) ──────────────────────────────────

    internal int SuggestionCount => _rows.Count;

    internal int SuggestionCountForTest => _rows.Count;

    /// <summary>Anzahl der Trennlinien in der Vorschlagsliste (Prüfstand).</summary>
    internal int SuggestionSeparatorCountForTest => _rows.Count(row => row.ShowSeparator);

    internal IReadOnlyList<bool> SuggestionSeparatorsForTest
        => _rows.Select(row => row.ShowSeparator).ToList();

    internal bool IsStreamingForTest => _streaming;

    internal string AnswerTextForTest
    {
        get
        {
            lock (_answer)
            {
                return _answer.ToString();
            }
        }
    }

    internal string MetaTextForTest => Meta.Text;

    internal string HintTextForTest => Hint.Text;

    /// <summary>Wie ein Enter im Eingabefeld — für den Prüfstand.</summary>
    internal void SubmitForTest() => ActivateSelectedOrAsk();

    internal void SetInputForTest(string text) => Input.Text = text;

    internal static string FooterForTest(AskResult result) => FooterText(result);

    internal int AnswerBlockCount => AnswerPanel.Children.Count;

    internal void ShowTextForTest(string title, string markdown) => ShowText(title, markdown);

    /// <summary>Für Bildschirmfotos: Antwort, Fußzeile und Vorschläge in einem Zug setzen.</summary>
    internal void FillForShot(string title, string markdown, string meta)
    {
        if (markdown.Length > 0)
        {
            ShowText(title, markdown);
        }

        Meta.Text = meta;
        SetHint(markdown.Length > 0
            ? "Fertig — Strg+C kopiert, /save speichert, Esc schließt."
            : "Enter fragt · Strg+, Einstellungen · Esc schließt");
        RefreshSuggestions();
    }

    internal void ShowHistoryForTest(string term) => ShowHistory(term);

    internal string HintForTest => Hint.Text;

    /// <summary>Solange true, versteckt sich die Leiste nicht beim Fokusverlust (Einstellungen offen).</summary>
    public bool SuppressAutoHide { get; set; }

    /// <summary>Zeitpunkt des letzten Einblendens (für Messungen).</summary>
    public DateTime ShownAt { get; private set; }

    // ── Fenster-Grundlagen ──────────────────────────────────────────────────

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        // Nicht in Alt+Tab und nicht in der Taskleiste auftauchen — wie ein Launcher.
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    }

    private void OnLoaded(object sender, EventArgs e)
    {
        ApplySettings();
        RefreshSuggestions();
    }

    /// <summary>Einstellungen anwenden (Breite, Schrift, Farben, Höhen).</summary>
    public void ApplySettings()
    {
        Theme.Apply(_settings.Theme, _settings.Accent, _settings.Opacity,
            _settings.CornerRadius, _settings.FontFamily);
        Width = _settings.WindowWidth;
        Input.FontSize = _settings.FontSize;
        Placeholder.FontSize = _settings.FontSize;

        // Die Fußzeile schließt unten mit demselben Radius ab wie das Fenster.
        var shell = Math.Clamp(_settings.CornerRadius, 8, 22);
        Footer.CornerRadius = new CornerRadius(0, 0, Math.Max(0, shell - 3), Math.Max(0, shell - 3));
        Meta.Visibility = _settings.ShowMeta ? Visibility.Visible : Visibility.Collapsed;

        var working = WorkingAreaDip();
        MaxHeight = Math.Max(320, working.Height - 60);
        AnswerScroll.MaxHeight = Math.Max(180, working.Height * 0.55);
        RefreshSuggestions();
    }

    /// <summary>Arbeitsfläche des Bildschirms, auf dem die Maus steht (in WPF-Einheiten).</summary>
    private Rect WorkingAreaDip()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var scale = DeviceScale;
        return new Rect(
            screen.WorkingArea.Left / scale.X,
            screen.WorkingArea.Top / scale.Y,
            screen.WorkingArea.Width / scale.X,
            screen.WorkingArea.Height / scale.Y);
    }

    private Point DeviceScale
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is { } target)
            {
                return new Point(target.TransformToDevice.M11, target.TransformToDevice.M22);
            }

            return new Point(1, 1);
        }
    }

    /// <summary>
    /// Leiste einblenden — in der Bildschirmmitte oben, wie ein Launcher.
    ///
    /// Wichtig ist die Reihenfolge: erst steht das Fenster (schnell), danach
    /// werden Verlauf und Vorschläge geladen. Deshalb ist die Leiste auch auf
    /// einem langsamen Rechner sofort da.
    /// </summary>
    public void ShowBar()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        PlaceWindow();
        // Leicht einblenden, wie ein Spotlight — abschaltbar über die Einstellungen.
        StartBarAnimation();
        Activate();
        Input.SelectAll();
        Input.Focus();
        ShownAt = DateTime.Now;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ApplySettings();
            RefreshSuggestions();

            if (!IsActive)
            {
                // Ein zweiter Versuch: Windows gibt den Fokus erst nach dem ersten Bild frei.
                Activate();
                Input.Focus();
            }
        }));
    }

    private void PlaceWindow()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var area = screen.WorkingArea;
        var handle = new WindowInteropHelper(this).Handle;
        var x = area.Left + (int)((area.Width - Width * DeviceScale.X) / 2);
        var y = area.Top + (int)(area.Height * (_settings.TopPercent / 100.0));
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Kurzes Einblenden (oder sofort, wenn Animationen ausgeschaltet sind).</summary>
    private void StartBarAnimation()
    {
        if (!_settings.AnimateBar)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            return;
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, fade);
    }

    public void HideBar()
    {
        if (!IsVisible)
        {
            return;
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Hide();
        if (_previousWindow != IntPtr.Zero && _previousWindow != new WindowInteropHelper(this).Handle)
        {
            SetForegroundWindow(_previousWindow);
        }
    }

    public void Toggle()
    {
        if (IsVisible && IsActive)
        {
            HideBar();
            return;
        }

        ShowBar();
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        if (SuppressAutoHide || !_settings.HideOnLostFocus)
        {
            return;
        }

        // Kurz warten: ein Klick neben die Leiste soll sie schließen, ein
        // Tray-Klick (Menü schließt sich) soll sie aber nicht sofort verstecken.
        _hideLater ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _hideLater.Stop();
        _hideLater.Tick -= OnHideLater;
        _hideLater.Tick += OnHideLater;
        _hideLater.Start();
    }

    private void OnHideLater(object? sender, EventArgs e)
    {
        _hideLater?.Stop();
        if (!IsActive && IsVisible && !SuppressAutoHide)
        {
            HideBar();
        }
    }

    // ── Tastatur ────────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Escape();
                return;

            case Key.Enter:
                e.Handled = true;
                ActivateSelectedOrAsk();
                return;

            case Key.Down:
                _arrowKeys = true;
                e.Handled = true;
                if (control)
                {
                    CycleHistory(1);
                }
                else
                {
                    MoveSelection(1);
                }

                return;

            case Key.Up:
                _arrowKeys = true;
                e.Handled = true;
                if (control)
                {
                    CycleHistory(-1);
                }
                else
                {
                    MoveSelection(-1);
                }

                return;

            case Key.Tab:
                e.Handled = true;
                CompleteSelected();
                return;

            case Key.N when control:
                e.Handled = true;
                NewChat();
                return;

            case Key.H when control:
                e.Handled = true;
                ShowHistory("");
                return;

            case Key.C when control:
                bool hasAnswer;
                lock (_answer)
                {
                    hasAnswer = _answer.Length > 0;
                }

                if (!string.IsNullOrEmpty(Input.SelectedText) || !hasAnswer)
                {
                    return; // normales Kopieren im Eingabefeld
                }

                e.Handled = true;
                CopyAnswer();
                return;

            case Key.OemComma when control:
                e.Handled = true;
                ((App)Application.Current).OpenSettings();
                return;

            case Key.PageDown:
                _arrowKeys = true;
                e.Handled = true;
                MoveSelection(5);
                return;

            case Key.PageUp:
                _arrowKeys = true;
                e.Handled = true;
                MoveSelection(-5);
                return;
        }
    }

    private void Escape()
    {
        if (_streaming)
        {
            Cancel();
            return;
        }

        if (Input.Text.Length > 0)
        {
            Input.Clear();
            SetHint("Enter fragt · Strg+, Einstellungen · Esc schließt");
            RefreshSuggestions();
            return;
        }

        HideBar();
    }

    // ── Eingabe und Vorschläge ──────────────────────────────────────────────

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        var rows = SuggestionBuilder.Build(Input.Text, _history, _settings, _answer.Length > 0);
        var previous = _selected;

        _rows.Clear();
        SuggestionKind? last = null;
        foreach (var suggestion in rows)
        {
            // Ein Wechsel der Art (frühere Frage → Befehl) bekommt eine dünne Linie:
            // so sieht man auf einen Blick, was Verlauf ist und was Befehl.
            var separator = last is not null && last != suggestion.Kind;
            _rows.Add(new SuggestionRow(suggestion, separator));
            last = suggestion.Kind;
        }

        _selected = _rows.Count == 0 ? -1 : Math.Clamp(previous, 0, _rows.Count - 1);
        UpdateSelectionVisual();
        SuggestionList.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MoveSelection(int delta)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _selected = ((_selected + delta) % _rows.Count + _rows.Count) % _rows.Count;
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            _rows[index].IsSelected = index == _selected;
        }

        // Mit den Pfeiltasten kann die Auswahl aus dem sichtbaren Bereich laufen —
        // deshalb die gewählte Zeile hereinscrollen lassen. (ItemsControl kennt kein
        // ScrollIntoView; der Container selbst holt sich ins Sichtfeld.)
        if (_selected >= 0
            && _selected < _rows.Count
            && SuggestionList.ItemContainerGenerator.ContainerFromIndex(_selected) is FrameworkElement container)
        {
            container.BringIntoView();
        }
    }

    private void CompleteSelected()
    {
        if (_selected < 0 || _selected >= _rows.Count)
        {
            return;
        }

        var suggestion = _rows[_selected].Item;
        switch (suggestion.Kind)
        {
            case SuggestionKind.Command:
                Input.Text = "/" + suggestion.Payload;
                Input.CaretIndex = Input.Text.Length;
                break;

            case SuggestionKind.History:
                if (_history.ById(suggestion.Payload) is { } entry)
                {
                    Input.Text = entry.Question;
                    Input.CaretIndex = Input.Text.Length;
                }

                break;
        }
    }

    private void ActivateSelectedOrAsk()
    {
        if (_selected >= 0 && _selected < _rows.Count)
        {
            var suggestion = _rows[_selected].Item;
            if (suggestion.Kind != SuggestionKind.Ask || Input.Text.Trim().Length == 0)
            {
                Activate(suggestion);
                return;
            }
        }

        var question = Input.Text.Trim();
        if (question.Length == 0)
        {
            if (_lastResult is not null)
            {
                CopyAnswer();
            }

            return;
        }

        if (Commands.IsCommandPrefix(question))
        {
            RunCommand(question);
            return;
        }

        Ask(question);
    }

    private void Activate(Suggestion suggestion)
    {
        switch (suggestion.Kind)
        {
            case SuggestionKind.Command:
                RunCommand("/" + suggestion.Payload);
                break;

            case SuggestionKind.History:
                if (_history.ById(suggestion.Payload) is { } entry)
                {
                    Input.Clear();
                    ShowAnswer(entry.Question, entry.Answer, new AskResult(
                        entry.Answer, "", entry.Model, entry.TokensIn, entry.TokensOut,
                        entry.CostUsd, TimeSpan.Zero, "history", false, false));
                    SetHint($"Aus dem Verlauf vom {entry.WhenText} — Enter fragt neu, Strg+C kopiert");
                }

                break;

            case SuggestionKind.Ask:
                Ask(suggestion.Payload);
                break;
        }
    }

    // ── Befehle ─────────────────────────────────────────────────────────────

    private void RunCommand(string text)
    {
        var split = Commands.Split(text);
        if (split is null)
        {
            SetHint("Unbekannter Befehl — /help zeigt alle.");
            RefreshSuggestions();
            return;
        }

        var (command, argument) = split.Value;
        switch (command.Name)
        {
            case "new":
                NewChat();
                break;

            case "clip":
                AskClipboard();
                break;

            case "history":
                ShowHistory(argument);
                break;

            case "forget":
                ForgetHistory(argument);
                break;

            case "stats":
                ShowStats();
                break;

            case "model":
                SwitchModel(argument);
                break;

            case "prompt":
                ShowText("System-Prompt", _chat.SystemPrompt());
                break;

            case "conversation":
                ShowConversation();
                break;

            case "copy":
                CopyAnswer();
                break;

            case "save":
                SaveAnswer();
                break;

            case "settings":
                ((App)Application.Current).OpenSettings();
                break;

            case "help":
                ShowText("Befehle", HelpText());
                break;

            case "quit":
                Application.Current.Shutdown();
                break;
        }
    }

    private void NewChat()
    {
        _chat.NewChat();
        Input.Clear();
        lock (_answer)
        {
            _answer.Clear();
        }

        lock (_thinking)
        {
            _thinking.Clear();
        }

        _lastResult = null;
        _lastQuestion = "";
        _thinkingAutoExpanded = false;
        StopSpinner();
        AnswerBox.Visibility = Visibility.Collapsed;
        ThinkingBox.Visibility = Visibility.Collapsed;
        Meta.Text = "";
        SetHint("Neues Gespräch — das Gedächtnis ist leer.");
        RefreshSuggestions();
        Input.Focus();
    }

    private void SwitchModel(string argument)
    {
        var value = argument.Trim().ToLowerInvariant();
        var choice = value switch
        {
            "" or "auto" => GroqModels.Auto,
            "20b" or "klein" or "schnell" => GroqModels.Small.Id,
            "120b" or "gross" or "groß" or "stark" => GroqModels.Large.Id,
            _ => "",
        };

        if (choice.Length == 0)
        {
            SetHint("Bitte /model auto, /model 20b oder /model 120b.");
            return;
        }

        _settings.Model = choice;
        var label = choice == GroqModels.Auto ? "automatisch" : GroqModels.ById(choice).Label;
        SetHint($"Modell: {label}");
        RefreshSuggestions();
    }

    private void AskClipboard()
    {
        var text = ReadClipboard();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetHint("Die Zwischenablage ist leer — erst etwas kopieren, dann /clip.");
            return;
        }

        var clipped = text.Length > 4000 ? text[..4000] : text;
        Ask("Beantworte oder erkläre das Folgende:\n\n" + clipped, display: "Frage zur Zwischenablage");
    }

    private void ForgetHistory(string term)
    {
        var removed = _history.Delete(term);
        SetHint(removed == 0
            ? "Nichts gelöscht — kein passender Eintrag gefunden."
            : $"{removed} Eintrag/Einträge gelöscht.");
        RefreshSuggestions();
    }

    /// <summary>Vom Tray aus aufgerufen: Leiste zeigen und den Verlauf öffnen.</summary>
    public void ShowHistoryFromTray() => ShowHistory("");

    private void ShowHistory(string term)
    {
        var entries = _history.Search(term, Math.Max(5, _settings.MaxSuggestions + 4));
        if (entries.Count == 0)
        {
            SetHint(term.Length == 0 ? "Der Verlauf ist noch leer." : $"Kein Eintrag zu „{term}“.");
            return;
        }

        _rows.Clear();
        foreach (var entry in entries)
        {
            _rows.Add(new SuggestionRow(Suggestion.ForHistory(entry)));
        }

        _selected = 0;
        UpdateSelectionVisual();
        SuggestionList.Visibility = Visibility.Visible;
        SetHint($"{entries.Count} Eintrag/Einträge — Enter zeigt die Antwort, /forget <Wort> löscht.");
        Input.Focus();
    }

    private void ShowStats()
    {
        var (count, tokensIn, tokensOut, cost, bytes) = _history.Stats();
        var text = $"""
            **Nutzung**

            - Antworten im Verlauf: **{count}**
            - Tokens gesamt: **{tokensIn} → {tokensOut}**
            - Geschätzte Kosten: **{GroqModels.FormatCost(cost)}**
            - Verlaufsdatei: **{bytes / 1024.0:0.0} kB**
            """;
        ShowText("Statistik", text);
    }

    private void ShowConversation()
    {
        var rounds = _chat.Rounds;
        if (rounds.Count == 0)
        {
            ShowText("Gespräch", "_Es ist noch kein Gespräch im Gedächtnis._");
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"Im Gedächtnis: **{rounds.Count} Runde(n)**, mitgesendet werden die letzten **{_settings.MemoryRounds}**.");
        text.AppendLine();
        foreach (var round in rounds)
        {
            text.AppendLine("**Frage:** " + round.Question);
            text.AppendLine();
            text.AppendLine(round.Answer);
            text.AppendLine();
            text.AppendLine("---");
        }

        ShowText("Gespräch", text.ToString());
    }

    private string HelpText()
    {
        var text = new StringBuilder();
        text.AppendLine("**Befehle** (mit `/` oder `#`)");
        text.AppendLine();
        foreach (var command in Commands.All)
        {
            text.AppendLine($"- `{command.Display}` — {command.Summary}");
        }

        text.AppendLine();
        text.AppendLine("**Tasten**");
        text.AppendLine("- `Enter` — Frage senden oder markierten Vorschlag ausführen");
        text.AppendLine("- `Esc` — löschen, abbrechen, schließen");
        text.AppendLine("- `↑` `↓` — Vorschlag wählen · `Tab` — übernehmen");
        text.AppendLine("- `Strg+↑` `Strg+↓` — frühere Fragen durchblättern");
        text.AppendLine("- `Strg+C` — Antwort kopieren · `Strg+N` — neues Gespräch");
        text.AppendLine("- `Strg+H` — Verlauf · `Strg+,` — Einstellungen");
        return text.ToString();
    }

    // ── Fragen und Antworten ────────────────────────────────────────────────

    private void Ask(string question, string display = "")
    {
        if (_streaming)
        {
            return;
        }

        _lastQuestion = question;
        Input.Clear();
        _answer.Clear();
        _thinking.Clear();
        _lastResult = null;
        ThinkingText.Text = "";
        ThinkingBox.Visibility = Visibility.Collapsed;
        AnswerPanel.Children.Clear();
        AnswerBox.Visibility = Visibility.Visible;
        Spinner.Visibility = Visibility.Visible;
        StartSpinner();
        StopButton.Visibility = Visibility.Visible;
        _thinkingAutoExpanded = false;
        SetHint("Anfrage läuft — Esc bricht ab.");
        Meta.Text = display;

        var token = new CancellationTokenSource();
        _cancel = token;
        _streaming = true;
        _startedAt = DateTime.Now;
        StartPaintTimer();

        _ = RunAsync(question, token.Token);
    }

    private async Task RunAsync(string question, CancellationToken token)
    {
        try
        {
            var result = await _chat.AskAsync(
                question,
                context: "",
                chunk =>
                {
                    lock (_answer)
                    {
                        _answer.Append(chunk);
                    }
                },
                chunk =>
                {
                    lock (_thinking)
                    {
                        _thinking.Append(chunk);
                    }
                },
                token);

            FinishStreaming(result);
        }
        catch (GroqException ex)
        {
            FinishStreaming(null, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Frage fehlgeschlagen", ex);
            FinishStreaming(null, "Unerwarteter Fehler — siehe Log. " + ex.Message);
        }
    }

    private void FinishStreaming(AskResult? result, string? error = null)
    {
        Dispatcher.Invoke(() =>
        {
            _streaming = false;
            StopPaintTimer();
            FlushAnswer();
            StopSpinner();
            Spinner.Visibility = Visibility.Collapsed;
            StopButton.Visibility = Visibility.Collapsed;

            if (error is not null)
            {
                SetHint(error);
                Meta.Text = "";
                if (_answer.Length == 0)
                {
                    AnswerPanel.Children.Clear();
                    AnswerPanel.Children.Add(new TextBlock
                    {
                        Text = error,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("Brush.Danger"),
                        Margin = new Thickness(0, 4, 0, 4),
                    });
                }

                return;
            }

            if (result is null)
            {
                return;
            }

            _lastResult = result;
            if (result.Truncated)
            {
                SetHint("Die Antwort wurde am Token-Limit abgeschnitten — Enter setzt fort.");
            }
            else if (result.Cancelled)
            {
                SetHint("Abgebrochen — der Text bleibt stehen (Enter setzt fort).");
            }
            else
            {
                SetHint("Fertig — Strg+C kopiert, /save speichert, Esc schließt.");
            }

            Meta.Text = FooterText(result);
            RefreshSuggestions();
        });
    }

    private static string FooterText(AskResult result)
    {
        var parts = new List<string>
        {
            result.Model[(result.Model.LastIndexOf('/') + 1)..],
            $"{result.InputTokens} → {result.OutputTokens} Tokens",
            GroqModels.FormatCost(result.CostUsd),
        };

        if (result.Duration > TimeSpan.Zero)
        {
            parts.Add($"{result.Duration.TotalSeconds:0.0} s".Replace('.', ','));
        }

        if (result.FinishReason == "length")
        {
            parts.Add("am Limit abgeschnitten");
        }

        return string.Join(" · ", parts);
    }

    private void Cancel()
    {
        _cancel?.Cancel();
        SetHint("Abbruch angefordert — der bisherige Text bleibt.");
    }

    private void OnStopClicked(object sender, RoutedEventArgs e) => Cancel();

    private void StartPaintTimer()
    {
        _paint ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(70) };
        _paint.Tick -= OnPaintTick;
        _paint.Tick += OnPaintTick;
        _paint.Start();
    }

    private void StopPaintTimer() => _paint?.Stop();

    /// <summary>Der kleine Punkt atmet, solange eine Antwort läuft.</summary>
    private void StartSpinner()
    {
        var pulse = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(560))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Spinner.Opacity = 1;
        if (_settings.AnimateBar)
        {
            Spinner.BeginAnimation(UIElement.OpacityProperty, pulse);
        }
    }

    private void StopSpinner()
    {
        Spinner.BeginAnimation(UIElement.OpacityProperty, null);
        Spinner.Opacity = 1;
    }

    private void OnPaintTick(object? sender, EventArgs e)
    {
        FlushAnswer();
        var seconds = (DateTime.Now - _startedAt).TotalSeconds;
        Meta.Text = $"läuft … {seconds:0,0} s";
    }

    /// <summary>Angesammelten Text als Markdown darstellen (nur während des Streamens).</summary>
    private void FlushAnswer()
    {
        string text;
        string thinking;
        lock (_answer)
        {
            text = _answer.ToString();
        }

        lock (_thinking)
        {
            thinking = _thinking.ToString();
        }

        var atBottom = AnswerScroll.VerticalOffset >= AnswerScroll.ScrollableHeight - 24;
        AnswerPanel.Children.Clear();
        foreach (var block in Markdown.Render(text, _settings.FontSize))
        {
            AnswerPanel.Children.Add(block);
        }

        if (thinking.Length > 0)
        {
            ThinkingBox.Visibility = Visibility.Visible;
            ThinkingText.Text = thinking.Trim();

            // Nur einmal je Frage automatisch aufklappen — danach gehört die
            // Entscheidung dem Nutzer (sonst würde jeder Zeichentakt sie öffnen).
            if (_settings.ShowThinking && !_thinkingAutoExpanded)
            {
                _thinkingAutoExpanded = true;
                ThinkingText.Visibility = Visibility.Visible;
                ThinkingHeader.Text = "▾ Denk-Schritte verbergen";
            }
        }

        if (atBottom)
        {
            AnswerScroll.ScrollToEnd();
        }
    }

    private void ShowAnswer(string question, string answer, AskResult result)
    {
        lock (_answer)
        {
            _answer.Clear();
            _answer.Append(answer);
        }

        _lastQuestion = question;
        _lastResult = result;
        AnswerBox.Visibility = Visibility.Visible;
        FlushAnswer();
        AnswerScroll.ScrollToTop();
        Meta.Text = FooterText(result);
    }

    private void ShowText(string title, string markdown)
    {
        lock (_answer)
        {
            _answer.Clear();
            _answer.Append($"## {title}\n\n{markdown}");
        }

        _lastResult = null;
        AnswerBox.Visibility = Visibility.Visible;
        FlushAnswer();
        AnswerScroll.ScrollToTop();
        Meta.Text = "";
        Input.Focus();
    }

    private void OnThinkingToggled(object sender, MouseButtonEventArgs e)
    {
        var visible = ThinkingText.Visibility == Visibility.Visible;
        ThinkingText.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        ThinkingHeader.Text = visible ? "▸ Denk-Schritte anzeigen" : "▾ Denk-Schritte verbergen";
    }

    private void SetHint(string text) => Hint.Text = text;

    private void CycleHistory(int direction)
    {
        var entries = _history.Recent(20);
        if (entries.Count == 0)
        {
            return;
        }

        var index = entries.ToList().FindIndex(entry => entry.Question == Input.Text);
        var next = index < 0
            ? (direction > 0 ? 0 : entries.Count - 1)
            : ((index + direction) % entries.Count + entries.Count) % entries.Count;
        Input.Text = entries[next].Question;
        Input.CaretIndex = Input.Text.Length;
    }

    // ── Hilfsmittel ─────────────────────────────────────────────────────────

    private void CopyAnswer()
    {
        string text;
        lock (_answer)
        {
            text = _answer.ToString();
        }

        if (text.Length == 0)
        {
            SetHint("Es gibt noch keine Antwort zum Kopieren.");
            return;
        }

        if (WriteClipboard(text))
        {
            SetHint("Antwort in der Zwischenablage.");
        }
    }

    private void SaveAnswer()
    {
        string text;
        lock (_answer)
        {
            text = _answer.ToString();
        }

        if (text.Length == 0)
        {
            SetHint("Es gibt noch keine Antwort zum Speichern.");
            return;
        }

        try
        {
            var folder = _settings.EffectiveAnswersFolder;
            Directory.CreateDirectory(folder);
            var name = $"AIlight {DateTime.Now:yyyy-MM-dd HH-mm-ss}.md";
            var path = Path.Combine(folder, name);
            var body = $"# {_lastQuestion}\n\n{text}\n";
            if (Meta.Text.Length > 0)
            {
                body += $"\n---\n{_lastQuestion.Length} Zeichen Frage · {Meta.Text}\n";
            }

            File.WriteAllText(path, body, Encoding.UTF8);
            SetHint($"Gespeichert: {path}");
        }
        catch (Exception ex)
        {
            Log.Error("Antwort konnte nicht gespeichert werden", ex);
            SetHint($"Speichern fehlgeschlagen: {ex.Message}");
        }
    }

    internal static string ReadClipboard()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch
            {
                Thread.Sleep(60);
            }
        }

        return "";
    }

    private static bool WriteClipboard(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(60);
            }
        }

        return false;
    }

}
