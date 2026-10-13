using System.IO;
using System.Text.Json;
using AIlight.Core;

namespace AIlight.Groq;

/// <summary>Eine Runde im Gespräch (Frage und Antwort).</summary>
public sealed record Round(string Question, string Answer);

/// <summary>
/// Verbindet Einstellungen, Gedächtnis, Verlauf und den HTTP-Client.
/// Die Oberfläche ruft nur noch <see cref="AskAsync"/> und zeigt an, was ankommt.
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

    public ChatService(GroqClient client, AppSettings settings, HistoryStore history, string? conversationPath = null)
    {
        Client = client;
        Settings = settings;
        History = history;
        _conversationPath = conversationPath ?? Paths.ConversationFile;
        Load();
    }

    private readonly string _conversationPath;

    public GroqClient Client { get; }
    public AppSettings Settings { get; }
    public HistoryStore History { get; }

    /// <summary>Wird bei jeder neuen Antwort ausgelöst (für Tray-Hinweise usw.).</summary>
    public event Action<HistoryEntry>? Answered;

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

    public bool HasKey => SecretStore.HasKey();
    public string ApiKey => SecretStore.Read();

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
        var apiKey = ApiKey;
        if (apiKey.Length == 0)
        {
            throw new GroqException(
                "Es ist noch kein API-Schlüssel hinterlegt. Öffne die Einstellungen (Strg+,) "
                + "und trage dort deinen Groq-Schlüssel ein.", retryable: false);
        }

        var (model, reason) = GroqModels.Resolve(Settings.Model, question, Settings.WebSearch);
        var messages = BuildMessages(question, context);
        Log.Info($"Frage an {model.Id} ({reason}, {messages.Count} Nachrichten, Textlänge {question.Length})");

        var request = new AskRequest
        {
            Model = model.Id,
            Messages = messages,
            MaxCompletionTokens = Settings.MaxCompletionTokens,
            ReasoningEffort = Settings.Reasoning,
            WebSearch = Settings.WebSearch,
        };

        var result = await Client
            .StreamAsync(apiKey, request, onText, onReasoning, token)
            .ConfigureAwait(false);

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
        History.Prune(Settings.KeepHistoryDays);
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
            File.WriteAllText(_conversationPath, JsonSerializer.Serialize(payload));
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
