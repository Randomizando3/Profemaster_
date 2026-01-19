using System.Text.Json.Serialization;

namespace ProfeMaster.Models;

public sealed class QuizQuestion
{
    public int Number { get; set; }

    public string Prompt { get; set; } = "";
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string C { get; set; } = "";
    public string D { get; set; } = "";

    // "A", "B", "C", "D"
    public string Answer { get; set; } = "A";

    [JsonIgnore]
    public string AnswerText => Answer switch
    {
        "A" => A,
        "B" => B,
        "C" => C,
        "D" => D,
        _ => ""
    };
}

public sealed class QuizDocument
{
    public string Theme { get; set; } = "";
    public string Difficulty { get; set; } = "Ensino Médio";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<QuizQuestion> Questions { get; set; } = new();
}
