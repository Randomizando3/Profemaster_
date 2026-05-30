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

    public string LastErrorMessage { get; private set; } = "";

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
        var questions = await GenerateAsync(theme, baseText, difficulty, 1, ct, avoid);
        return questions.FirstOrDefault();
    }

    public async Task<List<QuizQuestion>> GenerateAsync(
        string theme,
        string baseText,
        string difficulty,
        int count,
        CancellationToken ct = default,
        List<string>? avoid = null)
    {
        var apiKey = LocalSecrets.GroqApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LastErrorMessage = "Chave da IA nao configurada neste aparelho.";
            return [];
        }

        LastErrorMessage = "";
        count = Math.Clamp(count, 1, 10);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = JsonContent.Create(new ChatRequest
        {
            Model = LocalSecrets.GroqModel,
            Temperature = 0.4,
            MaxTokens = Math.Clamp(count * 420, 700, 3500),
            Messages =
            [
                new ChatMessage
                {
                    Role = "system",
                    Content = "Voce cria questoes objetivas em portugues do Brasil. Responda somente JSON valido."
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = BuildPrompt(theme, baseText, difficulty, count, avoid ?? [])
                }
            ]
        }, options: JsonOptions);

        try
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                LastErrorMessage = BuildHttpError(resp, body);
                return [];
            }

            var data = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions);
            var text = data?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            var questions = ParseQuestions(text)
                .Take(count)
                .ToList();

            if (questions.Count == 0)
                LastErrorMessage = "A IA respondeu, mas nao retornou perguntas validas. Tente gerar novamente.";

            return questions;
        }
        catch (OperationCanceledException)
        {
            LastErrorMessage = "Tempo de conexao com a IA esgotado. Tente novamente em instantes.";
            return [];
        }
        catch (HttpRequestException)
        {
            LastErrorMessage = "Falha de conexao com a IA. Verifique a internet e tente novamente.";
            return [];
        }
        catch (JsonException)
        {
            LastErrorMessage = "A IA retornou uma resposta invalida. Tente gerar novamente.";
            return [];
        }
        catch
        {
            LastErrorMessage = "Nao foi possivel gerar o quiz agora. Tente novamente em instantes.";
            return [];
        }
    }

    private static string BuildPrompt(string theme, string baseText, string difficulty, int count, List<string> avoid)
    {
        var avoidText = avoid.Count == 0
            ? "Nenhuma."
            : string.Join("\n", avoid.Select(x => "- " + x));

        return $$"""
Crie {{count}} questao(oes) objetiva(s) de multipla escolha.

Tema: {{theme}}
Nivel: {{difficulty}}
Texto base opcional: {{baseText}}

Evite repetir estas ideias:
{{avoidText}}

Retorne somente este JSON, sem markdown:
{
  "questions": [
    {
      "prompt": "enunciado da questao",
      "a": "alternativa A",
      "b": "alternativa B",
      "c": "alternativa C",
      "d": "alternativa D",
      "answer": "A"
    }
  ]
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

            var question = ToQuestion(dto);
            return IsUsable(question) ? question : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<QuizQuestion> ParseQuestions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        text = ExtractJson(text);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("questions", out var questionsElement) &&
            questionsElement.ValueKind == JsonValueKind.Array)
        {
            return ParseQuestionArray(questionsElement);
        }

        if (root.ValueKind == JsonValueKind.Array)
            return ParseQuestionArray(root);

        var single = ParseQuestion(text);
        return single == null ? [] : [single];
    }

    private static List<QuizQuestion> ParseQuestionArray(JsonElement questionsElement)
    {
        var questions = new List<QuizQuestion>();

        foreach (var item in questionsElement.EnumerateArray())
        {
            try
            {
                var dto = item.Deserialize<QuestionDto>(JsonOptions);
                if (dto == null) continue;

                var question = ToQuestion(dto);
                if (IsUsable(question))
                    questions.Add(question);
            }
            catch
            {
                // Keep valid questions even if one item comes malformed.
            }
        }

        return questions;
    }

    private static QuizQuestion ToQuestion(QuestionDto dto)
        => new()
        {
            Prompt = dto.Prompt?.Trim() ?? "",
            A = dto.A?.Trim() ?? "",
            B = dto.B?.Trim() ?? "",
            C = dto.C?.Trim() ?? "",
            D = dto.D?.Trim() ?? "",
            Answer = NormalizeAnswer(dto.Answer)
        };

    private static bool IsUsable(QuizQuestion question)
        => !string.IsNullOrWhiteSpace(question.Prompt)
           && !string.IsNullOrWhiteSpace(question.A)
           && !string.IsNullOrWhiteSpace(question.B)
           && !string.IsNullOrWhiteSpace(question.C)
           && !string.IsNullOrWhiteSpace(question.D);

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

    private static string BuildHttpError(HttpResponseMessage resp, string body)
    {
        var details = TryReadApiError(body);

        return (int)resp.StatusCode switch
        {
            401 or 403 => "A chave da IA foi recusada. Confira a chave do Groq neste aparelho.",
            429 => "Limite da IA atingido. Aguarde um pouco e tente novamente.",
            >= 500 => "A IA ficou indisponivel por instantes. Tente novamente.",
            _ when !string.IsNullOrWhiteSpace(details) => details,
            _ => $"A IA recusou a solicitacao ({(int)resp.StatusCode}). Tente novamente."
        };
    }

    private static string TryReadApiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "";
            }
        }
        catch
        {
        }

        return "";
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

        [JsonPropertyName("response_format")]
        public ResponseFormat ResponseFormat { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
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
