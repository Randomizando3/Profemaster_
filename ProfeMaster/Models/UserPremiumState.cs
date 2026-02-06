// Models/UserPremiumState.cs
using ProfeMaster.Config;

namespace ProfeMaster.Models;

public sealed class UserPremiumState
{
    // ===== NOVO =====
    // "free" | "premium" | "superpremium"
    public string Plan { get; set; } = "free";

    // Unix epoch seconds (UTC). 0 = sem expiração
    public long PlanUntil { get; set; } = 0;

    // ===== LEGADO (compat com seu Firebase atual) =====
    public bool IsPremium { get; set; } = false;
    public long IsPremiumUntil { get; set; } = 0;

    // Mantém compatível com seu FirebaseDbService atual
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public (PlanTier tier, long until) ToPlan()
    {
        // Preferir o novo
        if (!string.IsNullOrWhiteSpace(Plan))
        {
            var p = Plan.Trim().ToLowerInvariant();
            if (p == "superpremium") return (PlanTier.SuperPremium, PlanUntil);
            if (p == "premium") return (PlanTier.Premium, PlanUntil);
            return (PlanTier.Free, 0);
        }

        // Fallback do legado
        if (IsPremium) return (PlanTier.Premium, IsPremiumUntil);
        return (PlanTier.Free, 0);
    }

    public static UserPremiumState FromPlan(PlanTier tier, long untilUnix)
    {
        var planStr = tier switch
        {
            PlanTier.SuperPremium => "superpremium",
            PlanTier.Premium => "premium",
            _ => "free"
        };

        return new UserPremiumState
        {
            Plan = planStr,
            PlanUntil = tier == PlanTier.Free ? 0 : untilUnix,

            // legado preenchido também (evita quebrar versões antigas do app)
            IsPremium = tier >= PlanTier.Premium,
            IsPremiumUntil = tier >= PlanTier.Premium ? untilUnix : 0,

            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
