namespace ProfeMaster.Models;

public sealed class StudentContact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string InstitutionId { get; set; } = "";
    public string ClassroomId { get; set; } = "";

    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";       // WhatsApp / telefone
    public string Email { get; set; } = "";
    public string Notes { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
