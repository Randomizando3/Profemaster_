using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Networking;
using ProfeMaster.Config;
using ProfeMaster.Models;

namespace ProfeMaster.Services;

public sealed class GroqQuizService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GroqQuizService(HttpClient http)
    {
        _http = http;
    }

    public Task<bool> HasInternetAsync(CancellationToken ct = default)
        => Task.FromResult(Connectivity.Current.NetworkAccess == NetworkAccess.Internet);

    public async Task<QuizQuestion?> GenerateOneAsync(
        string theme,
        string baseText,
        string difficulty,
        List<string> avoid,
        CancellationToken ct = default)
    {
        var apiKey = LocalSecrets.GroqApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = JsonContent.Create(new ChatRequest
        {
            Model = LocalSecrets.GroqModel,
            Temperature = 0.4,
            MaxTokens = 700,
            Messages =
            [
                new ChatMessage
                {
                    Role = "system",
                    Content = "Você cria questões objetivas em português do Brasil. Responda somente JSON válido."
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = BuildPrompt(theme, baseText, difficulty, avoid)
                }
            ]
        }, options: JsonOptions);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        var data = await resp.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
        var text = data?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        return ParseQuestion(text);
    }

    private static string BuildPrompt(string theme, string baseText, string difficulty, List<string> avoid)
    {
        var avoidText = avoid.Count == 0
            ? "Nenhuma."
            : string.Join("\n", avoid.Select(x => "- " + x));

        return $$"""
Crie 1 questão objetiva de múltipla escolha.

Tema: {{theme}}
Nível: {{difficulty}}
Texto base opcional: {{baseText}}

Evite repetir estas ideias:
{{avoidText}}

Retorne somente este JSON, sem markdown:
{
  "prompt": "enunciado da questão",
  "a": "alternativa A",
  "b": "alternativa B",
  "c": "alternativa C",
  "d": "alternativa D",
  "answer": "A"
}
""";
    }

    private static QuizQuestion? ParseQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = ExtractJson(text);

        try
        {
            var dto = JsonSerializer.Deserialize<QuestionDto>(text, JsonOptions);
            if (dto == null) return null;

            return new QuizQuestion
            {
                Prompt = dto.Prompt?.Trim() ?? "",
                A = dto.A?.Trim() ?? "",
                B = dto.B?.Trim() ?? "",
                C = dto.C?.Trim() ?? "",
                D = dto.D?.Trim() ?? "",
                Answer = NormalizeAnswer(dto.Answer)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        return text;
    }

    private static string NormalizeAnswer(string? answer)
    {
        var a = (answer ?? "").Trim().ToUpperInvariant();
        return a is "A" or "B" or "C" or "D" ? a : "A";
    }

    private sealed class QuestionDto
    {
        public string? Prompt { get; set; }
        public string? A { get; set; }
        public string? B { get; set; }
        public string? C { get; set; }
        public string? D { get; set; }
        public string? Answer { get; set; }
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; } = [];
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
