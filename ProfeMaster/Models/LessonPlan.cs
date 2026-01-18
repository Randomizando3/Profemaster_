namespace ProfeMaster.Models;

public sealed class LessonPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";
    public string Objectives { get; set; } = "";
    public string Content { get; set; } = "";
    public string Steps { get; set; } = "";
    public string Evaluation { get; set; } = "";

    // Vínculo opcional (pode ser geral ou por turma)
    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";

    // “Materiais” por enquanto como lista de texto (depois vira Storage)
    public List<string> Materials { get; set; } = new();

    public DateTime Date { get; set; } = DateTime.Today;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<LessonMaterial> MaterialsV2 { get; set; } = new();

    public string LinkedEventId { get; set; } = "";
    public string LinkedEventTitle { get; set; } = "";

    // Intervalo do plano
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;

    // Slots gerados (um por dia)
    public List<LessonSlot> Slots { get; set; } = new();

    // Simplificado: um campo só
    public string Observations { get; set; } = "";

    // Novas propriedades para thumbnail
    public string ThumbLocalPath { get; set; } = "";
    public string ThumbUrl { get; set; } = "";




}
