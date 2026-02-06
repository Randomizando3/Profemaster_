// Services/Billing/NoBillingService.cs
using ProfeMaster.Config;

namespace ProfeMaster.Services.Billing;

public sealed class NoBillingService : IBillingService
{
    public bool IsSupported => false;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<(string monthly, string yearly)> GetPriceLabelsAsync(PlanTier tier, CancellationToken ct = default)
        => Task.FromResult(("", ""));

    public Task<(bool ok, string err)> PurchaseMonthlyAsync(PlanTier tier, CancellationToken ct = default)
        => Task.FromResult((false, "Billing não suportado nesta plataforma."));

    public Task<(bool ok, string err)> PurchaseYearlyAsync(PlanTier tier, CancellationToken ct = default)
        => Task.FromResult((false, "Billing não suportado nesta plataforma."));

    public Task<bool> HasActivePlanAsync(PlanTier tier, CancellationToken ct = default)
        => Task.FromResult(false);
}
