// Models/ExamItem.cs
namespace ProfeMaster.Models;

public sealed class ExamItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ThumbLocalPath { get; set; } = "";
    public string ThumbUrl { get; set; } = ""; // futuro Storage
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ===== NOVO: JSON do quiz gerado/editar =====
    // Guarda o QuizDocument serializado (Theme, Difficulty, GeneratedAt, Questions...)
    public string QuizJson { get; set; } = "";
}
