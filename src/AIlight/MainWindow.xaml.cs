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
    private DispatcherTimer? _hideTimer;
    private Glide? _answerGlide;
    private Glide? _rowGlide;
    private bool _followAnswer;
    private bool _hiding;
    /// <summary>Zwischen der Frage und dem letzten Buchstaben gehört die Fläche dem Gespräch.</summary>
    private bool _answering;
    /// <summary>Wie viele Absätze der Antwort schon aufgetaucht sind (für den Auftritt).</summary>
    private int _revealedBlocks;
    /// <summary>Die drei Punkte der Laufanzeige.</summary>
    private UIElement[] _dots = Array.Empty<UIElement>();
    private string? _renderedAnswer;
    private string? _renderedThinking;
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

        // Weiches Scrollen: Vorschlagsliste und Antwort springen nie.
        _answerGlide = new Glide(AnswerScroll);
        _rowGlide = new Glide(SuggestionScroll);
        _dots = new UIElement[] { Dot1, Dot2, Dot3 };

        // Wer im Text radelt, will selbst bestimmen, wo er liest — dann hört das
        // Nachziehen ans Ende auf.
        AnswerScroll.PreviewMouseWheel += (_, _) => _followAnswer = false;
    }

    /// <summary>
    /// Bewegung nur dann, wenn wirklich ein Fenster auf dem Bildschirm steht — und
    /// wenn weder die Einstellungen noch Windows selbst sie abgeschaltet haben.
    /// </summary>
    private bool CanAnimate => Motion.Moves && IsVisible;

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

    internal string InputTextForTest => Input.Text;

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

    /// <summary>Läuft gerade ein weiches Ausblenden? (Prüfstand)</summary>
    internal bool IsHidingForTest => _hiding;

    /// <summary>Steht der Vorschlagsbereich gerade im Bild? (Prüfstand)</summary>
    internal bool SuggestionsVisibleForTest => SuggestionBox.Visibility == Visibility.Visible;

    /// <summary>Läuft gerade eine Frage? (Prüfstand)</summary>
    internal bool AnsweringForTest => _answering;

    /// <summary>
    /// Der sichtbare Teil einer Frage — ohne Netz. Genau der Weg, den auch
    /// <see cref="Ask"/> nimmt, damit der Prüfstand keine zweite Fassung pflegt.
    /// </summary>
    internal void BeginAskForTest(string question) => BeginQuestion(question);

    /// <summary>Deckkraft der Leiste — der Prüfstand sieht nach, dass sie in Grundstellung zurückgeht.</summary>
    internal double ShellOpacityForTest => Root.Opacity;

    /// <summary>Auftrieb der Leiste (0 = Grundstellung).</summary>
    internal double ShellShiftForTest => BarShift.Y;

    /// <summary>Wie oft die Antwort wirklich neu aufgebaut wurde (Prüfstand).</summary>
    internal int AnswerRenderCountForTest { get; private set; }

    /// <summary>Einen Takt auslösen — der Prüfstand sieht nach, dass nichts doppelt geschieht.</summary>
    internal void FlushForTest() => FlushAnswer();

    /// <summary>
    /// Ein eigenes Fenster übernimmt — zum Beispiel die Einstellungen. Die Leiste
    /// zieht sich zurück, statt stur davor zu schweben, und holt dabei bewusst
    /// keinen fremden Fokus zurück: sonst nähme sie dem neuen Fenster die Tastatur
    /// wieder weg.
    /// </summary>
    public void YieldToSettings()
    {
        _previousWindow = IntPtr.Zero;
        HideBar();
    }

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
        // Der Schalter aus den Einstellungen gilt für alles, was sich bewegt —
        // auch für den Zeichentakt und die Kurven.
        Motion.Enabled = _settings.AnimateBar;

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

        // Schrift, Breite oder Höhe ändern sich: Der Text muss mit der neuen
        // Schrift neu gesetzt werden (vorher blieb er bis zum nächsten Takt alt).
        InvalidateAnswer();
        FlushAnswer();

        if (IsVisible)
        {
            // Nach einer Breitenänderung sitzt die Leiste sonst nicht mehr mittig.
            PlaceWindow();
        }
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
        _hiding = false;
        _hideTimer?.Stop();
        Show();
        PlaceWindow();
        // Auftritt wie ein Spotlight — abschaltbar über die Einstellungen.
        AnimateIn();
        Activate();
        Input.SelectAll();
        Input.Focus();
        UpdateFocusLine();
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

        // Vor dem ersten Messen gibt es noch keine wirkliche Breite — dann gilt
        // die eingestellte. Danach zählt, was wirklich dasteht, sonst sitzt die
        // Leiste nach einer Breitenänderung nicht mehr mittig.
        var width = (ActualWidth > 0 ? ActualWidth : Width) * DeviceScale.X;
        var x = area.Left + (int)((area.Width - width) / 2);
        var y = area.Top + (int)(area.Height * (_settings.TopPercent / 100.0));
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Auftritt der Leiste: aus der Deckkraft auftauchen, einen Hauch aufsteigen
    /// und kaum merklich wachsen. Ein reines Einblenden wirkte träge; erst die
    /// kurze Bewegung lässt die Leiste „angekommen“ wirken.
    /// </summary>
    private void AnimateIn()
    {
        ResetVisual();

        if (!CanAnimate)
        {
            return;
        }

        // Deckkraft, Auftrieb und ein Hauch Wachsen greifen ineinander: erst ist
        // die Leiste da, dann kommt sie an. Die Kurve ist die steile macOS-Kurve —
        // mit einer flacheren wirkte derselbe Weg zäh.
        Root.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.ShowMs, Motion.Soft));
        BarShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(-Motion.Rise, 0, Motion.ShowDuration) { EasingFunction = Motion.Soft });
        BarScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(Motion.Grow, 1, Motion.ShowDuration) { EasingFunction = Motion.Soft });
        BarScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(Motion.Grow, 1, Motion.ShowDuration) { EasingFunction = Motion.Soft });

        // Der Lichtschein zieht mit ein: erst kaum sichtbar, dann liegt er still.
        Sheen.BeginAnimation(OpacityProperty, null);
        Sheen.Opacity = 1;
        Sheen.BeginAnimation(OpacityProperty, Motion.Fade(0, 1, Motion.PageMs, Motion.Soft));
        Anim.Slide(Sheen, -8, 0, Motion.PageMs, Motion.Soft);
    }

    /// <summary>Grundstellung: keine laufende Animation, keine Verschiebung.</summary>
    private void ResetVisual()
    {
        Root.BeginAnimation(OpacityProperty, null);
        BarShift.BeginAnimation(TranslateTransform.YProperty, null);
        BarScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BarScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Sheen.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 1;
        BarShift.Y = 0;
        BarScale.ScaleX = 1;
        BarScale.ScaleY = 1;
        Sheen.Opacity = 1;
        Anim.SlideNow(Sheen, 0);
    }

    /// <summary>
    /// Ausblenden — weich statt hart. Wird während des Ausblendens wieder geöffnet,
    /// gehört der Abgang nicht mehr uns: <see cref="ShowBar"/> nimmt die Marke
    /// zurück und die Leiste bleibt stehen.
    /// </summary>
    public void HideBar()
    {
        if (!IsVisible || _hiding)
        {
            return;
        }

        if (!CanAnimate)
        {
            FinishHide();
            return;
        }

        _hiding = true;

        // Der Abschluss läuft über einen Zeitgeber, nicht über den Abschluss der
        // Animation: Der Zeitgeber hängt allein am Nachrichtenumlauf und zieht
        // auch dann durch, wenn gerade niemand zeichnet.
        _hideTimer ??= new DispatcherTimer(DispatcherPriority.Background);
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(Motion.HideMs + 20);
        _hideTimer.Tick -= OnHideTimer;
        _hideTimer.Tick += OnHideTimer;
        _hideTimer.Start();

        Root.BeginAnimation(OpacityProperty, Motion.Fade(1, 0, Motion.HideMs, Motion.In));
        BarShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, -Motion.Rise * 0.6, Motion.HideDuration) { EasingFunction = Motion.In });
        BarScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, Motion.Shrink, Motion.HideDuration) { EasingFunction = Motion.In });
        BarScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, Motion.Shrink, Motion.HideDuration) { EasingFunction = Motion.In });
        Sheen.BeginAnimation(OpacityProperty, Motion.Fade(1, 0, Motion.HideMs, Motion.In));
    }

    private void OnHideTimer(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        if (_hiding)
        {
            FinishHide();
        }
    }

    private void FinishHide()
    {
        _hideTimer?.Stop();
        _hiding = false;
        _answerGlide?.Stop();
        _rowGlide?.Stop();
        ResetVisual();
        Hide();

        if (_previousWindow != IntPtr.Zero && _previousWindow != new WindowInteropHelper(this).Handle)
        {
            SetForegroundWindow(_previousWindow);
        }
    }

    public void Toggle()
    {
        if (_hiding)
        {
            // Beim Ausblenden noch einmal gedrückt: der Nutzer will sie sehen.
            ShowBar();
            return;
        }

        if (IsVisible && IsActive)
        {
            HideBar();
            return;
        }

        ShowBar();
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        if (!_settings.HideOnLostFocus)
        {
            return;
        }

        // Kurz warten: ein Klick neben die Leiste soll sie schließen, ein
        // Tray-Klick (Menü schließt sich) soll sie aber nicht sofort verstecken.
        _hideLater ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _hideLater.Stop();
        _hideLater.Tick -= OnHideLater;
        _hideLater.Tick += OnHideLater;
        _hideLater.Start();
    }

    private void OnHideLater(object? sender, EventArgs e)
    {
        _hideLater?.Stop();
        if (!IsActive && IsVisible)
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

        // Wer tippt, sucht etwas — die Regel unten bringt die Vorschläge dann
        // von selbst zurück ins Bild, auch nach einer Antwort.
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
        _rowGlide?.Stop();
        SuggestionScroll.ScrollToTop();
        UpdateSelectionVisual();
        SuggestionList.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var wanted = _rows.Count > 0 && ShouldShowSuggestions;
        var visible = SuggestionBox.Visibility == Visibility.Visible;
        if (wanted && !visible)
        {
            ShowSuggestions();
        }
        else if (!wanted && visible)
        {
            HideSuggestions();
        }
    }

    /// <summary>
    /// Wann gehören Vorschläge ins Bild?
    ///
    /// Immer dann, wenn gerade gesucht wird (es steht etwas im Feld) oder wenn
    /// es nichts zu lesen gibt. Sobald eine Frage läuft oder eine Antwort dasteht,
    /// bleibt der Platz dem Gespräch — genau das ist der Unterschied zwischen
    /// einer Auswahlliste und einem Chat.
    /// </summary>
    private bool ShouldShowSuggestions
        => Input.Text.Trim().Length > 0 || !(_answering || HasVisibleAnswer);

    /// <summary>Steht eine Antwort im Bild, die man lesen kann?</summary>
    private bool HasVisibleAnswer
        => AnswerBox.Visibility == Visibility.Visible && AnswerPanel.Children.Count > 0;

    /// <summary>
    /// Vorschläge aufklappen. Die Zeilen kommen der Reihe nach herein — eine
    /// Liste, die auf einmal dasteht, wirkt hineingeworfen.
    /// </summary>
    private void ShowSuggestions()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        SuggestionList.Visibility = Visibility.Visible;
        Anim.Expand(SuggestionBox, Motion.CollapseMs);
        RevealRows();
    }

    /// <summary>Vorschläge zuklappen — Höhe, Deckkraft und kein Rest danach.</summary>
    private void HideSuggestions()
    {
        _rowGlide?.Stop();
        Anim.Collapse(SuggestionBox, Motion.CollapseMs);
    }

    /// <summary>
    /// Jede Zeile einen Wimpernschlag nach der vorigen. Nur beim wirklichen
    /// Erscheinen: beim Tippen wird die Liste nur umgesetzt, sonst zappelten die
    /// Zeilen bei jedem Buchstaben.
    /// </summary>
    private void RevealRows()
    {
        SuggestionList.UpdateLayout();
        var containers = new List<FrameworkElement>();
        for (var index = 0; index < _rows.Count; index++)
        {
            if (SuggestionList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            {
                containers.Add(container);
            }
        }

        Anim.Stagger(containers, Motion.StaggerMs, Motion.BlockMs);
    }

    /// <summary>Die Denk-Schritte tauchen einmal auf — nicht bei jedem Zeichentakt.</summary>
    private void RevealThinking()
    {
        Anim.Reset(ThinkingBox);
        Anim.FadeNow(ThinkingBox, 0);
        Anim.Fade(ThinkingBox, 1, Motion.QuickMs, Motion.Soft);
        Anim.Slide(ThinkingBox, 6, 0, Motion.PageMs, Motion.Soft);
    }

    /// <summary>Denk-Schritte wieder wegnehmen (ohne stehengebliebene Animation).</summary>
    private void HideThinking()
    {
        Anim.Reset(ThinkingBox);
        ThinkingBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Die Fokuslinie unter der Eingabezeile wächst aus der Mitte, wenn man
    /// hineinklickt, und zieht sich dorthin zurück, wenn man wieder geht.
    /// </summary>
    private void OnInputFocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateFocusLine();

    /// <summary>
    /// Die Fokuslinie auf den richtigen Stand bringen — beim Hineinklicken, beim
    /// Verlassen und jedes Mal, wenn die Leiste auftaucht.
    /// </summary>
    private void UpdateFocusLine()
    {
        var focused = Input.IsKeyboardFocusWithin;
        var target = focused ? 1 : 0;
        FocusLine.Opacity = target;
        FocusScale.ScaleX = target;

        if (!CanAnimate)
        {
            return;
        }

        FocusLine.BeginAnimation(OpacityProperty, Motion.Fade(1 - target, target, Motion.QuickMs, Motion.Out));
        FocusScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            Motion.Tween(1 - target, target, Motion.PageMs, Motion.Soft));
    }

    private void MoveSelection(int delta)
    {
        // Wer mit den Pfeilen wählt, will die Liste sehen — auch wenn gerade
        // eine Antwort dasteht.
        if (SuggestionBox.Visibility != Visibility.Visible)
        {
            ShowSuggestions();
        }

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
        // deshalb die gewählte Zeile hereingleiten lassen. (ItemsControl kennt kein
        // ScrollIntoView; der Container selbst kennt seine Lage.)
        if (_selected >= 0
            && _selected < _rows.Count
            && SuggestionList.ItemContainerGenerator.ContainerFromIndex(_selected) is FrameworkElement container)
        {
            GlideRowIntoView(container);
        }
    }

    /// <summary>
    /// Die gewählte Zeile ins Bild holen — weich, und nur wenn sie wirklich
    /// nicht mehr gut sichtbar ist. Ein hartes BringIntoView ließ die Liste bei
    /// jedem Tastendruck springen, obwohl die Zeile längst zu sehen war.
    /// </summary>
    private void GlideRowIntoView(FrameworkElement row)
    {
        var viewport = SuggestionScroll.ViewportHeight;
        if (_rowGlide is null || row.ActualHeight <= 0 || viewport <= 0)
        {
            return;
        }

        var top = row.TransformToAncestor(SuggestionScroll).Transform(new Point(0, 0)).Y;
        var height = row.ActualHeight;
        if (top >= 0 && top + height <= viewport)
        {
            return; // steht schon im Bild — nichts bewegen
        }

        _rowGlide.To(SuggestionScroll.VerticalOffset + top - ((viewport - height) / 2));
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

            case "welcome":
                ((App)Application.Current).OpenWelcome();
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
        _followAnswer = false;
        _answering = false;
        _revealedBlocks = 0;
        _answerGlide?.Stop();
        InvalidateAnswer();
        StopSpinner();
        AnswerBox.Visibility = Visibility.Collapsed;
        HideThinking();
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
        _rowGlide?.Stop();
        SuggestionScroll.ScrollToTop();
        UpdateSelectionVisual();

        // Der Verlauf ist eine Auswahlliste — also darf sie auch wieder aufgehen.
        ShowSuggestions();
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

        BeginQuestion(question);
        Meta.Text = display;

        var token = new CancellationTokenSource();
        _cancel = token;
        _streaming = true;
        _startedAt = DateTime.Now;
        StartPaintTimer();

        _ = RunAsync(question, token.Token);
    }

    /// <summary>
    /// Der sichtbare Teil einer Frage: das Feld wird leer, die Vorschläge klappen
    /// weg — von da an gehört die Fläche dem Gespräch — und der Antwortbereich
    /// steht bereit. Die Marke gibt dabei eine kurze Rückmeldung.
    /// </summary>
    private void BeginQuestion(string question)
    {
        _lastQuestion = question;
        _answering = true;
        HideSuggestions();
        Anim.Pulse(BrandChip, 1.07, 320, Motion.Spring);

        Input.Clear();
        _revealedBlocks = 0;
        InvalidateAnswer();
        _answer.Clear();
        _thinking.Clear();
        _lastResult = null;
        ThinkingText.Text = "";
        HideThinking();
        AnswerPanel.Children.Clear();
        AnswerBox.Visibility = Visibility.Visible;
        _answerGlide?.Stop();
        AnswerScroll.ScrollToTop();
        _followAnswer = true;
        RevealAnswer();
        Spinner.Visibility = Visibility.Visible;
        StartSpinner();
        StopButton.Visibility = Visibility.Visible;
        _thinkingAutoExpanded = false;
        SetHint("Anfrage läuft — Esc bricht ab.");
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
            _answering = false;
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
            _followAnswer = false;
            _answerGlide?.Stop();
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
        _paint ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(55) };
        _paint.Tick -= OnPaintTick;
        _paint.Tick += OnPaintTick;
        _paint.Start();
    }

    private void StopPaintTimer() => _paint?.Stop();

    /// <summary>
    /// Die drei Punkte leuchten der Reihe nach auf — jeder um ein Drittel der
    /// Runde versetzt. Das liest sich als „ich denke“ und nicht als „irgendwas
    /// blinkt“; ein einzelner Punkt konnte das nicht erzählen.
    /// </summary>
    private void StartSpinner()
    {
        var step = Motion.DotMs;
        for (var index = 0; index < _dots.Length; index++)
        {
            var dot = _dots[index];
            dot.BeginAnimation(OpacityProperty, null);
            dot.Opacity = 0.3;
            if (!CanAnimate)
            {
                dot.Opacity = 1;
                continue;
            }

            var pulse = new DoubleAnimation(0.3, 1, Motion.After(step))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(index * step * 0.6),
                EasingFunction = Motion.Out,
            };
            dot.BeginAnimation(OpacityProperty, pulse);
        }
    }

    private void StopSpinner()
    {
        foreach (var dot in _dots)
        {
            dot.BeginAnimation(OpacityProperty, null);
            dot.Opacity = 1;
        }
    }

    private void OnPaintTick(object? sender, EventArgs e)
    {
        FlushAnswer();
        var seconds = (DateTime.Now - _startedAt).TotalSeconds;
        Meta.Text = $"läuft · {seconds:0,0} s";
    }

    /// <summary>Erzwingt beim nächsten Takt einen vollständigen Neuaufbau der Antwort.</summary>
    private void InvalidateAnswer()
    {
        _renderedAnswer = null;
        _renderedThinking = null;
    }

    /// <summary>
    /// Der Antwortbereich steht bereit. Die Bewegung übernehmen die Absätze
    /// selbst (siehe <see cref="FlushAnswer"/>): ein zweites Einblenden über den
    /// ganzen Bereich würde die Staffelung darunter verwischen.
    /// </summary>
    private void RevealAnswer()
    {
        Anim.Reset(AnswerBox);
        Anim.Slide(AnswerBox, 5, 0, Motion.PageMs, Motion.Soft);
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

        // Nur neu aufbauen, wenn sich wirklich etwas geändert hat. Vorher wurde
        // bis zu zwanzigmal je Sekunde derselbe Text neu gesetzt — das flackerte
        // und kostete mehr, als das Schreiben selbst brauchte.
        thinking = thinking.Trim();
        if (text == _renderedAnswer && thinking == _renderedThinking)
        {
            if (_followAnswer)
            {
                _answerGlide?.Chase();
            }

            return;
        }

        _renderedAnswer = text;
        _renderedThinking = thinking;
        AnswerRenderCountForTest++;

        var blocks = Markdown.Render(text, _settings.FontSize);
        var shown = Math.Clamp(_revealedBlocks, 0, blocks.Count);
        AnswerPanel.Children.Clear();
        foreach (var block in blocks)
        {
            AnswerPanel.Children.Add(block);
        }

        _revealedBlocks = blocks.Count;

        // Nur die neuen Absätze tauchen auf. Vorher stand die ganze Antwort mit
        // einem Schlag da — beim Schreiben sah das nach Flackern aus, weil jeder
        // Takt den ganzen Baum neu setzte.
        Anim.Stagger(blocks.Skip(shown).ToList(), Motion.StaggerMs, Motion.BlockMs);

        if (thinking.Length > 0)
        {
            if (ThinkingBox.Visibility != Visibility.Visible)
            {
                ThinkingBox.Visibility = Visibility.Visible;
                RevealThinking();
            }

            ThinkingText.Text = thinking;

            // Nur einmal je Frage automatisch aufklappen — danach gehört die
            // Entscheidung dem Nutzer (sonst würde jeder Zeichentakt sie öffnen).
            if (_settings.ShowThinking && !_thinkingAutoExpanded)
            {
                _thinkingAutoExpanded = true;
                ThinkingText.Visibility = Visibility.Visible;
                ThinkingHeader.Text = "▾ Denk-Schritte verbergen";
            }
        }

        if (_followAnswer)
        {
            // Nicht springen: die Ansicht zieht sanft ans neue Textende nach.
            _answerGlide?.Chase();
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
        ShowFromTop();
        Meta.Text = FooterText(result);

        // Auch hier gilt: eine Antwort gehört allein ins Bild.
        RefreshSuggestions();
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
        ShowFromTop();
        Meta.Text = "";
        Input.Focus();
    }

    /// <summary>Eine fertige Antwort von vorn zeigen — ohne Nachziehen ans Ende.</summary>
    private void ShowFromTop()
    {
        _followAnswer = false;
        _answerGlide?.Stop();
        InvalidateAnswer();
        _revealedBlocks = 0;
        FlushAnswer();
        AnswerScroll.ScrollToTop();
        RevealAnswer();
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
