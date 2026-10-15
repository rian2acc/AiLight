using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AIlight.Core;
using AIlight.Groq;
using AIlight.Views;
using Microsoft.Win32;
using Application = System.Windows.Application;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace AIlight;

/// <summary>
/// Das Herz: Einstellungen, Verlauf, Chat-Dienst, Infobereich, Hotkey und die
/// Leiste. Alles wird erst gebaut, wenn es gebraucht wird — im Leerlauf kostet
/// das Programm damit so gut wie nichts.
/// </summary>
public sealed class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AIlight";

    private readonly Options _options;
    private SingleInstance? _single;
    private MainWindow? _bar;
    private SettingsWindow? _settings;
    private WelcomeWindow? _welcome;
    private TrayIcon? _tray;
    private HotKey? _hotkey;
    private GroqClient? _client;
    private Report? _report;
    private string _hotkeyProblem = "";
    private ToolStripMenuItemHost? _autostartItem;
    private System.Drawing.Icon? _trayIconImage;

    public App(Options options)
    {
        _options = options;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    public static new App Current => (App)Application.Current;

    /// <summary>Echter Prozessbeginn — für die Startspur (JIT, XAML, Symbol …).</summary>
    private static readonly DateTime ProcessStarted = ReadProcessStart();

    private static DateTime ReadProcessStart()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }

    /// <summary>Startspur ins Log schreiben (nur mit <c>--trace-start</c>).</summary>
    private void Trace(string stage)
    {
        if (!_options.TraceStart)
        {
            return;
        }

        var elapsed = (DateTime.Now - ProcessStarted).TotalMilliseconds;
        Log.Info($"Startspur {stage,-18} {elapsed,7:0} ms ab Prozessstart");
    }

    public AppSettings Settings { get; private set; } = new();

    public ChatService Chat { get; private set; } = null!;

    public HistoryStore History { get; private set; } = null!;

    public MainWindow? Bar => _bar;

    /// <summary>Für Messungen: Zeit von Programmstart bis die Leiste sichtbar war.</summary>
    public double StartToBarMilliseconds { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Views/Theme.xaml", UriKind.Absolute),
        });

        Trace("xaml_geladen");
        Paths.EnsureFolders();
        Settings = AppSettings.Load();
        Log.Start(Paths.LogFolder, Settings.Logging);
        Log.Info($"AIlight startet (Version {GetType().Assembly.GetName().Version}, {_options.Summary})");
        Trace("einstellungen");

        Theme.Apply(Settings.Theme, Settings.Accent, Settings.Opacity, Settings.CornerRadius, Settings.FontFamily);

        // Der Bewegungsschalter gilt für alle Fenster — deshalb steht er hier und
        // nicht erst in der Leiste (die es in diesem Moment noch gar nicht gibt).
        Motion.Enabled = Settings.AnimateBar;

        _client = new GroqClient();
        History = new HistoryStore(Paths.HistoryFile);
        Chat = new ChatService(_client, Settings, History);
        Trace("dienste");

        InstallExceptionHandling();
        RegisterHotkey(Settings.Hotkey, notify: false);
        Trace("hotkey");
        var hotkeyProblem = _hotkeyProblem;

        // Infobereich und Autostart-Eintrag brauchen den Start nicht aufzuhalten:
        // das Symbol erscheint, sobald das Fenster steht (wenige Millisekunden später).
        Task.Run(() =>
        {
            try
            {
                SyncAutostart();
            }
            catch (Exception ex)
            {
                Log.Warn($"Autostart: {ex.Message}");
            }
        });

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            BuildTray();
            Trace("tray");

            if (hotkeyProblem.Length > 0)
            {
                _tray?.ShowBalloon("AIlight",
                    $"Der Hotkey {Settings.Hotkey} ist {hotkeyProblem}. Über das Symbol im Infobereich lässt "
                    + "sich die Leiste weiterhin öffnen; in den Einstellungen lässt sich ein anderer wählen.");
            }

            ShowWelcomeOnFirstStart();
            Trace("bereit");
        }));

        Settings.Changed += ApplySetting;

        if (_options.Ask.Length > 0)
        {
            RunHeadlessQuestion(_options.Ask);
            return;
        }

        if (_options.AskUi.Length > 0)
        {
            RunAskThroughBar(_options.AskUi);
            return;
        }

        if (_options.Shot.Length > 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    RunShots(_options.Shot);
                }
                catch (Exception ex)
                {
                    Log.Error("Bildschirmfotos fehlgeschlagen", ex);
                }

                Shutdown(0);
            }));
            return;
        }

        if (_options.SmokeSeconds > 0)
        {
            RunSmoke(_options.SmokeSeconds);
            return;
        }

        if (_options.Welcome)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                OpenWelcome();
                Trace("einführung");
            }));
            return;
        }

        if (Settings.PreloadBar)
        {
            // Erst nach dem Anmelden in Ruhe aufbauen: dann ist der erste Hotkey sofort da.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = EnsureBar()));
            }));
        }

        if (_options.ShowBar)
        {
            ShowBar();
            Trace("leiste_gezeigt");
        }

    }

    /// <summary>
    /// Läuft gerade ein Mess- oder Bildmodus? Dann öffnet sich kein Fenster, das
    /// niemand angefordert hat — die Einführung würde sonst mitten in die
    /// Aufnahmen platzen.
    /// </summary>
    private bool IsMeasurementMode
        => _options.Shot.Length > 0
           || _options.Ask.Length > 0
           || _options.AskUi.Length > 0
           || _options.SmokeSeconds > 0
           || _options.SelfTest;

    /// <summary>
    /// Der erste Start nach der Installation: die Einführung öffnet sich und
    /// erklärt alles — vom Schlüssel bis zum Hotkey. Ein zweites Mal kommt sie
    /// nicht; wer sie nochmal sehen will, ruft sie über /welcome auf.
    /// </summary>
    private void ShowWelcomeOnFirstStart()
    {
        if (IsMeasurementMode)
        {
            return;
        }

        if (!Settings.WelcomeSeen)
        {
            Log.Info("Erster Start — die Einführung wird geöffnet");
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(OpenWelcome));
            return;
        }

        ShowWelcomeWhenNoKey();
    }

    /// <summary>
    /// Erster Start ohne Schlüssel: freundlich erklären, was zu tun ist, und das
    /// Einstellungsfenster gleich öffnen — mehr braucht ein neuer Rechner nicht.
    /// </summary>
    private void ShowWelcomeWhenNoKey()
    {
        if (Chat.HasKey)
        {
            return;
        }

        Log.Info("Kein API-Schlüssel hinterlegt — Einstellungen werden geöffnet");
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _tray?.ShowBalloon("AIlight",
                "Fast fertig: Trage im Einstellungsfenster deinen Groq-Schlüssel ein "
                + "(console.groq.com/keys) und klicke auf „Schlüssel testen“.");
            OpenSettings("konto");
        }));
    }

    /// <summary>
    /// Die Einführung öffnen — beim ersten Start, über /welcome, aus dem
    /// Infobereich oder aus den Einstellungen. Die Leiste tritt dabei zurück,
    /// wie vor den Einstellungen: sie schwebt sonst über allem anderen.
    /// </summary>
    public void OpenWelcome()
    {
        _bar?.YieldToSettings();

        if (_welcome is null)
        {
            Log.Info("Einführung geöffnet");
            _welcome = new WelcomeWindow(Settings, Chat);
            _welcome.Closed += (_, _) =>
            {
                _welcome = null;
                // Gesehen ist gesehen: sie soll nicht bei jedem Start wiederkommen.
                Settings.WelcomeSeen = true;
            };
        }

        _welcome.Show();
        if (_welcome.WindowState == WindowState.Minimized)
        {
            _welcome.WindowState = WindowState.Normal;
        }

        _welcome.PlayEntrance();
        _welcome.Activate();
    }

    /// <summary>Die Leiste bauen (oder zurückgeben) — ohne sie zu zeigen.</summary>
    public MainWindow EnsureBar()
    {
        if (_bar is null)
        {
            _bar = new MainWindow(Chat);
            _bar.Closed += (_, _) => _bar = null;
        }

        return _bar;
    }

    public void ShowBar()
    {
        var started = Stopwatch.StartNew();
        var bar = EnsureBar();
        bar.ShowBar();
        StartToBarMilliseconds = started.Elapsed.TotalMilliseconds;
    }

    public void ToggleBar()
    {
        // Solange die Einführung offen ist, gehört sie nach vorn.
        if (_welcome is { } welcome)
        {
            if (welcome.WindowState == WindowState.Minimized)
            {
                welcome.WindowState = WindowState.Normal;
            }

            welcome.Activate();
            return;
        }

        // Solange die Einstellungen offen sind, gehören sie nach vorn: Die Leiste
        // ist immer im Vordergrund und würde sonst davor schweben.
        if (_settings is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var bar = EnsureBar();
        bar.Toggle();
    }

    /// <summary>
    /// Einstellungen öffnen — ohne Bereich beim ersten Bereich, mit Bereich zum
    /// Beispiel aus der Einführung heraus ("/welcome" → „Schlüssel eintragen“).
    /// </summary>
    public void OpenSettings(string area = "")
    {
        // Die Leiste tritt zurück, statt vor den Einstellungen zu schweben —
        // genau das war der störende Fall. Fokus holt sie sich dabei nicht
        // zurück, sonst nähme sie dem neuen Fenster sofort die Tastatur weg.
        _bar?.YieldToSettings();

        if (_settings is null)
        {
            Log.Info("Einstellungen geöffnet");
            _settings = new SettingsWindow(Settings, Chat);
            _settings.Closed += (_, _) => _settings = null;
        }

        if (area.Length > 0)
        {
            _settings.ShowArea(area);
        }

        _settings.Show();
        if (_settings.WindowState == WindowState.Minimized)
        {
            _settings.WindowState = WindowState.Normal;
        }

        // Jetzt erst, und nur hier: Der Auftritt gehört dem echten Öffnen. Für
        // Prüfstand und Bilder wird dasselbe Fenster ohne Bewegung gerendert.
        _settings.PlayEntrance();
        _settings.Activate();
    }

    /// <summary>Eine Einstellung ist geändert worden — sofort anwenden.</summary>
    public void ApplySetting(string name)
    {
        switch (name)
        {
            case nameof(AppSettings.Hotkey):
                RegisterHotkey(Settings.Hotkey, notify: true);
                break;

            case nameof(AppSettings.StartWithWindows):
                SyncAutostart(force: true);
                _autostartItem?.Update(Settings.StartWithWindows);
                break;

            case nameof(AppSettings.Theme):
            case nameof(AppSettings.Accent):
            case nameof(AppSettings.Opacity):
            case nameof(AppSettings.CornerRadius):
            case nameof(AppSettings.FontFamily):
                Theme.Apply(Settings.Theme, Settings.Accent, Settings.Opacity,
                    Settings.CornerRadius, Settings.FontFamily);
                _bar?.ApplySettings();
                break;

            case nameof(AppSettings.AnimateBar):
                // Gilt für jedes Fenster — auch für das, das gerade offen ist.
                Motion.Enabled = Settings.AnimateBar;
                _bar?.ApplySettings();
                break;

            case nameof(AppSettings.WindowWidth):
            case nameof(AppSettings.TopPercent):
            case nameof(AppSettings.FontSize):
            case nameof(AppSettings.MaxSuggestions):
            case nameof(AppSettings.ShowThinking):
            case nameof(AppSettings.ShowMeta):
                _bar?.ApplySettings();
                break;

            case nameof(AppSettings.Logging):
                Log.Info($"Protokollierung {(Settings.Logging ? "an" : "aus")}");
                break;
        }
    }

    // ── Tray ────────────────────────────────────────────────────────────────

    private void BuildTray()
    {
        try
        {
            _trayIconImage = Icons.Load(32);
            _tray = new TrayIcon(_trayIconImage, $"AIlight {Settings.Hotkey} — fragen, Enter");
            _tray.Activated += ShowBar;
            _tray.AddItem("Leiste zeigen", ShowBar);
            _tray.AddItem("Einstellungen …", () => OpenSettings());
            _tray.AddItem("Neues Gespräch", () =>
            {
                Chat.NewChat();
                ShowBar();
            });
            _tray.AddItem("Verlauf …", () =>
            {
                ShowBar();
                EnsureBar().ShowHistoryFromTray();
            });
            _tray.AddItem("Einführung …", OpenWelcome);
            _tray.AddSeparator();
            var item = _tray.AddCheck("Mit Windows starten", Settings.StartWithWindows, value => Settings.StartWithWindows = value);
            _autostartItem = new ToolStripMenuItemHost(item);
            _tray.AddItem("Über AIlight …", () =>
            {
                OpenSettings();
            });
            _tray.AddSeparator();
            _tray.AddItem("Beenden", Shutdown);
        }
        catch (Exception ex)
        {
            Log.Error("Infobereich-Symbol konnte nicht erstellt werden", ex);
        }
    }

    private void RegisterHotkey(string spec, bool notify)
    {
        _hotkey ??= new HotKey();
        _hotkey.Pressed -= ToggleBar;
        _hotkey.Pressed += ToggleBar;
        if (_hotkey.Register(spec, out var error))
        {
            _hotkeyProblem = "";
            return;
        }

        _hotkeyProblem = error;
        if (notify)
        {
            _tray?.ShowBalloon(
                "AIlight",
                $"Der Hotkey {spec} ist {error}. Über das Symbol im Infobereich lässt sich die Leiste "
                + "weiterhin öffnen; in den Einstellungen lässt sich ein anderer wählen.");
        }
    }

    // ── Autostart ───────────────────────────────────────────────────────────

    private static bool IsInstalledCopy
    {
        get
        {
            var path = Environment.ProcessPath ?? "";
            return path.EndsWith("AIlight.exe", StringComparison.OrdinalIgnoreCase)
                   && path.StartsWith(Paths.LocalFolder, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SyncAutostart(bool force = false)
    {
        if (!IsInstalledCopy && !force)
        {
            return; // Entwicklungsfassung: nichts in die Registry schreiben
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (Settings.StartWithWindows)
            {
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Autostart-Eintrag konnte nicht geändert werden: {ex.Message}");
        }
    }

    // ── Fehler, die niemanden ausbremsen dürfen ─────────────────────────────

    private void InstallExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unbehandelte Ausnahme", args.Exception);
            _tray?.ShowBalloon("AIlight", "Ein Fehler ist aufgetreten — das Programm läuft weiter. Details im Log.");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Schwerer Fehler", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Fehler im Hintergrund", args.Exception);
            args.SetObserved();
        };
    }

    // ── Prüf- und Messmodi ──────────────────────────────────────────────────

    private void RunHeadlessQuestion(string question)
    {
        var report = EnsureReport();
        var started = Stopwatch.StartNew();
        double firstChunk = 0;
        var text = new StringBuilder();
        var code = 0;
        try
        {
            var result = Chat
                .AskAsync(
                    question,
                    "",
                    chunk =>
                    {
                        if (firstChunk == 0)
                        {
                            firstChunk = started.Elapsed.TotalMilliseconds;
                        }

                        lock (text)
                        {
                            text.Append(chunk);
                        }
                    },
                    null,
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            report.Line($"ask: modell={result.Model}");
            report.Line($"ask: erster_buchstabe_ms={firstChunk:0}");
            report.Line($"ask: tokens={result.InputTokens}->{result.OutputTokens} kosten={result.CostUsd:0.00000}");
            report.Line($"ask: dauer_s={result.Duration.TotalSeconds:0.0} grund={result.FinishReason}");
            report.Line($"ask: antwort={result.Text}");
            report.Line($"ask: meta={AIlight.MainWindow.FooterForTest(result)}");
        }
        catch (GroqException ex)
        {
            report.Line($"ask: fehler={ex.Message}");
            code = 3;
        }
        catch (Exception ex)
        {
            report.Line($"ask: fehler={ex.GetType().Name}: {ex.Message}");
            code = 4;
        }

        report.Flush();
        Shutdown(code);
    }

    /// <summary>
    /// Der Bericht lebt bis zum Programmende: die Messläufe schreiben aus
    /// Rückrufen heraus, die nach dem Ende der aufrufenden Methode laufen.
    /// </summary>
    private Report EnsureReport() => _report ??= new Report(_options.Report);

    /// <summary>
    /// Eine echte Frage **durch die Leiste** schicken: eingeben wie von Hand,
    /// Enter, gestreamte Antwort abwarten und melden, was angekommen ist. Das ist
    /// der einzige Weg zu belegen, dass Anzeige, Markdown und Fußzeile stimmen.
    /// </summary>
    private void RunAskThroughBar(string question)
    {
        var report = EnsureReport();
        var started = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(60);

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var bar = EnsureBar();
            bar.ShowBar();
            report.Line($"askui: leiste_sichtbar=True ms={started.Elapsed.TotalMilliseconds:0}");

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                bar.SetInputForTest(question);
                bar.SubmitForTest();
                report.Line($"askui: frage_abgeschickt={question}");
                WatchForAnswer(bar, report, started, timeout);
            }));
        }));
    }

    private void WatchForAnswer(MainWindow bar, Report report, Stopwatch started, TimeSpan timeout)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var done = false;
        timer.Tick += (_, _) =>
        {
            if (started.Elapsed > timeout)
            {
                report.Line("askui: zeitüberschreitung=ja");
                done = true;
            }
            else if (!bar.IsStreamingForTest)
            {
                var text = bar.AnswerTextForTest;
                if (text.Trim().Length > 0)
                {
                    report.Line($"askui: absätze={bar.AnswerBlockCount} zeichen={text.Length}");
                    report.Line($"askui: antwort_anfang={Vorschau(text)}");
                    report.Line($"askui: fusszeile={bar.MetaTextForTest}");
                    report.Line($"askui: hinweis={bar.HintTextForTest}");
                    report.Line($"askui: ms_bis_fertig={started.Elapsed.TotalMilliseconds:0}");
                    done = true;
                }
            }

            if (!done)
            {
                return;
            }

            timer.Stop();
            report.Flush();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Shutdown(0)));
        };
        timer.Start();
    }

    private static string Vorschau(string text)
    {
        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length > 120 ? single[..120] + "…" : single;
    }

    /// <summary>
    /// Bildschirmfotos für die Gestaltungsprüfung: alle Bereiche der Einstellungen
    /// und die Leiste in beiden Zuständen. Passiert abseits des Bildschirms.
    /// Aufruf: <c>AIlight.exe --shot dist\bilder</c>.
    /// </summary>
    private void RunShots(string folder)
    {
        // Der Hinweis für den ersten Start („kein Schlüssel“) würde ein zweites
        // Einstellungsfenster öffnen — für die Bilder nicht gewünscht.
        if (_settings is { } open)
        {
            open.Close();
            _settings = null;
        }

        string[] areas = { "konto", "modell", "gespraech", "verhalten", "aussehen", "verlauf", "ueber" };

        // Dunkel alle Bereiche, hell die beiden aussagekräftigsten — die Farben
        // wirken sofort, weil alles über DynamicResource läuft.
        foreach (var area in areas)
        {
            SaveSettingsShot(folder, "dunkel", area);
        }

        foreach (var area in new[] { "konto", "aussehen" })
        {
            SaveSettingsShot(folder, "hell", area);
        }

        Theme.Apply(Settings.Theme, Settings.Accent, Settings.Opacity, Settings.CornerRadius, Settings.FontFamily);

        // Die Einführung: alle sieben Schritte dunkel, der erste auch hell.
        foreach (var step in new[] { "willkommen", "schluessel", "hotkey", "fragen", "befehle", "aussehen", "fertig" })
        {
            SaveWelcomeShot(folder, "dunkel", step);
        }

        SaveWelcomeShot(folder, "hell", "willkommen");
        Theme.Apply(Settings.Theme, Settings.Accent, Settings.Opacity, Settings.CornerRadius, Settings.FontFamily);

        SaveBarShot(folder, "leiste-vorschlaege.png", "", "", "");
        SaveBarShot(
            folder,
            "leiste-antwort.png",
            "Was ist Strom?",
            "Strom ist die **Bewegung von Ladungsträgern**.\n\n"
            + "- In Metallen wandern Elektronen.\n"
            + "- Gemessen wird in Ampere.\n\n"
            + "```python\nprint(230 * 16)\n```",
            "gpt-oss-20b · 240 → 96 Tokens · unter $0,0001 · 0,7 s");

        Log.Info($"Bildschirmfotos geschrieben nach {folder}");
    }

    /// <summary>Ein Bild der Einstellungen in einer Farbstimmung.</summary>
    private void SaveSettingsShot(string folder, string theme, string area)
    {
        Theme.Apply(theme, Settings.Accent, Settings.Opacity);
        var window = new SettingsWindow(Settings, Chat);
        Shot.SaveWindow(
            window,
            Path.Combine(folder, $"einstellungen-{theme}-{area}.png"),
            () => window.SelectAreaForTest(area));
    }

    /// <summary>Ein Bild der Einführung in einer Farbstimmung und einem Schritt.</summary>
    private void SaveWelcomeShot(string folder, string theme, string step)
    {
        Theme.Apply(theme, Settings.Accent, Settings.Opacity, Settings.CornerRadius, Settings.FontFamily);
        var window = new WelcomeWindow(Settings, Chat);
        Shot.SaveWindow(
            window,
            Path.Combine(folder, $"einfuehrung-{theme}-{step}.png"),
            () => window.GoToStepForTest(step));
    }

    /// <summary>Die Leiste ohne Fenster messen und zeichnen — so blitzt nichts auf.</summary>
    private void SaveBarShot(string folder, string name, string title, string markdown, string meta)
    {
        var bar = new MainWindow(Chat);
        if (bar.Content is not FrameworkElement root)
        {
            return;
        }

        bar.Content = null;
        bar.FillForShot(title, markdown, meta);
        Shot.SaveElement(root, Path.Combine(folder, name), Settings.WindowWidth);
        bar.Close();
    }

    private void RunSmoke(double seconds)
    {
        var started = Stopwatch.StartNew();
        var report = EnsureReport();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var bar = EnsureBar();
            bar.ShowBar();
            report.Line("smoke: leiste_sichtbar=True");
            report.Line($"smoke: ms_bis_leiste={started.Elapsed.TotalMilliseconds:0}");
            report.Line($"smoke: hotkey={Settings.Hotkey} registriert={(_hotkeyProblem.Length == 0 ? "True" : "False")}");
            report.Line($"smoke: groesse={bar.ActualWidth:0}x{bar.ActualHeight:0} links={bar.Left:0} oben={bar.Top:0}");

            // Tray und Vorschläge entstehen bewusst nach dem ersten Bild.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                report.Line($"smoke: tray={(_tray is null ? "nein" : "ja")} schluessel={Chat.HasKey}");
                report.Line($"smoke: vorschlaege_im_leerlauf={bar.SuggestionCountForTest}");
                report.Line($"smoke: eintraege_im_verlauf={History.All().Count}");
                report.Flush();
            }));
        }));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            report.Line($"smoke: ms_gesamt={started.Elapsed.TotalMilliseconds:0}");
            report.Flush();
            Shutdown(0);
        };
        timer.Start();
    }

    /// <summary>Kleine Hilfe: Ausgabe in Datei (die EXE hat keine Konsole) und auf die Konsole.</summary>
    private sealed class Report : IDisposable
    {
        private readonly StreamWriter? _writer;

        public Report(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var full = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                _writer = new StreamWriter(full, append: false) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Report nicht schreibbar: {ex.Message}");
            }
        }

        public void Line(string text)
        {
            Console.WriteLine(text);
            _writer?.WriteLine(text);
        }

        public void Flush() => _writer?.Flush();

        public void Dispose() => _writer?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            SyncAutostart();
            _hotkey?.Dispose();
            _tray?.Dispose();
            _trayIconImage?.Dispose();
            _client?.Dispose();
            // Die Einzelinstanz gehört dem Einstiegspunkt (Program) — dort wird sie freigegeben.
            _report?.Dispose();
            Log.Info("AIlight beendet");
        }
        catch (Exception ex)
        {
            Log.Warn($"Aufräumen beim Beenden: {ex.Message}");
        }

        base.OnExit(e);
    }

    public void AttachSingleInstance(SingleInstance single)
    {
        _single = single;
        single.Listen();
        single.MessageReceived += message => Dispatcher.Invoke(() =>
        {
            if (message.Contains("quit", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Beenden wurde von einer zweiten Instanz angefordert");
                Shutdown();
            }
            else
            {
                Log.Info("Eine zweite Instanz hat die Leiste angefordert");
                ShowBar();
            }
        });
    }
}

/// <summary>Kleiner Wrapper, damit der Tray-Haken mit den Einstellungen synchron bleibt.</summary>
public sealed class ToolStripMenuItemHost
{
    private readonly ToolStripMenuItem _item;

    public ToolStripMenuItemHost(ToolStripMenuItem item) => _item = item;

    public void Update(bool isChecked)
    {
        if (_item.Checked != isChecked)
        {
            _item.Checked = isChecked;
        }
    }
}
