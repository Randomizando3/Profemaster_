// Services/Billing/IBillingService.cs
namespace ProfeMaster.Services.Billing;

public interface IBillingService
{
    bool IsSupported { get; }

    Task InitializeAsync(CancellationToken ct = default);

    // Retorna preços amigáveis, se disponível
    Task<(string monthly, string yearly)> GetPriceLabelsAsync(CancellationToken ct = default);

    // Dispara a compra (monthly/yearly)
    Task<(bool ok, string err)> PurchaseMonthlyAsync(CancellationToken ct = default);
    Task<(bool ok, string err)> PurchaseYearlyAsync(CancellationToken ct = default);

    // Consulta se tem premium ativo (best-effort, sem servidor)
    Task<bool> HasActivePremiumAsync(CancellationToken ct = default);
}
