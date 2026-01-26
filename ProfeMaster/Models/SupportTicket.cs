namespace ProfeMaster.Models;

public sealed class SupportTicket
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Uid { get; set; } = "";

    public string Name { get; set; } = "";
    public string Message { get; set; } = "";

    public string Status { get; set; } = "open"; // open/closed
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
