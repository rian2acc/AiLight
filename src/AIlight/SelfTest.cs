using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
        TestMotion();
        TestUserInterface();
        // Alles, was die geladenen Themen-Ressourcen braucht, kommt danach:
        // die Übergänge des Aufbaus und die Marke.
        TestBuildMotion();
        TestMark();

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
            foreach (var line in (ex.StackTrace ?? "").Split('\n')
                .Select(part => part.Trim())
                .Where(part => part.StartsWith("at ", StringComparison.Ordinal))
                .Take(3))
            {
                Console.WriteLine("       " + line);
            }
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

        Check("Einstellungen: Ecken-Radius und Schrift werden begrenzt", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-anp-").FullName;
            var file = Path.Combine(folder, "settings.json");
            try
            {
                File.WriteAllText(file, """{"CornerRadius": 99, "FontFamily": "comic"}""");
                var loaded = AppSettings.Load(file);
                Equal(22d, loaded.CornerRadius, "Radius begrenzt");
                Equal("system", loaded.FontFamily, "unbekannte Schrift ersetzt");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }

            var fresh = new AppSettings();
            Equal(16d, fresh.CornerRadius, "Standard-Radius");
            True(fresh.AnimateBar, "Animationen sind standardmäßig an");
            True(fresh.ShowMeta, "Fußzeile ist standardmäßig an");
            True(!fresh.ShowThinking, "Denk-Schritte sind standardmäßig eingeklappt");
            True(Theme.IsKnownFont(fresh.FontFamily), "Standardschrift ist bekannt");
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

    // ── Bewegung ────────────────────────────────────────────────────────────

    /// <summary>Wartet, bis eine Bedingung wirklich gilt (höchstens zwei Sekunden).</summary>
    private static bool WaitFor(DispatcherObject target, Func<bool> condition, int milliseconds = 2000)
    {
        var deadline = DateTime.Now.AddMilliseconds(milliseconds);
        while (DateTime.Now < deadline)
        {
            // Settle lässt den Nachrichtenumlauf arbeiten: nur so kommen Zeitgeber
            // und Animationen überhaupt voran.
            Shot.Settle(target);
            if (condition())
            {
                return true;
            }

            System.Threading.Thread.Sleep(20);
        }

        Shot.Settle(target);
        return condition();
    }

    /// <summary>
    /// Alle Übergänge einer Gestaltungsvorlage — aus der Vorlage selbst und aus
    /// den Auslösern des Stils (beide Wege kommen im Aufbau vor).
    /// </summary>
    private static List<DoubleAnimation> AnimationsIn(object resource)
    {
        var found = new List<DoubleAnimation>();
        switch (resource)
        {
            case Style style:
                foreach (var trigger in style.Triggers)
                {
                    Collect(trigger, found);
                }

                var template = style.Setters
                    .OfType<Setter>()
                    .FirstOrDefault(setter => setter.Property == Control.TemplateProperty)?.Value;
                if (template is ControlTemplate fromStyle)
                {
                    foreach (var trigger in fromStyle.Triggers)
                    {
                        Collect(trigger, found);
                    }
                }

                break;

            case ControlTemplate direct:
                foreach (var trigger in direct.Triggers)
                {
                    Collect(trigger, found);
                }

                break;
        }

        return found;
    }

    private static void Collect(TriggerBase trigger, List<DoubleAnimation> found)
    {
        if (trigger is not Trigger typed)
        {
            return;
        }

        foreach (TriggerAction action in typed.EnterActions)
        {
            Collect(action as BeginStoryboard, found);
        }

        foreach (TriggerAction action in typed.ExitActions)
        {
            Collect(action as BeginStoryboard, found);
        }
    }

    private static void Collect(BeginStoryboard? begin, List<DoubleAnimation> found)
    {
        if (begin?.Storyboard is not { } storyboard)
        {
            return;
        }

        foreach (var child in storyboard.Children)
        {
            if (child is DoubleAnimation animation)
            {
                found.Add(animation);
            }
        }
    }

    private static void TestMotion()
    {
        Check("Bewegung: kurze Zeiten, weiche Kurve", () =>
        {
            // Die Werte sind die Hausordnung der Bewegung: Wer sie ändert, soll
            // hier darüber stolpern, statt es auf einem Bildschirm zu spüren.
            True(Motion.ShowMs >= 100 && Motion.ShowMs <= 320, $"Auftritt unpassend lang: {Motion.ShowMs} ms");
            True(Motion.HideMs > 0 && Motion.HideMs <= Motion.ShowMs,
                "Der Abgang muss kürzer sein als der Auftritt — sonst wirkt das Schließen zäh.");
            True(Motion.GlideMs >= 80 && Motion.GlideMs <= 400, $"Scrollen unpassend: {Motion.GlideMs} ms");
            True(Motion.Rise > 0 && Motion.Rise <= 24, "Der Auftrieb darf nur ein Hauch sein.");
            True(Motion.Grow > 0.96 && Motion.Grow < 1.0, "Das Wachsen darf nur ein Hauch sein.");
        });

        Check("Bewegung: die Kurve läuft von 0 nach 1, ohne zurückzugehen", () =>
        {
            Equal(0d, Math.Round(Motion.Ease(0), 6), "Start");
            Equal(1d, Math.Round(Motion.Ease(1), 6), "Ende");
            True(Motion.Ease(0.5) > 0.5, "Die Kurve klingt nicht aus (wäre sie linear oder zäh)");
            True(Motion.Ease(0.25) > Motion.Ease(0.1), "Die Kurve wird nicht schneller");

            var previous = -1d;
            for (var step = 0; step <= 20; step++)
            {
                var value = Motion.Ease(step / 20.0);
                True(value >= previous, $"Die Kurve läuft bei {step}/20 zurück");
                previous = value;
            }
        });

        Check("Bewegung: Seitenwechsel ist ruhiger als ein Detail", () =>
        {
            True(Motion.PageMs > Motion.QuickMs && Motion.PageMs <= 400,
                $"Der Seitenwechsel ist unpassend getaktet: {Motion.PageMs} ms");
            True(Motion.PageRise > 0 && Motion.PageRise <= 16, "Der Auftrieb der Seite ist zu groß");
        });

        Check("Bewegung: die Hauskurve ist die steile macOS-Kurve, ohne Überschwingen", () =>
        {
            // Eine Kurve mit flachem Anfang wirkt zäh, eine mit Überschwingen
            // wirkt billig. Diese Werte sind die Hausordnung.
            var house = Motion.Soft as CubicBezierEase;
            True(house is not null, "Die Hauskurve ist keine kubische Bézier-Kurve");
            Equal(0.32, house!.X1, "Stützpunkt X1");
            Equal(0.72, house.Y1, "Stützpunkt Y1");
            Equal(0d, house.X2, "Stützpunkt X2");
            Equal(1d, house.Y2, "Stützpunkt Y2");

            // Steil los, dann langer Auslauf: nach der Hälfte der Zeit ist fast
            // alles unterwegs — genau das lässt eine Bewegung „flüssig“ aussehen.
            True(Motion.Ease(0.5) > 0.85, $"Die Kurve ist zu zahm: {Motion.Ease(0.5):0.00}");

            for (var step = 0; step <= 40; step++)
            {
                var value = Motion.Ease(step / 40.0);
                True(value >= -1e-9 && value <= 1 + 1e-9, $"Die Kurve verlässt 0…1 bei {step}/40: {value}");
            }

            // Die Rechnung selbst: Endpunkte sitzen genau, Mitte dazwischen.
            Equal(0d, CubicBezierEase.Solve(0, 0.32, 0.72, 0, 1), "Kurve am Anfang");
            Equal(1d, CubicBezierEase.Solve(1, 0.32, 0.72, 0, 1), "Kurve am Ende");
            True(CubicBezierEase.Solve(0.5, 0.32, 0.72, 0, 1) > 0.5, "Kurve in der Mitte");
        });

        Check("Bewegung: der Schalter von Windows wird beachtet", () =>
        {
            var wasEnabled = Motion.Enabled;
            var wasFollowing = Motion.FollowSystem;
            try
            {
                Motion.Enabled = false;
                True(!Motion.Moves, "Ohne Bewegung darf nichts laufen");
                Equal(false, Anim.On, "Auch die Übergänge müssen stehen");

                Motion.Enabled = true;
                Motion.FollowSystem = false;
                True(Motion.Moves, "Mit Hausschalter muss Bewegung laufen, egal was Windows sagt");

                Motion.FollowSystem = true;
                Equal(Motion.SystemAllows, Motion.Moves, "Der Systemschalter wird nicht beachtet");
            }
            finally
            {
                Motion.Enabled = wasEnabled;
                Motion.FollowSystem = wasFollowing;
            }
        });

        Check("Bewegung: ohne Bewegung springt das Scrollen sofort ans Ziel", () =>
        {
            var view = new ScrollViewer { Height = 60, Width = 300 };
            var content = new StackPanel();
            for (var line = 0; line < 60; line++)
            {
                content.Children.Add(new TextBlock { Text = $"Zeile {line}", Height = 20 });
            }

            view.Content = content;

            // Ein echtes (aber verstecktes) Fenster: nur darin hat ein Scrollbereich
            // wirklich eine Länge, an die man springen kann.
            var window = new Window { Width = 300, Height = 60, ShowInTaskbar = false, Content = view };
            Shot.Show(window);
            var glide = new Glide(view);

            var wasEnabled = Motion.Enabled;
            try
            {
                Equal(60d, view.ViewportHeight, "Sichtbare Höhe des Bereichs");
                True(view.ScrollableHeight > 300, $"Zu wenig Inhalt zum Scrollen: {view.ScrollableHeight}");

                Motion.Enabled = false;
                glide.To(300);
                True(!glide.IsRunning, "Ohne Bewegung darf kein Zeichentakt laufen");

                // Eine neue Scrollposition wirkt beim nächsten Anordnen — deshalb
                // erst anordnen, dann messen.
                Shot.Settle(window);
                view.UpdateLayout();
                Equal(300d, Math.Round(view.VerticalOffset), "Der Sprung ging nicht ans Ziel");

                // Nachziehen ohne Bewegung: es springt ans Ende, statt gar nichts zu tun.
                glide.Chase();
                True(!glide.IsRunning, "Auch das Nachziehen darf ohne Bewegung nicht laufen");
                Shot.Settle(window);
                view.UpdateLayout();
                Equal(view.ScrollableHeight, view.VerticalOffset, "Das Nachziehen blieb am alten Platz stehen");
            }
            finally
            {
                Motion.Enabled = wasEnabled;
                glide.Dispose();
                window.Close();
            }
        });
    }

    // ── Bewegung im Aufbau ──────────────────────────────────────────────────

    /// <summary>
    /// Die Übergänge in <c>Views/Theme.xaml</c> — sie tragen den größten Teil des
    /// Gefühls (Hover, Drücken, Auswahl). Geprüft wird deshalb nicht „sieht gut
    /// aus“, sondern: jede Bewegung hat eine Kurve und eine Dauer aus dem Haus.
    /// Ohne Kurve liefe sie linear, und genau das wirkt hart.
    /// </summary>
    private static void TestBuildMotion()
    {
        Check("Bewegung: die Übergänge im Aufbau benutzen dieselben Kurven und Zeiten", () =>
        {
            var resources = Application.Current.Resources;
            var curve = resources["Ease.Out"] as CubicBezierEase;
            True(curve is not null, "Die Kurve „Ease.Out“ fehlt im Aufbau");
            Equal(((CubicBezierEase)Motion.Out).X1, curve!.X1, "Stützpunkt der Hauskurve im Aufbau");
            Equal(((CubicBezierEase)Motion.Out).Y1, curve.Y1, "Stützpunkt der Hauskurve im Aufbau");

            var allowed = new[] { Motion.PressMs, Motion.QuickMs, Motion.LeaveMs, Motion.PageMs };
            var seen = 0;
            foreach (var key in new[]
                     {
                         "Button.Ghost", "Button.Primary", "Nav.Item", "Switch", "Swatch",
                         "Chrome.Button", "Suggest.Row", "Slider.Slim",
                     })
            {
                if (resources[key] is not { } resource)
                {
                    continue;
                }

                foreach (var animation in AnimationsIn(resource))
                {
                    seen++;
                    True(animation.EasingFunction is CubicBezierEase,
                        $"Ein Übergang in „{key}“ läuft ohne Kurve (linear)");
                    var milliseconds = (int)Math.Round(animation.Duration.TimeSpan.TotalMilliseconds);
                    True(allowed.Contains(milliseconds),
                        $"Unpassende Dauer in „{key}“: {milliseconds} ms");
                }
            }

            True(seen >= 20, $"Zu wenige Übergänge gefunden: {seen}");
        });
    }

    // ── Marke ───────────────────────────────────────────────────────────────

    private static void TestMark()
    {
        Check("Marke: der Lichtpunkt mit drei Strahlen ergibt A und I", () =>
        {
            True(Application.Current.Resources["Icon.Ai"] is GeometryGroup,
                "Die Marke fehlt als Vektorgrafik");
            var mark = (GeometryGroup)Application.Current.Resources["Icon.Ai"];
            var lines = mark.Children.OfType<LineGeometry>().ToList();
            Equal(4, lines.Count, "Strahlen und Querstrich");

            // Der mittlere Strahl steht senkrecht in der Mitte — das ist das I.
            var middle = lines.Single(line => line.StartPoint.X == 50 && line.EndPoint.X == 50);
            True(middle.StartPoint.Y > 20 && middle.EndPoint.Y > middle.StartPoint.Y,
                "Der mittlere Strahl läuft nicht nach unten");

            // Die beiden äußeren Strahlen gehen symmetrisch nach außen — die Schenkel des A.
            var left = lines.Single(line => line.StartPoint.X == 44);
            var right = lines.Single(line => line.StartPoint.X == 56);
            True(left.EndPoint.X < left.StartPoint.X && right.EndPoint.X > right.StartPoint.X,
                "Die äußeren Strahlen spreizen nicht");
            Equal(left.StartPoint.Y, right.StartPoint.Y, "Die Schenkel beginnen nicht auf einer Höhe");
            Equal(100d - right.EndPoint.X, left.EndPoint.X, "Die Schenkel sind nicht symmetrisch");

            // Der Querstrich liegt zwischen den Schenkeln und kreuzt den mittleren Strahl.
            var bar = lines.Single(line => line.StartPoint.Y == line.EndPoint.Y);
            True(bar.StartPoint.X < middle.StartPoint.X && bar.EndPoint.X > middle.StartPoint.X,
                "Der Querstrich reicht nicht über den mittleren Strahl");
            True(lines.Where(line => line.StartPoint.Y == line.EndPoint.Y).Count() == 1,
                "Es gibt mehr als einen Querstrich");
        });
    }

    // ── Oberfläche ──────────────────────────────────────────────────────────

    /// <summary>Wartet, bis das weiche Ausblenden durch ist (höchstens zwei Sekunden).</summary>
    private static void WaitUntilHidden(MainWindow bar)
    {
        var deadline = DateTime.Now.AddSeconds(2);
        while (bar.IsVisible && DateTime.Now < deadline)
        {
            Shot.Settle(bar);
            System.Threading.Thread.Sleep(25);
        }

        Shot.Settle(bar);
    }

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

                // Die Einführung lässt sich aus den Einstellungen erneut öffnen.
                window.SelectAreaForTest("verhalten");
                var welcomeButton = (Button)window.FindName("WelcomeButton")!;
                True(welcomeButton.IsEnabled && (string)welcomeButton.Content == "Einführung anzeigen",
                    "Der Knopf für die Einführung fehlt in den Einstellungen");

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

                // Erst zeigen (der Fensteraufbau braucht das), dann die Seite so
                // scrollen, dass der Regler wirklich im Bild liegt.
                Shot.Show(window);
                slider.BringIntoView();
                Shot.Settle(window);

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
                "Mark", "Icon.Ai", "Radius.Shell", "Radius.Card", "Radius.Field", "Radius.Row",
                "Brush.Card", "Brush.Hover", "Brush.Track", "Brush.Field", "Brush.Ring",
                "Chrome.Sheen", "Suggest.Row", "Suggest.Title", "Suggest.Glyph",
            })
            {
                True(resources.Contains(key), $"Vorlage fehlt: {key}");
            }
        });

        Check("Oberfläche: Ecken-Radius und Fußzeilen-Schalter erreichen die Leiste", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-radius-").FullName;
            try
            {
                var settings = new AppSettings { CornerRadius = 20, ShowMeta = false };
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);
                bar.ApplySettings();

                var shell = (CornerRadius)Application.Current.Resources["Radius.Shell"];
                Equal(20d, shell.TopLeft, "Fenster-Radius");

                // Die Fußzeile sitzt unten am Fenster: oben bleibt sie eckig,
                // unten folgt sie dem Radius (20 - 3).
                var footer = (Border)bar.FindName("Footer")!;
                Equal(17d, footer.CornerRadius.BottomRight, "Fußzeile folgt dem Radius");
                Equal(0d, footer.CornerRadius.TopLeft, "Fußzeile bleibt oben eckig");

                var meta = (TextBlock)bar.FindName("Meta")!;
                Equal(Visibility.Collapsed, meta.Visibility, "Fußzeile ausgeblendet");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Verhalten: die Leiste tritt zurück, wenn die Einstellungen öffnen", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-yield-").FullName;
            try
            {
                var settings = new AppSettings { AnimateBar = true };
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);

                // Außerhalb des Bildschirms zeigen: sichtbar wie im Betrieb, ohne Fokus zu klauen.
                Shot.Show(bar);
                True(bar.IsVisible, "Die Leiste ließ sich nicht zeigen");

                bar.YieldToSettings();

                // Sie verschwindet weich — nicht mit einem Schlag.
                True(bar.IsVisible, "Die Leiste verschwand hart statt weich");
                True(bar.IsHidingForTest, "Es läuft kein Ausblenden");

                WaitUntilHidden(bar);
                True(!bar.IsVisible, "Die Leiste schwebt weiter über den Einstellungen");
                True(!bar.IsHidingForTest, "Das Ausblenden wurde nicht abgeschlossen");

                // Danach steht sie wieder sauber da — der nächste Auftritt beginnt frisch.
                Equal(1d, Math.Round(bar.ShellOpacityForTest, 3), "Deckkraft in Grundstellung");
                Equal(0d, Math.Round(bar.ShellShiftForTest, 3), "Auftrieb in Grundstellung");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Verhalten: ohne Bewegung schließt die Leiste sofort", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-instant-").FullName;
            try
            {
                var settings = new AppSettings { AnimateBar = false };
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);
                Shot.Show(bar);

                bar.YieldToSettings();
                True(!bar.IsVisible, "Ohne Bewegung muss die Leiste sofort weg sein");

                // Die Grundstellung bleibt auch in diesem Weg sauber.
                Equal(1d, Math.Round(bar.ShellOpacityForTest, 3), "Deckkraft in Grundstellung");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Antwort: unveränderter Text wird nicht neu gezeichnet", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-flush-").FullName;
            try
            {
                var settings = new AppSettings();
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);
                bar.ApplySettings();

                bar.ShowTextForTest("Test", "**fett** und `code`");
                var afterShow = bar.AnswerRenderCountForTest;
                True(afterShow > 0, "Die Antwort wurde gar nicht aufgebaut");
                True(bar.AnswerBlockCount > 0, "Antwortbereich blieb leer");

                // Zwanzig Takte ohne neuen Buchstaben dürfen nichts neu aufbauen —
                // genau das ließ die Antwort früher flackern.
                for (var tick = 0; tick < 20; tick++)
                {
                    bar.FlushForTest();
                }

                Equal(afterShow, bar.AnswerRenderCountForTest, "Neuaufbauten ohne neuen Text");
                True(bar.AnswerBlockCount > 0, "Antwortbereich wurde geleert");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Oberfläche: die Leiste zeigt die neue Marke", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-mark-").FullName;
            try
            {
                var settings = new AppSettings();
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);
                bar.ApplySettings();

                var root = (FrameworkElement)bar.Content;
                Shot.Measure(root, 680);

                var chip = (Border)bar.FindName("BrandChip")!;
                var accent = ((SolidColorBrush)Application.Current.Resources["Brush.Accent"]).Color;
                var middleX = chip.ActualWidth / 2;
                var middleY = chip.ActualHeight / 2;

                // Die Mitte des Chips trifft den mittleren Strahl — das I der Marke.
                var middle = Shot.PixelAt(root, InBitmap(root, chip, middleX, middleY).X,
                    InBitmap(root, chip, middleX, middleY).Y);
                True(Near(middle, accent), $"Der mittlere Strahl fehlt: {middle}");

                // Daneben liegt die Fläche des Chips — dort darf keine Marke stehen.
                var besideX = middleX + 6;
                var beside = Shot.PixelAt(root, InBitmap(root, chip, besideX, middleY).X,
                    InBitmap(root, chip, besideX, middleY).Y);
                True(!Near(beside, accent), $"Neben dem Strahl steht auch Akzentfarbe: {beside}");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Einstellungen: Auftritt und Seitenwechsel sind weich, ohne Reste", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-pages-").FullName;
            try
            {
                var settings = AppSettings.Load(Path.Combine(folder, "settings.json"));
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var window = new SettingsWindow(settings, chat);

                // Ohne Fenster (der Weg, auf dem Prüfstand und Bilder rendern)
                // darf keine halb sichtbare Seite entstehen.
                window.SelectAreaForTest("ueber");
                Equal(1d, Math.Round(window.PageOpacityForTest("ueber"), 3), "Deckkraft ohne Fenster");
                Equal(0d, Math.Round(window.PageShiftForTest("ueber"), 3), "Versatz ohne Fenster");
                window.PlayEntrance();
                Equal(1d, Math.Round(window.RootOpacityForTest, 3), "Auftritt ohne Fenster");
                Equal(0d, Math.Round(window.RootShiftForTest, 3), "Auftrieb ohne Fenster");

                // Im echten Fenster läuft die Bewegung wirklich.
                Shot.Show(window);
                window.PlayEntrance();
                True(window.RootAnimatedForTest, "Der Auftritt hängt an keiner Bewegung");

                window.SelectAreaForTest("aussehen");
                True(window.IsAreaVisibleForTest("aussehen"), "Seite Aussehen ist nicht sichtbar");
                True(window.PageAnimatedForTest("aussehen"), "Der Seitenwechsel blendet nicht ein");

                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
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

        Check("Oberfläche: nach einer Frage verschwinden die Vorschläge, beim Tippen kommen sie zurück", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-ask-").FullName;
            try
            {
                var settings = new AppSettings { MaxSuggestions = 6 };
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                history.Add(new HistoryEntry("h-1", DateTimeOffset.Now, "Was ist Strom?",
                    "Bewegte Ladung", GroqModels.Small.Id, 1, 1, 0));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var bar = new MainWindow(chat);

                // Ein echtes (aber verstecktes) Fenster: nur dort läuft Bewegung.
                Shot.Show(bar);
                bar.ApplySettings();
                True(bar.SuggestionCount > 0, "keine Vorschläge aufgebaut");
                True(WaitFor(bar, () => bar.SuggestionsVisibleForTest), "die Vorschläge standen nicht im Bild");

                // Die Frage: das Feld wird leer, die Vorschläge klappen weg.
                bar.BeginAskForTest("Was ist Strom?");
                True(bar.AnsweringForTest, "die Frage wurde nicht begonnen");
                Equal("", bar.InputTextForTest, "das Eingabefeld wurde nicht geleert");
                True(WaitFor(bar, () => !bar.SuggestionsVisibleForTest),
                    "die Vorschläge stehen weiter im Bild, obwohl eine Frage läuft");
                Equal(0, bar.AnswerTextForTest.Length, "es steht schon eine Antwort da");

                // Wer tippt, sucht etwas — dann gehören sie zurück ins Bild.
                bar.SetInputForTest("Was");
                True(bar.SuggestionsVisibleForTest, "beim Tippen kamen die Vorschläge nicht zurück");

                // Feld wieder leer: es bleibt das Gespräch stehen, sonst nichts.
                bar.SetInputForTest("");
                True(WaitFor(bar, () => !bar.SuggestionsVisibleForTest),
                    "bei leerem Feld stehen die Vorschläge wieder da");

                bar.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Einführung: sieben Schritte, Blättern, Vorführung und Marke", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-tour-").FullName;
            try
            {
                var settings = AppSettings.Load(Path.Combine(folder, "settings.json"));
                var history = new HistoryStore(Path.Combine(folder, "history.jsonl"));
                var chat = new ChatService(new GroqClient(), settings, history, Path.Combine(folder, "conversation.json"));
                var welcome = new WelcomeWindow(settings, chat);

                Equal(7, welcome.StepKeysForTest.Count, "Schritte der Einführung");
                Equal("willkommen", welcome.VisibleStepForTest, "erster Schritt");
                True(!welcome.BackEnabledForTest, "„Zurück“ ist im ersten Schritt aktiv");
                Equal("Weiter", welcome.NextLabelForTest, "Beschriftung am Anfang");

                // Die Befehlsliste kommt aus der Quelle — jeder Befehl steht drin.
                foreach (var command in Commands.All)
                {
                    True(welcome.CommandNamesForTest.Any(name => name.Contains("/" + command.Name, StringComparison.Ordinal)),
                        $"Befehl fehlt in der Einführung: {command.Name}");
                }

                welcome.NextForTest();
                Equal("schluessel", welcome.VisibleStepForTest, "zweiter Schritt");
                True(welcome.IsStepVisibleForTest("schluessel"), "die Seite „Schlüssel“ ist nicht sichtbar");
                True(!welcome.IsStepVisibleForTest("willkommen"), "die alte Seite blieb sichtbar");
                True(welcome.BackEnabledForTest, "„Zurück“ ist im zweiten Schritt aus");

                welcome.GoToStepForTest("fertig");
                Equal("Fertig", welcome.NextLabelForTest, "Beschriftung am Ende");
                welcome.BackForTest();
                Equal("aussehen", welcome.VisibleStepForTest, "ein Schritt zurück");

                // Im Fenster: die Vorführung läuft und tippt wirklich.
                Shot.Show(welcome);
                welcome.GoToStepForTest("fragen");
                True(welcome.DemoRunningForTest, "die Vorführung läuft nicht");
                welcome.DemoStepForTest();
                True(welcome.DemoQuestionForTest.Length > 0, "die Vorführung tippt nicht");

                // Der Weiter-Knopf sitzt vollständig im Fenster — nichts abgeschnitten.
                var next = (Button)welcome.FindName("NextButton")!;
                var corner = next.TransformToAncestor(welcome).Transform(new Point(next.ActualWidth, next.ActualHeight));
                True(corner.X <= welcome.ActualWidth + 1 && corner.Y <= welcome.ActualHeight + 1,
                    $"Der „Weiter“-Knopf ragt aus dem Fenster: {corner}");

                // Die Marke steht auch hier: der Lichtpunkt trifft den Akzent.
                var root = (FrameworkElement)welcome.Content;
                var mark = welcome.MarkForTest;
                var accent = ((SolidColorBrush)Application.Current.Resources["Brush.Accent"]).Color;
                var point = InBitmap(root, mark, mark.ActualWidth / 2, mark.ActualHeight * 0.14);
                var light = Shot.PixelAt(root, point.X, point.Y);
                True(Near(light, accent), $"Der Lichtpunkt der Marke fehlt in der Einführung: {light}");

                var beside = InBitmap(root, mark, mark.ActualWidth * 0.18, mark.ActualHeight * 0.42);
                var empty = Shot.PixelAt(root, beside.X, beside.Y);
                True(!Near(empty, accent), $"Neben der Marke steht auch Akzentfarbe: {empty}");

                welcome.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });

        Check("Einführung: einmal gesehen bleibt gesehen", () =>
        {
            var folder = Directory.CreateTempSubdirectory("ailight-seen-").FullName;
            try
            {
                var file = Path.Combine(folder, "settings.json");
                var settings = AppSettings.Load(file);
                True(!settings.WelcomeSeen, "die Einführung käme ungefragt immer wieder");

                // Beim Schließen setzt das Programm diesen Vermerk — hier wird
                // geprüft, dass er wirklich in der Datei landet.
                settings.WelcomeSeen = true;
                True(AppSettings.Load(file).WelcomeSeen, "der Vermerk wurde nicht gespeichert");
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
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
