using System.IO;
using System.Text.Json;
using AIlight.Ai;
using AIlight.Core;

namespace AIlight.Chat;

/// <summary>Eine Runde im Gespräch (Frage und Antwort).</summary>
public sealed record Round(string Question, string Answer);

/// <summary>
/// Verbindet Einstellungen, Gedächtnis, Verlauf und den Klienten des gewählten
/// Anbieters. Die Oberfläche ruft nur noch <see cref="AskAsync"/> und zeigt an,
/// was ankommt — welcher Anbieter dahinter steckt, entscheidet die Einstellung.
/// </summary>
public sealed class ChatService
{
    private const string DefaultSystemPrompt =
        "Du bist AIlight, ein knapper, freundlicher Assistent auf einem Windows-Rechner. "
        + "Antworte auf Deutsch, sofern die Frage nicht ausdrücklich eine andere Sprache verlangt. "
        + "Antworte direkt und ohne Einleitungsfloskeln. Nutze Markdown sparsam und nur, wo es hilft "
        + "(kurze Listen, Code in Codeblöcken). Wenn du etwas nicht weißt, sage es klar.";

    private readonly List<Round> _rounds = new();
    private readonly object _gate = new();
    private readonly string _conversationPath;

    public ChatService(AppSettings settings, HistoryStore history, string? conversationPath = null)
    {
        Settings = settings;
        History = history;
        _conversationPath = conversationPath ?? Paths.ConversationFile;
        Load();
    }

    public AppSettings Settings { get; }
    public HistoryStore History { get; }

    /// <summary>Wird bei jeder neuen Antwort ausgelöst (für Tray-Hinweise usw.).</summary>
    public event Action<HistoryEntry>? Answered;

    /// <summary>Der Anbieter, mit dem gerade gesprochen wird.</summary>
    public ProviderInfo Provider => Providers.Find(Settings.Provider);

    /// <summary>Die Adresse, die wirklich benutzt wird (eigene Angabe schlägt den Katalog).</summary>
    public string BaseUrl
    {
        get
        {
            var own = (Settings.BaseUrl ?? "").Trim();
            return own.Length > 0 ? own.TrimEnd('/') : Provider.BaseUrl;
        }
    }

    public string ApiKey => SecretStore.Read(Provider.Id);

    /// <summary>Braucht dieser Anbieter überhaupt einen Schlüssel? (Lokale Server nicht.)</summary>
    public bool NeedsKey => Provider.NeedsKey;

    /// <summary>Ist alles da, was eine Frage braucht?</summary>
    public bool HasKey => !Provider.NeedsKey || ApiKey.Length > 0;

    /// <summary>Ein lesbarer Satz, wenn etwas fehlt — sonst leer.</summary>
    public string KeyProblem
    {
        get
        {
            if (!Provider.NeedsKey)
            {
                return "";
            }

            if (Provider.BaseUrlRequired && (Settings.BaseUrl ?? "").Trim().Length == 0)
            {
                return $"{Provider.Name} braucht eine eigene Adresse — bitte in den Einstellungen "
                       + "unter „Konto“ eintragen.";
            }

            return ApiKey.Length > 0
                ? ""
                : $"Für {Provider.Name} ist noch kein API-Schlüssel hinterlegt. Öffne die Einstellungen "
                  + "(Strg+,), Bereich „Konto“, und trage ihn dort ein.";
        }
    }

    public IReadOnlyList<Round> Rounds
    {
        get
        {
            lock (_gate)
            {
                return _rounds.ToList();
            }
        }
    }

    /// <summary>System-Nachricht, die wirklich gesendet wird.</summary>
    public string SystemPrompt(string context = "")
    {
        var text = Settings.HasCustomSystemPrompt ? Settings.SystemPrompt.Trim() : DefaultSystemPrompt;
        if (!string.IsNullOrWhiteSpace(context))
        {
            text += "\n\n" + context;
        }

        return text;
    }

    /// <summary>Ein neues Gespräch beginnen (das Gedächtnis wird geleert).</summary>
    public void NewChat()
    {
        lock (_gate)
        {
            _rounds.Clear();
        }

        Save();
    }

    public async Task<AskResult> AskAsync(
        string question,
        string context,
        Action<string> onText,
        Action<string>? onReasoning,
        CancellationToken token)
    {
        var provider = Provider;
        var problem = KeyProblem;
        if (problem.Length > 0)
        {
            throw new AiException(problem, retryable: false);
        }

        var setting = Settings.ModelFor(provider.Id);
        var (model, reason) = Providers.Resolve(provider, setting, question, Settings.WebSearch);
        var messages = BuildMessages(question, context);
        Log.Info($"Frage an {provider.Name}/{model.Id} ({reason}, {messages.Count} Nachrichten, "
            + $"Textlänge {question.Length})");

        var request = new AskRequest
        {
            Model = model.Id,
            Messages = messages,
            MaxCompletionTokens = Settings.EffectiveMaxTokens,
            ReasoningEffort = Settings.Reasoning,
            WebSearch = Settings.WebSearch && provider.WebSearch,
            Temperature = Settings.Temperature,
            TopP = Settings.TopP,
            Thinking = Settings.ExtendedThinking,
            Stream = Settings.Streaming,
        };

        var call = new AiCall(
            provider,
            BaseUrl,
            ApiKey,
            request,
            onText,
            onReasoning,
            Settings.Retries,
            TimeSpan.FromSeconds(Settings.TimeoutSeconds));

        var result = await AiClients.For(provider).StreamAsync(call, token).ConfigureAwait(false);

        if (result.Cancelled || result.Text.Trim().Length == 0)
        {
            return result;
        }

        lock (_gate)
        {
            _rounds.Add(new Round(question, result.Text));
        }

        Save();
        var entry = new HistoryEntry(
            HistoryStore.NewId(),
            DateTimeOffset.Now,
            question,
            result.Text,
            result.Model,
            result.InputTokens,
            result.OutputTokens,
            result.CostUsd);
        History.Add(entry);
        History.Prune(Settings.KeepHistoryDays, Settings.HistoryEntries);
        SaveLastAnswer(entry);
        Answered?.Invoke(entry);
        return result;
    }

    /// <summary>Die abgeschnittene Antwort fortsetzen.</summary>
    public Task<AskResult> ContinueAsync(
        string question,
        Action<string> onText,
        Action<string>? onReasoning,
        CancellationToken token)
        => AskAsync(
            question.Length > 0 ? question : "Bitte fahre mit der vorigen Antwort fort.",
            context: "",
            onText,
            onReasoning,
            token);

    /// <summary>Schlüssel prüfen — derselbe Weg wie eine Frage, nur winzig.</summary>
    public Task<string> TestKeyAsync(string apiKey, CancellationToken token)
    {
        var provider = Provider;
        var call = new AiCall(
            provider,
            BaseUrl,
            apiKey,
            new AskRequest
            {
                Model = provider.Fast.Id,
                Messages = Array.Empty<ChatMessage>(),
                Stream = false,
            },
            Attempts: 1);

        return AiClients.For(provider).TestKeyAsync(call, token);
    }

    /// <summary>Die Modelle abfragen, die der Anbieter (oder ein lokaler Server) anbietet.</summary>
    public Task<IReadOnlyList<string>> AvailableModelsAsync(CancellationToken token)
    {
        var provider = Provider;
        var call = new AiCall(provider, BaseUrl, ApiKey, new AskRequest
        {
            Model = provider.Fast.Id,
            Messages = Array.Empty<ChatMessage>(),
        });

        return AiClients.For(provider).ModelsAsync(call, token);
    }

    private List<ChatMessage> BuildMessages(string question, string context)
    {
        var messages = new List<ChatMessage> { new("system", SystemPrompt(context)) };
        lock (_gate)
        {
            var rounds = Settings.MemoryRounds <= 0
                ? new List<Round>()
                : _rounds.TakeLast(Settings.MemoryRounds).ToList();
            foreach (var round in rounds)
            {
                messages.Add(new ChatMessage("user", round.Question));
                messages.Add(new ChatMessage("assistant", round.Answer));
            }
        }

        messages.Add(new ChatMessage("user", question));
        return messages;
    }

    private void SaveLastAnswer(HistoryEntry entry)
    {
        try
        {
            File.WriteAllText(Paths.LastAnswerFile, entry.Answer);
        }
        catch (Exception ex)
        {
            Log.Warn($"Letzte Antwort konnte nicht gespeichert werden: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var payload = new ConversationFile(_rounds.ToList());
            AtomicFile.WriteAllText(_conversationPath, JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            Log.Warn($"Gespräch konnte nicht gespeichert werden: {ex.Message}");
        }
    }

    private void Load()
    {
        if (!File.Exists(_conversationPath))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<ConversationFile>(File.ReadAllText(_conversationPath));
            if (loaded?.Rounds is { Count: > 0 } rounds)
            {
                _rounds.AddRange(rounds);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Gespräch konnte nicht gelesen werden: {ex.Message}");
        }
    }

    private sealed record ConversationFile(List<Round> Rounds);
}
