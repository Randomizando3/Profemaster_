namespace ProfeMaster.Models;

public sealed class UserSession
{
    public string Uid { get; set; } = "";
    public string Email { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
