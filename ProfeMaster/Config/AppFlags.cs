// Config/AppFlags.cs
namespace ProfeMaster.Config;

public static class AppFlags
{
    // Se false: UI não mostra upload, só links (economia).
    public static bool EnableStorageUploads = true;

    // ===== Premium =====
    // Premium = sem anúncios (por enquanto)
    public static bool IsPremium = false;

    // Unix epoch seconds (UTC). 0 = sem premium
    public static long IsPremiumUntil = 0;

    public static bool HasPremiumActive()
    {
        if (!IsPremium) return false;
        if (IsPremiumUntil <= 0) return true; // permite "premium permanente (dev)"
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return now <= IsPremiumUntil;
    }

    public static void ApplyPremium(bool premium, long untilUnixSeconds)
    {
        IsPremium = premium;
        IsPremiumUntil = untilUnixSeconds;

        // opcional: se expirou, derruba automaticamente
        if (IsPremiumUntil > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() > IsPremiumUntil)
        {
            IsPremium = false;
            IsPremiumUntil = 0;
        }
    }
}
