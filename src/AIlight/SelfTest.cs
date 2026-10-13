using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using AIlight.Core;
using AIlight.Groq;
using AIlight.Views;

namespace AIlight;

/// <summary>
/// Eingebauter Prüfstand: <c>AIlight.exe --selftest</c>. Ohne Test-Framework,
/// ohne Netzzugriff — prüft genau die Stellen, an denen Fehler teuer sind
/// (Hotkey-Umrechnung, Einstellungen, Verlauf, Markdown, Oberflächenaufbau).
/// Exit-Code 0 = alles grün.
/// </summary>
public static class SelfTest
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    public static int RunAll()
    {
        try
        {
            // In einem Fensterprogramm gibt es nicht immer eine Konsole (z. B. wenn die
            // EXE per Doppelklick gestartet wurde) — Ausgaben dürfen nie abstürzen.
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
            // Ohne Konsole wird eben nichts gedruckt.
        }

        Console.WriteLine("AIlight — Selbsttest");
        Console.WriteLine(new string('-', 60));

        TestHotkeySpec();
        TestSettings();
        TestModels();
        TestCommands();
        TestThinkSplitter();
        TestMarkdown();
        TestHistory();
        TestSecrets();
        TestPayload();
        TestSuggestions();
        TestUserInterface();

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"Ergebnis: {_passed} von {_passed + Failures.Count} Prüfungen bestanden");
        foreach (var failure in Failures)
        {
            Console.WriteLine("  rot: " + failure);
        }

        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Check(string name, Action action)
    {
        try
        {
            action();
            _passed++;
            Console.WriteLine($"[ OK ] {name}");
        }
        catch (Exception ex)
        {
            Failures.Add($"{name} — {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[ROT ] {name} — {ex.Message}");
        }
    }

    /// <summary>Farbe an einer Stelle eines Fensters, angegeben in Koordinaten eines Kindelements.</summary>
    private static System.Windows.Media.Color OnWindow(Window window, FrameworkElement child, double x, double y)
    {
        var point = child.TransformToAncestor(window).Transform(new Point(x, y));
        return Shot.PixelAt(window, point.X, point.Y);
    }

    /// <summary>
    /// Punkt im gerenderten Bild eines abgehängten Baums: die Koordinaten eines
    /// Kindelements plus den Rand, den der Baum im Fenster gehabt hätte.
    /// </summary>
    private static Point InBitmap(FrameworkElement root, FrameworkElement child, double x, double y)
    {
        var point = child.TransformToAncestor(root).Transform(new Point(x, y));
        return new Point(point.X + root.Margin.Left, point.Y + root.Margin.Top);
    }

    /// <summary>Zwei Farben gelten als gleich, wenn sie deutlich nah beieinander liegen.</summary>
    private static bool Near(System.Windows.Media.Color a, System.Windows.Media.Color b, int tolerance = 42)
        => Math.Abs(a.R - b.R) <= tolerance
        && Math.Abs(a.G - b.G) <= tolerance
        && Math.Abs(a.B - b.B) <= tolerance;

    private static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{what}: erwartet „{expected}“, war „{actual}“");
        }
    }

    private static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(what);
        }
    }

    // ── Hotkey ──────────────────────────────────────────────────────────────

    private static void TestHotkeySpec()
    {
        Check("Hotkey: „Alt+Space“ wird zu MOD_ALT + 0x20", () =>
        {
            True(HotKeySpec.TryParse("Alt+Space", out var modifiers, out var key, out _), "Parse fehlgeschlagen");
            Equal(HotKey.MOD_ALT, modifiers, "Modifikatoren");
            Equal(0x20u, key, "Tastencode");
        });

        Check("Hotkey: „Strg+Umschalt+C“ (deutsche Schreibweise)", () =>
        {
            HotKeySpec.TryParse("Strg+Umschalt+C", out var modifiers, out var key, out _);
            Equal(HotKey.MOD_CONTROL | HotKey.MOD_SHIFT, modifiers, "Modifikatoren");
            Equal((uint)'C', key, "Tastencode");
        });

        Check("Hotkey: F-Tasten und Anzeigetext", () =>
        {
            HotKeySpec.TryParse("Ctrl+F5", out var modifiers, out var key, out _);
            Equal(0x74u, key, "F5");
            Equal("Ctrl+F5", HotKeySpec.Format(modifiers, key), "Anzeigetext");
        });

        Check("Hotkey: ohne Zusatztaste kommt ein lesbarer Satz", () =>
        {
            var ok = HotKeySpec.TryParse("F5", out _, out _, out var error);
            True(!ok, "F5 allein darf nicht gültig sein");
            True(error.Contains("Zusatztaste"), $"Fehlertext unerwartet: {error}");
        });

        Check("Hotkey: unbekannte Taste wird erklärt", () =>
        {
            var ok = HotKeySpec.TryParse("Alt+Foo", out _, out _, out var error);
            True(!ok, "Alt+Foo darf nicht gültig sein");
            True(error.Contains("Foo"), $"Fehlertext nennt die Taste nicht: {error}");
        });

        Check("Hotkey: Tastendruck aus dem Fenster wird übersetzt", () =>
        {
            Equal("Ctrl+Alt+Space", HotKeySpec.FromKeyEvent(Key.Space, ModifierKeys.Control | ModifierKeys.Alt), "Kombination");
            Equal("", HotKeySpec.FromKeyEvent(Key.Space, ModifierKeys.None), "ohne Zusatztaste leer");
        });
    }

    // ── Einstellungen ───────────────────────────────────────────────────────

    private static void TestSettings()
    {
        Check("Einstellungen: Standardwerte sind vernünftig", () =>
        {
            var settings = new AppSettings();
            Equal("Alt+Space", settings.Hotkey, "Hotkey");
            Equal("auto", settings.Model, "Modell");
            True(settings.StartWithWindows, "Autostart standardmäßig an");
            True(settings.WindowWidth >= 420, "Breite plausibel");
        });

        Check("Einstellungen: Speichern und Laden über einen Temporärpfad", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-test-").FullName;
            var file = Path.Combine(folder, "settings.json");
            try
            {
                var settings = AppSettings.Load(file);   // schreibt nur in die Temporärdatei
                settings.Hotkey = "Ctrl+Shift+K";
                settings.Model = GroqModels.Large.Id;
                settings.FontSize = 20;
                settings.Save();
                var loaded = AppSettings.Load(file);
                Equal("Ctrl+Shift+K", loaded.Hotkey, "Hotkey");
                Equal(GroqModels.Large.Id, loaded.Model, "Modell");
                Equal(20d, loaded.FontSize, "Schriftgröße");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Einstellungen: unsinnige Werte werden begrenzt", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-test-").FullName;
            var file = Path.Combine(folder, "settings.json");
            try
            {
                File.WriteAllText(file, """{"WindowWidth": 9999, "FontSize": 2, "Opacity": 5, "Theme": "neon"}""");
                var loaded = AppSettings.Load(file);
                Equal(1600d, loaded.WindowWidth, "Breite begrenzt");
                Equal(12d, loaded.FontSize, "Schriftgröße begrenzt");
                Equal(1.0d, loaded.Opacity, "Deckkraft begrenzt");
                Equal("system", loaded.Theme, "unbekanntes Thema ersetzt");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Einstellungen: abgeleitete Werte (Tokens, Reasoning)", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-tok-").FullName;
            var settings = AppSettings.Load(Path.Combine(folder, "settings.json"));
            settings.AnswerLength = "kurz";
            Equal(512, settings.MaxCompletionTokens, "kurz");
            True(settings.Reasoning is null, "default bedeutet: Parameter weglassen");
            settings.ReasoningEffort = "hoch";
            Equal("high", settings.Reasoning, "hoch");
            Directory.Delete(folder, recursive: true);
        });
    }

    // ── Modell ──────────────────────────────────────────────────────────────

    private static void TestModels()
    {
        Check("Modell: „auto“ wählt bei kurzer Frage das kleine Modell", () =>
        {
            var (model, _) = GroqModels.Resolve("auto", "Was ist 2+2?", webSearch: false);
            Equal(GroqModels.Small.Id, model.Id, "Modell");
        });

        Check("Modell: „auto“ wählt bei Websuche das große Modell", () =>
        {
            var (model, _) = GroqModels.Resolve("auto", "kurz", webSearch: true);
            Equal(GroqModels.Large.Id, model.Id, "Modell");
        });

        Check("Modell: feste Wahl wird nicht überstimmt", () =>
        {
            var (model, reason) = GroqModels.Resolve(GroqModels.Small.Id, new string('x', 900), webSearch: true);
            Equal(GroqModels.Small.Id, model.Id, "Modell");
            True(reason.Length > 0, "Begründung fehlt");
        });

        Check("Modell: Kosten werden gerechnet und lesbar formatiert", () =>
        {
            var cost = GroqModels.Small.Cost(1_000_000, 1_000_000);
            Equal(0.375d, Math.Round(cost, 6), "Preis 20b");
            True(GroqModels.FormatCost(0.00023).Contains("0,0002"), "Formatierung");
            Equal("~$0", GroqModels.FormatCost(0), "Nullkosten");
        });
    }

    // ── Befehle ─────────────────────────────────────────────────────────────

    private static void TestCommands()
    {
        Check("Befehle: „/history Urlaub“ wird zerlegt", () =>
        {
            var split = Commands.Split("/history Urlaub");
            True(split is not null, "Befehl nicht erkannt");
            var (command, argument) = split!.Value;
            Equal("history", command.Name, "Name");
            Equal("Urlaub", argument, "Argument");
        });

        Check("Befehle: „#new“ bleibt gültig (alte Schreibweise)", () =>
        {
            var split = Commands.Split("#new");
            True(split?.Command.Name == "new", "„#new“ wurde nicht erkannt");
        });

        Check("Befehle: Unbekanntes liefert nichts", () =>
            True(Commands.Split("/gibtsnicht") is null, "unbekannter Befehl wurde akzeptiert"));

        Check("Befehle: Teilwort schlägt passende Befehle vor", () =>
        {
            var matches = Commands.Matching("/h").Select(c => c.Name).ToList();
            True(matches.Contains("history") && matches.Contains("help"), $"Treffer: {string.Join(",", matches)}");
        });
    }

    private static void TestThinkSplitter()
    {
        Check("Denk-Schritte: Text in <think> wird abgetrennt", () =>
        {
            var reasoning = new StringBuilder();
            var pieces = ThinkSplitter.Split("Hallo <think>kurz überlegen</think>Welt", reasoning).ToList();
            Equal("Hallo Welt", string.Concat(pieces), "sichtbarer Text");
            Equal("kurz überlegen", reasoning.ToString(), "Denk-Text");
        });

        Check("Denk-Schritte: offener Block bleibt im Nachdenken", () =>
        {
            var reasoning = new StringBuilder();
            var pieces = ThinkSplitter.Split("Antwort <think>noch offen", reasoning).ToList();
            Equal("Antwort ", string.Concat(pieces), "sichtbarer Text");
            Equal("noch offen", reasoning.ToString(), "Denk-Text");
        });
    }

    // ── Markdown ────────────────────────────────────────────────────────────

    private static void TestMarkdown()
    {
        var sample = """
            ## Überschrift

            Ein **fetter** Satz mit `Code`.

            - Punkt eins
            - Punkt zwei

            ```
            Zeile 1
            Zeile 2
            ```

            > Zitat
            """;

        Check("Markdown: Absätze, Listen und Codeblöcke entstehen", () =>
        {
            var blocks = Markdown.Render(sample, 16);
            True(blocks.Count >= 5, $"zu wenige Blöcke: {blocks.Count}");
            True(blocks.Any(b => b is Border { Child: TextBlock }), "Codeblock fehlt");
        });

        Check("Markdown: fette Schrift wird erkannt", () =>
        {
            var blocks = Markdown.Render("Ein **fetter** Satz", 16);
            var text = (TextBlock)blocks[0];
            True(text.Inlines.OfType<Run>().Any(run => run.FontWeight == FontWeights.SemiBold), "fett fehlt");
        });

        Check("Markdown: Listenzeilen bekommen Aufzählungszeichen", () =>
        {
            var blocks = Markdown.Render("- eins\n- zwei", 16);
            var panel = (StackPanel)blocks[0];
            Equal(2, panel.Children.Count, "Listenpunkte");
        });

        Check("Markdown: leerer Text ergibt nichts", () =>
            Equal(0, Markdown.Render("", 16).Count, "Blöcke"));
    }

    // ── Verlauf ─────────────────────────────────────────────────────────────

    private static void TestHistory()
    {
        Check("Verlauf: schreiben, lesen, suchen, löschen, Statistiken", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-hist-").FullName;
            var file = Path.Combine(folder, "history.jsonl");
            try
            {
                var store = new HistoryStore(file);
                store.Add(new HistoryEntry("a-1", DateTimeOffset.Now.AddMinutes(-10), "Was ist Wasser?", "H2O", "m", 10, 5, 0.001));
                store.Add(new HistoryEntry("a-2", DateTimeOffset.Now, "Wie hoch ist der Everest?", "8848 m", "m", 20, 8, 0.002));
                Equal(2, store.All().Count, "Einträge");
                Equal("a-2", store.Recent(1)[0].Id, "neuester zuerst");
                Equal(1, store.Search("Wasser", 5).Count, "Suche");
                Equal("8848 m", store.ById("a-2")?.Answer, "Suche über Kennung");
                var (count, tokensIn, tokensOut, cost, _) = store.Stats();
                Equal(2, count, "Anzahl");
                Equal(30, tokensIn, "Tokens ein");
                Equal(13, tokensOut, "Tokens aus");
                Equal(0.003d, Math.Round(cost, 6), "Kosten");
                Equal(1, store.Delete("Wasser"), "gelöscht");
                Equal(1, store.All().Count, "Rest");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Verlauf: kaputte Zeilen verderben den Rest nicht", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-hist-").FullName;
            var file = Path.Combine(folder, "history.jsonl");
            try
            {
                File.WriteAllText(file, "{\"Id\":\"x\",\"Question\":\"ok\"}\nkeine json zeile\n");
                var store = new HistoryStore(file);
                Equal(1, store.All().Count, "gültige Einträge");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Verlauf: Aufbewahrung schneidet nur Altes ab", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-hist-").FullName;
            var file = Path.Combine(folder, "history.jsonl");
            try
            {
                var store = new HistoryStore(file);
                store.Add(new HistoryEntry("alt", DateTimeOffset.Now.AddDays(-40), "alt", "alt", "m", 1, 1, 0));
                store.Add(new HistoryEntry("neu", DateTimeOffset.Now, "neu", "neu", "m", 1, 1, 0));
                Equal(1, store.Prune(30), "gelöscht");
                Equal("neu", store.All()[0].Id, "Rest");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    // ── Schlüsselspeicher ───────────────────────────────────────────────────

    private static void TestSecrets()
    {
        Check("Schlüssel: speichern, lesen, löschen (eigener Testeintrag)", () =>
        {
            // Wichtig: hier werden nur Vergleiche gemeldet, nie Werte — sonst
            // könnte ein echter Schlüssel im Protokoll des Prüfstands landen.
            const string target = "AIlight:selftest";
            True(SecretStore.Write("testwert", target), "Schreiben fehlgeschlagen");
            True(SecretStore.Read(target) == "testwert", "Gelesener Wert stimmt nicht");
            True(SecretStore.HasKey(target), "HasKey meldet nichts");
            True(SecretStore.Delete(target), "Löschen fehlgeschlagen");
            True(SecretStore.Read(target).Length == 0, "nach dem Löschen ist noch ein Eintrag vorhanden");
            True(!SecretStore.HasKey(target), "HasKey findet einen gelöschten Eintrag");
        });
    }

    // ── Anfrage-Aufbau ──────────────────────────────────────────────────────

    private static void TestPayload()
    {
        Check("Anfrage: JSON enthält Modell, Streaming, Nachrichten und Token-Limit", () =>
        {
            var request = new AskRequest
            {
                Model = GroqModels.Small.Id,
                Messages = new[] { new ChatMessage("system", "Hallo"), new ChatMessage("user", "Frage") },
                MaxCompletionTokens = 2048,
                ReasoningEffort = "medium",
            };

            using var document = JsonDocument.Parse(GroqClient.BuildPayload(request));
            var root = document.RootElement;
            Equal("openai/gpt-oss-20b", root.GetProperty("model").GetString(), "Modell");
            True(root.GetProperty("stream").GetBoolean(), "stream=true");
            Equal(2048, root.GetProperty("max_completion_tokens").GetInt32(), "Token-Limit");
            Equal(2, root.GetProperty("messages").GetArrayLength(), "Nachrichten");
            Equal("medium", root.GetProperty("reasoning_effort").GetString(), "Denk-Aufwand");
        });

        Check("Anfrage: Websuche wird als eingebautes Werkzeug angehängt", () =>
        {
            var request = new AskRequest
            {
                Model = GroqModels.Large.Id,
                Messages = new[] { new ChatMessage("user", "Neuigkeiten?") },
                WebSearch = true,
            };

            using var document = JsonDocument.Parse(GroqClient.BuildPayload(request));
            var tools = document.RootElement.GetProperty("tools");
            Equal(1, tools.GetArrayLength(), "Werkzeuge");
            Equal("browser_search", tools[0].GetProperty("type").GetString(), "Werkzeugtyp");
        });

        Check("Anfrage: ohne Denk-Aufwand fehlt das Feld ganz", () =>
        {
            var request = new AskRequest
            {
                Model = GroqModels.Small.Id,
                Messages = new[] { new ChatMessage("user", "x") },
            };

            using var document = JsonDocument.Parse(GroqClient.BuildPayload(request));
            True(!document.RootElement.TryGetProperty("reasoning_effort", out _), "Feld sollte fehlen");
            True(!document.RootElement.TryGetProperty("tools", out _), "Werkzeuge sollten fehlen");
        });
    }

    // ── Vorschläge ──────────────────────────────────────────────────────────

    private static void TestSuggestions()
    {
        Check("Vorschläge: leerer Eingabebereich zeigt Befehle und frühere Fragen", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-sug-").FullName;
            try
            {
                var store = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                store.Add(new HistoryEntry("a-1", DateTimeOffset.Now, "Was ist Strom?", "Bewegte Ladung", "m", 1, 1, 0));
                var settings = new AppSettings { MaxSuggestions = 6 };
                var rows = SuggestionBuilder.Build("", store, settings, hasAnswer: false);
                True(rows.Count >= 3, $"zu wenige Zeilen: {rows.Count}");
                True(rows.Any(r => r.Kind == SuggestionKind.History), "Verlaufszeile fehlt");
                True(rows.Any(r => r.Kind == SuggestionKind.Command), "Befehlszeile fehlt");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Vorschläge: getippte Frage zeigt genau eine Fragezeile", () =>
        {
            var store = new HistoryStore(Path.Combine(Path.GetTempPath(), "ailight-egal.jsonl"));
            var rows = SuggestionBuilder.Build("Was kostet ein Haus?", store, new AppSettings(), hasAnswer: false);
            Equal(1, rows.Count, "Zeilen");
            Equal(SuggestionKind.Ask, rows[0].Kind, "Art");
            True(rows[0].Subtitle.Contains("gpt-oss"), "Modellhinweis fehlt");
        });

        Check("Vorschläge: „/h“ schlägt Befehle vor", () =>
        {
            var store = new HistoryStore(Path.Combine(Path.GetTempPath(), "ailight-egal.jsonl"));
            var rows = SuggestionBuilder.Build("/h", store, new AppSettings(), hasAnswer: false);
            True(rows.All(r => r.Kind == SuggestionKind.Command), "nur Befehle erwartet");
            True(rows.Count >= 2, "zu wenige Befehle");
        });
    }

    // ── Oberfläche ──────────────────────────────────────────────────────────

    private static void TestUserInterface()
    {
        Check("Oberfläche: Themen-Ressourcen und Leiste bauen sich fehlerfrei auf", () =>
        {
            var app = Application.Current;
            if (app is null)
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            if (!app.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.Contains("Theme") == true))
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Views/Theme.xaml", UriKind.Absolute),
                });
            }

            Theme.Apply("dunkel", "#4C8DFF", 0.98);

            var folder = Directory.CreateTempSubdirectory("ailight-ui-").FullName;
            try
            {
                var settings = new AppSettings { MaxSuggestions = 6 };
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                // Eine frühere Frage, damit die Liste wirklich beide Gruppen zeigt.
                history.Add(new HistoryEntry("h-1", DateTimeOffset.Now, "Was ist Strom?",
                    "Bewegte Ladung", GroqModels.Small.Id, 1, 1, 0));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);

                bar.ApplySettings();
                True(bar.SuggestionCount > 0, "keine Vorschläge aufgebaut");

                bar.SetInputForTest("/h");
                True(bar.SuggestionCount >= 2, "Befehlsvorschläge fehlen");

                // Im leeren Feld stehen erst frühere Fragen, dann Befehle:
                // dazwischen genau eine Trennlinie, sonst keine.
                bar.SetInputForTest("");
                var separators = bar.SuggestionSeparatorsForTest;
                Equal(1, separators.Count(x => x), "Trennlinien zwischen den Gruppen");
                True(bar.SuggestionSeparatorCountForTest == 1, "Gruppenwechsel nicht markiert");

                // Nur eine Fragezeile: keine Linie, kein Schnickschnack.
                bar.SetInputForTest("Was ist Strom?");
                Equal(1, bar.SuggestionCount, "Fragezeilen");
                Equal(0, bar.SuggestionSeparatorCountForTest, "Trennlinie ohne Gruppenwechsel");

                bar.SetInputForTest("");
                bar.ShowTextForTest("Test", "**fett** und `code`");
                True(bar.AnswerBlockCount > 0, "Antwortbereich blieb leer");

                var result = new AskResult("Antwort", "", GroqModels.Small.Id, 100, 50, 0.000015, TimeSpan.FromSeconds(1.2), "stop", false, false);
                True(MainWindow.FooterForTest(result).Contains("20b"), "Fußzeile ohne Modell");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Oberfläche: Einstellungsfenster baut sich auf und übernimmt Werte", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-set-").FullName;
            try
            {
                // Über eine Temporärdatei laden: so bleiben die echten Einstellungen unberührt.
                var settings = AppSettings.Load(Path.Combine(folder, "settings.json"));
                settings.Hotkey = "Ctrl+Alt+K";
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var window = new SettingsWindow(settings, chat);

                // Werte ändern und prüfen, dass sie sofort in den Einstellungen landen.
                window.ApplyChoiceForTest("ModelBox", GroqModels.Small.Id);
                Equal(GroqModels.Small.Id, settings.Model, "Modell aus dem Fenster");
                window.ApplySliderForTest("WidthSlider", 900);
                Equal(900d, settings.WindowWidth, "Breite aus dem Fenster");

                // Bereiche: links wählen, rechts erscheint genau eine Seite.
                True(window.AreaKeysForTest.Count >= 7, $"zu wenige Bereiche: {window.AreaKeysForTest.Count}");
                window.SelectAreaForTest("aussehen");
                Equal("aussehen", window.VisibleAreaForTest, "gewählter Bereich");
                True(window.IsAreaVisibleForTest("aussehen"), "Seite Aussehen ist nicht sichtbar");
                True(!window.IsAreaVisibleForTest("konto"), "alte Seite blieb sichtbar");

                // Die Vorschau muss den Reglern folgen — sonst zeigt sie Unsinn.
                window.ApplySliderForTest("FontSlider", 22);
                True(window.PreviewForTest.Opacity is > 0.5 and <= 1.0, "Vorschau ohne Deckkraft");
                window.ApplySliderForTest("OpacitySlider", 0.8);
                Equal(0.8, Math.Round(window.PreviewForTest.Opacity, 1), "Deckkraft der Vorschau");

                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Oberfläche: Regler zeigt den gefüllten Teil in der Akzentfarbe", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-slider-").FullName;
            try
            {
                var settings = AppSettings.Load(Path.Combine(folder, "settings.json"));
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var window = new SettingsWindow(settings, chat);

                // Auf halben Weg stellen und wirklich zeichnen lassen.
                window.SelectAreaForTest("aussehen");
                window.ApplySliderForTest("WidthSlider", 810);
                var slider = (Slider)window.FindName("WidthSlider")!;
                Shot.Show(window);

                var accent = ((SolidColorBrush)Application.Current.Resources["Brush.Accent"]).Color;
                var y = slider.ActualHeight / 2;
                var left = OnWindow(window, slider, 2, y);
                var right = OnWindow(window, slider, Math.Max(2, slider.ActualWidth - 4), y);

                True(Near(left, accent), $"links nicht im Akzent: {left}");
                True(!Near(right, accent), $"rechts auch im Akzent: {right}");
                True(right.A > 0, "der Regler zeichnet rechts gar nichts");

                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Oberfläche: Vorschlagszeile markiert die Auswahl mit dem Akzent", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-row-").FullName;
            try
            {
                var settings = new AppSettings { MaxSuggestions = 6 };
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);
                bar.ApplySettings();

                var root = (FrameworkElement)bar.Content;
                Shot.Measure(root, 680);

                // Die erste Zeile ist ausgewählt: links muss der Akzentstreifen stehen.
                // Die Zeile hat 8 Rand und 10 Innenabstand — der Streifen beginnt also
                // bei 18 und ist drei Punkte breit.
                var list = (ItemsControl)bar.FindName("SuggestionList")!;
                var row = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0)!;
                var accent = ((SolidColorBrush)Application.Current.Resources["Brush.Accent"]).Color;
                var markPoint = InBitmap(root, row, 19, row.ActualHeight / 2);
                var mark = Shot.PixelAt(root, markPoint.X, markPoint.Y);
                True(Near(mark, accent), $"Auswahlstreifen fehlt: {mark}");

                var plain = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(3)!;
                var plainPoint = InBitmap(root, plain, 19, plain.ActualHeight / 2);
                var plainMark = Shot.PixelAt(root, plainPoint.X, plainPoint.Y);
                True(!Near(plainMark, accent), $"andere Zeile auch markiert: {plainMark}");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Oberfläche: alle Gestaltungsvorlagen sind vorhanden", () =>
        {
            var resources = Application.Current.Resources;
            foreach (var key in new[]
            {
                "Card", "Line", "Panel", "Chrome.Shell", "Chrome.Button", "Chrome.Close",
                "Nav.Item", "Button.Primary", "Button.Ghost", "Button.Danger", "Bar.Button",
                "Field.Text", "Field.Multiline", "Field.Secret", "Box.Select", "Box.Item",
                "Switch", "Swatch", "Slider.Slim", "Suggest.Row", "Suggest.Subtitle",
                "Brush.Card", "Brush.Hover", "Brush.Track", "Brush.Field", "Brush.Ring",
            })
            {
                True(resources.Contains(key), $"Vorlage fehlt: {key}");
            }
        });

        Check("Oberfläche: helles und dunkles Thema setzen unterschiedliche Farben", () =>
        {
            Theme.Apply("hell", "#4C8DFF", 1.0);
            var light = ((SolidColorBrush)Application.Current.Resources["Brush.Text"]).Color;
            Theme.Apply("dunkel", "#4C8DFF", 1.0);
            var dark = ((SolidColorBrush)Application.Current.Resources["Brush.Text"]).Color;
            True(light != dark, "beide Themen zeigen dieselbe Textfarbe");
        });

        Check("Symbol: das Infobereich-Symbol lädt (Datei oder eingebettet)", () =>
        {
            // Windows wählt aus einem Symbol die nächstpassende Größe — geprüft wird
            // deshalb, dass überhaupt ein brauchbares, quadratisches Symbol entsteht.
            using var icon = Icons.Load(32);
            True(icon.Width >= 16 && icon.Height >= 16, $"Symbol zu klein: {icon.Width}×{icon.Height}");
            Equal(icon.Width, icon.Height, "Symbol ist nicht quadratisch");
        });
    }
}
