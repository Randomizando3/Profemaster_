#if ANDROID
using Android.BillingClient.Api;
using Microsoft.Maui.ApplicationModel;
using ProfeMaster.Config;

using AndroidApp = Android.App.Application;

namespace ProfeMaster.Services.Billing;

public sealed class GooglePlayBillingService : Java.Lang.Object, IBillingService, IPurchasesUpdatedListener
{
    // ===== SKUs (Play Console) =====
    private const string PremiumMonthly = "premium_monthly";
    private const string PremiumYearly  = "premium_yearly";

    private const string SuperMonthly   = "superpremium_monthly";
    private const string SuperYearly    = "superpremium_yearly";

    private BillingClient? _client;

    // Cache por SKU (mensal/anual de cada plano)
    private readonly Dictionary<string, SkuDetails> _skuCache = new(StringComparer.OrdinalIgnoreCase);

    private TaskCompletionSource<(bool ok, string err)>? _purchaseTcs;

    public bool IsSupported => true;

    // 0 = OK (BillingResponseCode.OK). Usamos int para evitar incompatibilidades.
    private static bool IsOk(BillingResult r) => r != null && r.ResponseCode == 0;

    private static bool IsPurchased(Purchase p) => p != null && p.PurchaseState == PurchaseState.Purchased;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_client != null && _client.IsReady) return;

        var tcs = new TaskCompletionSource<bool>();

        _client = BillingClient.NewBuilder(AndroidApp.Context)
            .EnablePendingPurchases()
            .SetListener(this)
            .Build();

        _client.StartConnection(new BillingStateListener(
            onSetupFinished: result =>
            {
                if (IsOk(result)) tcs.TrySetResult(true);
                else tcs.TrySetException(new Exception($"Billing setup falhou: {result.ResponseCode} - {result.DebugMessage}"));
            },
            onDisconnected: () => { }
        ));

        await tcs.Task;
    }

    // ===== Interface =====

    public async Task<(string monthly, string yearly)> GetPriceLabelsAsync(PlanTier tier, CancellationToken ct = default)
    {
        // Best-effort: pode retornar "" se o binding não suportar query corretamente.
        await InitializeAsync(ct);

        var skuM = GetSkuId(tier, yearly: false);
        var skuY = GetSkuId(tier, yearly: true);

        var dM = await GetSkuDetailsSafeAsync(skuM);
        var dY = await GetSkuDetailsSafeAsync(skuY);

        // Alguns bindings têm Price, outros PriceAmountMicros/CurrencyCode etc.
        // Para não quebrar, tentamos Price via reflection.
        return (TryGetPriceString(dM), TryGetPriceString(dY));
    }

    public async Task<(bool ok, string err)> PurchaseMonthlyAsync(PlanTier tier, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var skuId = GetSkuId(tier, yearly: false);
        var sku = await GetSkuDetailsSafeAsync(skuId);

        if (sku == null)
            return (false, $"SKU mensal não encontrado no Google Play: {skuId}");

        return await LaunchPurchaseAsync(sku);
    }

    public async Task<(bool ok, string err)> PurchaseYearlyAsync(PlanTier tier, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var skuId = GetSkuId(tier, yearly: true);
        var sku = await GetSkuDetailsSafeAsync(skuId);

        if (sku == null)
            return (false, $"SKU anual não encontrado no Google Play: {skuId}");

        return await LaunchPurchaseAsync(sku);
    }

    // Por enquanto mantém “restore” falso (seu controle é por Firebase).
    // Depois dá pra implementar queryPurchases e mapear pro tier correto.
    public Task<bool> HasActivePlanAsync(PlanTier tier, CancellationToken ct = default)
        => Task.FromResult(false);

    // ===== PurchasesUpdatedListener =====

    public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
    {
        if (!IsOk(billingResult))
        {
            _purchaseTcs?.TrySetResult((false, $"{billingResult.ResponseCode}: {billingResult.DebugMessage}"));
            _purchaseTcs = null;
            return;
        }

        if (purchases == null || purchases.Count == 0)
        {
            _purchaseTcs?.TrySetResult((false, "Compra não retornou itens."));
            _purchaseTcs = null;
            return;
        }

        _ = HandlePurchasesAsync(purchases);
    }

    private async Task HandlePurchasesAsync(IList<Purchase> purchases)
    {
        try
        {
            foreach (var p in purchases)
            {
                if (!IsPurchased(p)) continue;

                // Acknowledge é obrigatório para compra ser concluída (evita reembolso automático).
                if (!p.IsAcknowledged)
                {
                    var ackParams = AcknowledgePurchaseParams.NewBuilder()
                        .SetPurchaseToken(p.PurchaseToken)
                        .Build();

                    var tcsAck = new TaskCompletionSource<bool>();

                    _client!.AcknowledgePurchase(ackParams, new AckListener(result =>
                    {
                        tcsAck.TrySetResult(IsOk(result));
                    }));

                    var ackOk = await tcsAck.Task;
                    if (!ackOk)
                    {
                        _purchaseTcs?.TrySetResult((false, "Falha ao reconhecer (acknowledge) a compra."));
                        _purchaseTcs = null;
                        return;
                    }
                }
            }

            _purchaseTcs?.TrySetResult((true, ""));
        }
        catch (Exception ex)
        {
            _purchaseTcs?.TrySetResult((false, ex.Message));
        }
        finally
        {
            _purchaseTcs = null;
        }
    }

    private async Task<(bool ok, string err)> LaunchPurchaseAsync(SkuDetails sku)
    {
        await InitializeAsync();

        var activity = Platform.CurrentActivity;
        if (activity == null)
            return (false, "Activity atual não disponível.");

        var flowParams = BillingFlowParams.NewBuilder()
            .SetSkuDetails(sku)
            .Build();

        _purchaseTcs = new TaskCompletionSource<(bool ok, string err)>();

        var result = _client!.LaunchBillingFlow(activity, flowParams);
        if (!IsOk(result))
        {
            _purchaseTcs.TrySetResult((false, $"{result.ResponseCode}: {result.DebugMessage}"));
            _purchaseTcs = null;
            return (false, $"{result.ResponseCode}: {result.DebugMessage}");
        }

        return await _purchaseTcs.Task;
    }

    /// <summary>
    /// QuerySkuDetails no binding varia. Para compilar SEM depender de overloads,
    /// tentamos a assinatura (SkuDetailsParams, ISkuDetailsResponseListener) via reflection.
    /// </summary>
    private async Task<SkuDetails?> GetSkuDetailsSafeAsync(string skuId)
    {
        if (string.IsNullOrWhiteSpace(skuId))
            return null;

        if (_skuCache.TryGetValue(skuId, out var cached))
            return cached;

        await InitializeAsync();

        var skuParams = SkuDetailsParams.NewBuilder()
            .SetSkusList(new List<string> { skuId })
            .SetType(BillingClient.SkuType.Subs)
            .Build();

        var tcs = new TaskCompletionSource<SkuDetails?>();

        try
        {
            var mi2 = _client!.GetType().GetMethod(
                "QuerySkuDetailsAsync",
                new[] { typeof(SkuDetailsParams), typeof(ISkuDetailsResponseListener) }
            );

            if (mi2 != null)
            {
                mi2.Invoke(_client, new object[]
                {
                    skuParams,
                    new SkuDetailsListener((result, list) =>
                    {
                        if (!IsOk(result) || list == null || list.Count == 0)
                        {
                            tcs.TrySetResult(null);
                            return;
                        }

                        tcs.TrySetResult(list[0]);
                    })
                });

                var got = await tcs.Task;
                if (got != null) _skuCache[skuId] = got;
                return got;
            }

            // Se não existir método compatível, não quebramos o app — só não mostramos preço.
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetPriceString(SkuDetails? d)
    {
        if (d == null) return "";

        // Tentativa direta (alguns bindings expõem Price)
        try
        {
            var prop = d.GetType().GetProperty("Price");
            var val = prop?.GetValue(d) as string;
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }
        catch { }

        return "";
    }

    private static string GetSkuId(PlanTier tier, bool yearly)
    {
        return tier switch
        {
            PlanTier.SuperPremium => yearly ? SuperYearly : SuperMonthly,
            PlanTier.Premium => yearly ? PremiumYearly : PremiumMonthly,
            _ => yearly ? PremiumYearly : PremiumMonthly // Free não compra; fallback seguro
        };
    }

    // ===== Wrappers =====

    private sealed class BillingStateListener : Java.Lang.Object, IBillingClientStateListener
    {
        private readonly Action<BillingResult> _onSetupFinished;
        private readonly Action _onDisconnected;

        public BillingStateListener(Action<BillingResult> onSetupFinished, Action onDisconnected)
        {
            _onSetupFinished = onSetupFinished;
            _onDisconnected = onDisconnected;
        }

        public void OnBillingSetupFinished(BillingResult billingResult) => _onSetupFinished(billingResult);
        public void OnBillingServiceDisconnected() => _onDisconnected();
    }

    private sealed class SkuDetailsListener : Java.Lang.Object, ISkuDetailsResponseListener
    {
        private readonly Action<BillingResult, IList<SkuDetails>?> _cb;
        public SkuDetailsListener(Action<BillingResult, IList<SkuDetails>?> cb) => _cb = cb;

        public void OnSkuDetailsResponse(BillingResult billingResult, IList<SkuDetails>? skuDetailsList)
            => _cb(billingResult, skuDetailsList);
    }

    private sealed class AckListener : Java.Lang.Object, IAcknowledgePurchaseResponseListener
    {
        private readonly Action<BillingResult> _cb;
        public AckListener(Action<BillingResult> cb) => _cb = cb;

        public void OnAcknowledgePurchaseResponse(BillingResult billingResult) => _cb(billingResult);
    }
}
#endif
