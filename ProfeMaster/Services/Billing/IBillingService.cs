// Services/Billing/IBillingService.cs
using ProfeMaster.Config;

namespace ProfeMaster.Services.Billing;

public interface IBillingService
{
    bool IsSupported { get; }

    Task InitializeAsync(CancellationToken ct = default);

    // Preços amigáveis por plano
    Task<(string monthly, string yearly)> GetPriceLabelsAsync(PlanTier tier, CancellationToken ct = default);

    // Compra mensal/anual por plano
    Task<(bool ok, string err)> PurchaseMonthlyAsync(PlanTier tier, CancellationToken ct = default);
    Task<(bool ok, string err)> PurchaseYearlyAsync(PlanTier tier, CancellationToken ct = default);

    // Best-effort
    Task<bool> HasActivePlanAsync(PlanTier tier, CancellationToken ct = default);
}
