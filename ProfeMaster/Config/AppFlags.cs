// Config/AppFlags.cs
namespace ProfeMaster.Config;

public static class AppFlags
{
    // Se false: UI não mostra upload, só links (economia).
    public static bool EnableStorageUploads = true;

    // ===== NOVO: Plano atual =====
    public static PlanTier CurrentPlan { get; private set; } = PlanTier.Free;

    // Unix epoch seconds (UTC). 0 = sem expiração (dev/permanente)
    public static long PlanUntil { get; private set; } = 0;

    // ===== COMPAT: continua existindo para seu código atual =====
    public static bool IsPremium
    {
        get => CurrentPlan >= PlanTier.Premium;
        private set { /* compat: ignorado (use ApplyPlan) */ }
    }

    public static long IsPremiumUntil
    {
        get => PlanUntil;
        private set { /* compat: ignorado (use ApplyPlan) */ }
    }

    // ===== DEV OVERRIDES (3 UIDs de teste) =====
    // Coloque aqui seus 3 UIDs e o plano que quer forçar.
    private static readonly Dictionary<string, PlanTier> DevUidOverrides = new()
    {
        // ["UID_1"] = PlanTier.Premium,
        // ["UID_2"] = PlanTier.SuperPremium,
        // ["UID_3"] = PlanTier.Premium,
    };

    public static bool TryApplyDevOverride(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return false;

        if (DevUidOverrides.TryGetValue(uid.Trim(), out var tier))
        {
            // override dev = sem expiração
            ApplyPlan(tier, untilUnixSeconds: 0);
            return true;
        }

        return false;
    }

    public static bool HasPlan(PlanTier required)
        => HasPlanActive() && CurrentPlan >= required;

    public static bool HasPlanActive()
    {
        if (CurrentPlan == PlanTier.Free) return true; // Free sempre “ativo”

        if (PlanUntil <= 0) return true; // pago “permanente/dev”

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return now <= PlanUntil;
    }

    public static void ApplyPlan(PlanTier tier, long untilUnixSeconds)
    {
        CurrentPlan = tier;
        PlanUntil = untilUnixSeconds;

        // se expirou, derruba pra Free
        if (tier != PlanTier.Free && PlanUntil > 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now > PlanUntil)
            {
                CurrentPlan = PlanTier.Free;
                PlanUntil = 0;
            }
        }
    }

    // ===== helpers prontos (fica gostoso de usar no app) =====
    public static bool IsFree() => CurrentPlan == PlanTier.Free;
    public static bool IsPremiumPlan() => CurrentPlan == PlanTier.Premium && HasPlanActive();
    public static bool IsSuperPremiumPlan() => CurrentPlan == PlanTier.SuperPremium && HasPlanActive();
}
