namespace ProfeMaster.Models;

public sealed class ScheduleEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    // vínculos
    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";

    // datas (ISO)
    public DateTime Start { get; set; } = DateTime.Now;
    public DateTime End { get; set; } = DateTime.Now.AddHours(1);

    public string Type { get; set; } = "Aula"; // Aula, Prova, Evento etc.

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
