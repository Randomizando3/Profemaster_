namespace ProfeMaster.Models;

public sealed class Classroom
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string InstitutionId { get; set; } = "";
    public string Name { get; set; } = "";          // Ex: 8º Ano A
    public string Period { get; set; } = "";        // Ex: Manhã
    public string Room { get; set; } = "";          // Ex: Sala 12
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
