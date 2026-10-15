namespace AIlight.Ai;

/// <summary>Wie mit einem Anbieter gesprochen wird — drei Formen decken alle ab.</summary>
public enum ApiShape
{
    /// <summary>Der verbreitete Weg: <c>/chat/completions</c> mit SSE-Datenzeilen.</summary>
    OpenAi,

    /// <summary>Anthropic: <c>/v1/messages</c> mit benannten SSE-Ereignissen.</summary>
    Anthropic,

    /// <summary>Google: <c>:streamGenerateContent?alt=sse</c> mit Kandidaten-Teilen.</summary>
    Gemini,
}

/// <summary>Ein Modell mit Anzeigenamen und Preis (US-Dollar je 1 Mio. Tokens).</summary>
public sealed record ModelInfo(string Id, string Label, double InputPerMillion = 0, double OutputPerMillion = 0)
{
    /// <summary>Preis für einen Aufruf in US-Dollar (0, wenn kein Preis bekannt ist).</summary>
    public double Cost(int inputTokens, int outputTokens)
        => (inputTokens / 1_000_000.0 * InputPerMillion) + (outputTokens / 1_000_000.0 * OutputPerMillion);

    /// <summary>Ist für dieses Modell ein Preis hinterlegt?</summary>
    public bool Priced => InputPerMillion > 0 || OutputPerMillion > 0;
}

/// <summary>
/// Ein Anbieter: wo er zu erreichen ist, wie er angesprochen wird und was er kann.
///
/// Die Modellnamen sind Vorschläge, kein Gesetz: jeder Anbieter benennt seine
/// Modelle um, nimmt sie weg und fügt neue hinzu. Deshalb lässt sich in den
/// Einstellungen jeder Name auch von Hand eintippen — und die Liste hier ist
/// nur das, was beim Schreiben dieser Fassung richtig war.
/// </summary>
public sealed record ProviderInfo(
    string Id,
    string Name,
    ApiShape Shape,
    string BaseUrl,
    string KeyUrl,
    string KeyHint,
    ModelInfo Fast,
    ModelInfo Strong,
    IReadOnlyList<ModelInfo> Models,
    bool NeedsKey = true,
    string MaxTokensField = "max_completion_tokens",
    bool ReasoningEffort = true,
    bool WebSearch = false,
    bool BaseUrlRequired = false,
    string Note = "")
{
    /// <summary>Alle wählbaren Modelle — die beiden wichtigen zuerst.</summary>
    public IReadOnlyList<ModelInfo> Catalog
    {
        get
        {
            var seen = new List<ModelInfo> { Fast, Strong };
            foreach (var model in Models)
            {
                if (seen.All(known => known.Id != model.Id))
                {
                    seen.Add(model);
                }
            }

            return seen;
        }
    }

    /// <summary>Ein Modell zu einem Namen — unbekannte Namen bleiben gültig.</summary>
    public ModelInfo Model(string id)
        => Catalog.FirstOrDefault(model => model.Id == id)
           ?? new ModelInfo(string.IsNullOrWhiteSpace(id) ? Fast.Id : id, id);

    public string DefaultModel => Fast.Id;

    public string ModelsFilePath => BaseUrl.TrimEnd('/') + "/models";
}

/// <summary>
/// Der Katalog der Anbieter. Reihenfolge = Reihenfolge in den Einstellungen.
/// </summary>
public static class Providers
{
    /// <summary>„auto“ überlässt die Wahl der Frage (schnell oder stark).</summary>
    public const string Auto = "auto";

    public static ProviderInfo Groq { get; } = new(
        "groq", "Groq", ApiShape.OpenAi,
        "https://api.groq.com/openai/v1",
        "https://console.groq.com/keys",
        "Schlüssel von console.groq.com/keys — fängt mit gsk_ an.",
        new ModelInfo("openai/gpt-oss-20b", "gpt-oss-20b — sehr schnell, günstig", 0.075, 0.30),
        new ModelInfo("openai/gpt-oss-120b", "gpt-oss-120b — stärker für schwere Fragen", 0.15, 0.60),
        new[]
        {
            new ModelInfo("llama-3.3-70b-versatile", "llama-3.3-70b — ohne Denk-Schritte", 0.59, 0.79),
        },
        WebSearch: true,
        Note: "Kann als einziger die eingebaute Websuche (browser_search) und Denk-Schritte.");

    public static ProviderInfo OpenAi { get; } = new(
        "openai", "OpenAI", ApiShape.OpenAi,
        "https://api.openai.com/v1",
        "https://platform.openai.com/api-keys",
        "Schlüssel von platform.openai.com/api-keys — fängt mit sk- an.",
        new ModelInfo("gpt-5.4-mini", "gpt-5.4-mini — schnell und günstig"),
        new ModelInfo("gpt-5.4", "gpt-5.4 — das große Modell"),
        Array.Empty<ModelInfo>(),
        Note: "Ohne Preisangabe: die Preise ändern sich dort häufiger als die Modellnamen.");

    public static ProviderInfo Anthropic { get; } = new(
        "anthropic", "Anthropic (Claude)", ApiShape.Anthropic,
        "https://api.anthropic.com/v1",
        "https://console.anthropic.com/settings/keys",
        "Schlüssel von console.anthropic.com — fängt mit sk-ant- an.",
        new ModelInfo("claude-haiku-5-5", "Claude Haiku 5.5 — schnell, 1 Mio. Kontext", 0.10, 0.50),
        new ModelInfo("claude-sonnet-5-5", "Claude Sonnet 5.5 — ausgewogen, 1 Mio. Kontext", 2, 10),
        new[]
        {
            new ModelInfo("claude-opus-5-5", "Claude Opus 5.5 — das stärkste, 1 Mio. Kontext", 4, 20),
            new ModelInfo("claude-fable-5-1", "Claude Fable 5.1 — 1 Mio. Kontext", 10, 50),
        },
        MaxTokensField: "max_tokens",
        ReasoningEffort: false,
        Note: "Die System-Vorgabe steht bei Anthropic nicht als Nachricht, sondern im Feld „system“; "
              + "„max_tokens“ ist dort Pflicht.");

    public static ProviderInfo Gemini { get; } = new(
        "gemini", "Google Gemini", ApiShape.Gemini,
        "https://generativelanguage.googleapis.com/v1beta",
        "https://aistudio.google.com/app/apikey",
        "Schlüssel von aistudio.google.com — beginnt mit AIza.",
        new ModelInfo("gemini-3.8-flash", "Gemini 3.8 Flash — schnell, 1 Mio. Kontext", 0.75, 3.75),
        new ModelInfo("gemini-3.1-pro-preview", "Gemini 3.1 Pro — stark, 1 Mio. Kontext", 2, 12),
        new[]
        {
            new ModelInfo("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite — am günstigsten", 0.30, 2.50),
            new ModelInfo("gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite — 1 Mio. Kontext", 0.25, 1.50),
        },
        MaxTokensField: "maxOutputTokens",
        ReasoningEffort: false,
        Note: "Denk-Stufen heißen hier „thinkingLevel“ bzw. „thinkingBudget“ — je nach Modell.");

    public static ProviderInfo Mistral { get; } = new(
        "mistral", "Mistral", ApiShape.OpenAi,
        "https://api.mistral.ai/v1",
        "https://console.mistral.ai/api-keys",
        "Schlüssel von console.mistral.ai/api-keys.",
        new ModelInfo("mistral-small-4", "Mistral Small 4 — schnell, 256k Kontext"),
        new ModelInfo("mistral-medium-3.5", "Mistral Medium 3.5 — stärker, 256k Kontext"),
        new[] { new ModelInfo("mistral-large-4", "Mistral Large 4 — das größte") },
        MaxTokensField: "max_tokens",
        Note: "Die genauen Modellnamen ändern sich hier schnell — im Zweifel den Namen "
              + "aus der Mistral-Konsole eintippen.");

    public static ProviderInfo DeepSeek { get; } = new(
        "deepseek", "DeepSeek", ApiShape.OpenAi,
        "https://api.deepseek.com",
        "https://platform.deepseek.com/api_keys",
        "Schlüssel von platform.deepseek.com.",
        new ModelInfo("deepseek-v4-pro", "DeepSeek V4 Pro"),
        new ModelInfo("deepseek-chat", "deepseek-chat — der stabile Standard"),
        Array.Empty<ModelInfo>(),
        MaxTokensField: "max_tokens",
        Note: "Die offizielle Dokumentation war beim Schreiben nicht erreichbar — "
              + "Modellnamen bitte in der eigenen Konsole gegenprüfen.");

    public static ProviderInfo Xai { get; } = new(
        "xai", "xAI (Grok)", ApiShape.OpenAi,
        "https://api.x.ai/v1",
        "https://console.x.ai",
        "Schlüssel von console.x.ai — fängt mit xai- an.",
        new ModelInfo("grok-4.7", "Grok 4.7 — 500k Kontext"),
        new ModelInfo("grok-4.1-fast", "Grok 4.1 Fast — schnelle Variante"),
        Array.Empty<ModelInfo>());

    public static ProviderInfo OpenRouter { get; } = new(
        "openrouter", "OpenRouter", ApiShape.OpenAi,
        "https://openrouter.ai/api/v1",
        "https://openrouter.ai/keys",
        "Schlüssel von openrouter.ai/keys — fängt mit sk-or- an.",
        new ModelInfo("google/gemini-3.8-flash", "Gemini 3.8 Flash über OpenRouter"),
        new ModelInfo("anthropic/claude-sonnet-5-5", "Claude Sonnet 5.5 über OpenRouter"),
        new[]
        {
            new ModelInfo("openai/gpt-5.4-mini", "GPT-5.4 mini über OpenRouter"),
            new ModelInfo("openai/gpt-5.4", "GPT-5.4 über OpenRouter"),
            new ModelInfo("meta-llama/llama-4-70b", "Llama 4 70B über OpenRouter"),
        },
        Note: "Ein Zugang für viele Modelle: die Namen tragen hier immer den Anbieter "
              + "davor (anbieter/modell).");

    public static ProviderInfo Together { get; } = new(
        "together", "Together AI", ApiShape.OpenAi,
        "https://api.together.ai/v1",
        "https://api.together.ai/settings/api-keys",
        "Schlüssel von api.together.ai/settings/api-keys.",
        new ModelInfo("Qwen/Qwen3.5-9B", "Qwen 3.5 9B — schnell", 0.10, 0.30),
        new ModelInfo("moonshotai/Kimi-K3", "Kimi K3 — stark, 1 Mio. Kontext"),
        Array.Empty<ModelInfo>());

    public static ProviderInfo Perplexity { get; } = new(
        "perplexity", "Perplexity", ApiShape.OpenAi,
        "https://api.perplexity.ai/router/v1",
        "https://www.perplexity.ai/settings/api",
        "Schlüssel von perplexity.ai/settings/api — fängt mit pplx- an.",
        new ModelInfo("perplexity/glm-5.3-flash", "GLM 5.3 Flash über Perplexity"),
        new ModelInfo("perplexity/kimi-k3", "Kimi K3 über Perplexity"),
        Array.Empty<ModelInfo>(),
        Note: "Sucht von Haus aus im Netz; der Zugang war beim Schreiben noch Vorschau.");

    public static ProviderInfo Ollama { get; } = new(
        "ollama", "Ollama (lokal)", ApiShape.OpenAi,
        "http://localhost:11434/v1",
        "https://ollama.com/download",
        "Lokal läuft nichts ohne Schlüssel — das Feld darf leer bleiben.",
        new ModelInfo("gpt-oss:20b", "gpt-oss 20b (muss installiert sein)"),
        new ModelInfo("gemma4", "gemma4 (muss installiert sein)"),
        Array.Empty<ModelInfo>(),
        NeedsKey: false,
        MaxTokensField: "max_tokens",
        Note: "Läuft auf dem eigenen Rechner: „Modelle laden“ zeigt, was wirklich da ist.");

    public static ProviderInfo LmStudio { get; } = new(
        "lmstudio", "LM Studio (lokal)", ApiShape.OpenAi,
        "http://localhost:1234/v1",
        "https://lmstudio.ai",
        "Lokal läuft nichts ohne Schlüssel — das Feld darf leer bleiben.",
        new ModelInfo("local-model", "Der in LM Studio geladene Server"),
        new ModelInfo("qwen2.5-7b-instruct", "qwen2.5-7b-instruct"),
        Array.Empty<ModelInfo>(),
        NeedsKey: false,
        MaxTokensField: "max_tokens",
        Note: "Erst im Programm einen Server starten, dann hier fragen.");

    public static ProviderInfo Azure { get; } = new(
        "azure", "Azure OpenAI", ApiShape.OpenAi,
        "https://IHRE-RESSOURCE.openai.azure.com/openai/v1",
        "https://portal.azure.com",
        "Schlüssel aus dem Azure-Portal (Schlüssel und Endpunkt).",
        new ModelInfo("gpt-5.4-mini", "Name Ihrer Bereitstellung (deployment)"),
        new ModelInfo("gpt-5.4", "Name Ihrer Bereitstellung (deployment)"),
        Array.Empty<ModelInfo>(),
        BaseUrlRequired: true,
        Note: "Hier zählt nicht der Modellname, sondern der Name Ihrer Bereitstellung; "
              + "die Adresse muss auf Ihre Ressource zeigen.");

    public static IReadOnlyList<ProviderInfo> All { get; } = new[]
    {
        Groq, OpenAi, Anthropic, Gemini, Mistral, DeepSeek, Xai, OpenRouter, Together, Perplexity,
        Ollama, LmStudio, Azure,
    };

    /// <summary>Ein Anbieter zur Kennung — Unbekanntes fällt auf Groq zurück.</summary>
    public static ProviderInfo Find(string? id)
        => All.FirstOrDefault(provider => provider.Id == id) ?? Groq;

    public static bool Known(string? id) => id is not null && All.Any(provider => provider.Id == id);

    /// <summary>
    /// Welches Modell wird wirklich gesendet? „auto“ entscheidet nach der Frage,
    /// eine feste Wahl bleibt unangetastet. Der Grund wird zurückgegeben, damit
    /// die Leiste ihn anzeigen kann.
    /// </summary>
    public static (ModelInfo Model, string Reason) Resolve(
        ProviderInfo provider, string setting, string question, bool webSearch)
    {
        var chosen = (setting ?? "").Trim();
        if (chosen.Length > 0 && chosen != Auto)
        {
            return (provider.Model(chosen), "in den Einstellungen gewählt");
        }

        if (webSearch)
        {
            return (provider.Strong, "Websuche braucht das stärkere Modell");
        }

        return question.Length > 240 || question.Contains("```")
            ? (provider.Strong, "längere Frage")
            : (provider.Fast, "kurze Frage (schnell und günstig)");
    }

    /// <summary>Anzeigetext für die Fußzeile, z. B. „~$0,0003“.</summary>
    public static string FormatCost(double usd)
    {
        if (usd <= 0)
        {
            return "~$0";
        }

        if (usd < 0.0001)
        {
            return "unter $0,0001";
        }

        return "~$" + usd.ToString("0.0000", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    }

    /// <summary>Grober Token-Schätzer, wenn die API keine Nutzung mitschickt (4 Zeichen ≈ 1 Token).</summary>
    public static int EstimateTokens(int characters) => Math.Max(1, characters / 4);

    /// <summary>
    /// Die Namen aus einer <c>/models</c>-Antwort herausziehen — für lokale Server,
    /// die ihre Modelle selbst auflisten. Nimmt beide gebräuchlichen Formen
    /// (<c>data[].id</c> und <c>models[].name</c>).
    /// </summary>
    public static IReadOnlyList<string> ReadModelNames(string json)
    {
        var names = new List<string>();
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            foreach (var key in new[] { "data", "models" })
            {
                if (!document.RootElement.TryGetProperty(key, out var list)
                    || list.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in list.EnumerateArray())
                {
                    foreach (var field in new[] { "id", "name" })
                    {
                        if (item.TryGetProperty(field, out var value)
                            && value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var name = (value.GetString() ?? "").Trim();
                            // Ollama liefert bei /api/tags „name:tag“ — alles brauchbar.
                            if (name.Length > 0 && !names.Contains(name))
                            {
                                names.Add(name);
                            }
                        }
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // keine Liste — dann eben leer
        }

        return names;
    }
}
