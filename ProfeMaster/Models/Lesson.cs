namespace ProfeMaster.Models;

public sealed class Lesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    // Contexto (igual aos demais)
    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";

    // Thumb offline-first
    public string ThumbLocalPath { get; set; } = "";
    public string ThumbUrl { get; set; } = "";

    public int DurationMinutes { get; set; } = 50;

    // Materiais (igual ao Plano V2)
    public List<LessonMaterial> MaterialsV2 { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
