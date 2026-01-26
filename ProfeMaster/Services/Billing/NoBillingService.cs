// Services/Billing/NoBillingService.cs
namespace ProfeMaster.Services.Billing;

public sealed class NoBillingService : IBillingService
{
    public bool IsSupported => false;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<(string monthly, string yearly)> GetPriceLabelsAsync(CancellationToken ct = default)
        => Task.FromResult(("", ""));

    public Task<(bool ok, string err)> PurchaseMonthlyAsync(CancellationToken ct = default)
        => Task.FromResult((false, "Billing não suportado nesta plataforma."));

    public Task<(bool ok, string err)> PurchaseYearlyAsync(CancellationToken ct = default)
        => Task.FromResult((false, "Billing não suportado nesta plataforma."));

    public Task<bool> HasActivePremiumAsync(CancellationToken ct = default)
        => Task.FromResult(false);
}
