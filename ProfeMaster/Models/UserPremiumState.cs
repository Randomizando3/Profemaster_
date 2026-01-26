// Models/UserPremiumState.cs
namespace ProfeMaster.Models;

public sealed class UserPremiumState
{
    public bool IsPremium { get; set; } = false;

    // Unix epoch seconds (UTC). 0 = sem premium
    public long IsPremiumUntil { get; set; } = 0;

    // Mantém compatível com seu FirebaseDbService atual
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
