namespace ProfeMaster.Models;

public sealed class Institution
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Escola"; // Pública, Privada, Particular etc.
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
