#if ANDROID
using Android.BillingClient.Api;
using Microsoft.Maui.ApplicationModel;

using AndroidApp = Android.App.Application;

namespace ProfeMaster.Services.Billing;

public sealed class GooglePlayBillingService : Java.Lang.Object, IBillingService, IPurchasesUpdatedListener
{
    // Recomendação: crie 2 subscriptions no Play Console:
    // premium_monthly e premium_yearly
    private const string SkuMonthly = "premium_monthly";
    private const string SkuYearly = "premium_yearly";

    private BillingClient? _client;

    private SkuDetails? _skuMonthly;
    private SkuDetails? _skuYearly;

    private TaskCompletionSource<(bool ok, string err)>? _purchaseTcs;

    public bool IsSupported => true;

    // 0 = OK (BillingResponseCode.OK). Usamos int para evitar incompatibilidades.
    private static bool IsOk(BillingResult r) => r != null && r.ResponseCode == 0;

    // PurchaseState é enum no seu binding.
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

    // Como seu binding está variando as APIs de query, retornamos labels vazios por enquanto.
    // No Upgrade, você mostra "Ver preço no Google Play" e só compra ao clicar.
    public Task<(string monthly, string yearly)> GetPriceLabelsAsync(CancellationToken ct = default)
        => Task.FromResult(("", ""));

    public async Task<(bool ok, string err)> PurchaseMonthlyAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var sku = await GetSkuDetailsSafeAsync(SkuMonthly);
        if (sku == null) return (false, "SKU mensal não encontrado no Google Play.");
        return await LaunchPurchaseAsync(sku);
    }

    public async Task<(bool ok, string err)> PurchaseYearlyAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var sku = await GetSkuDetailsSafeAsync(SkuYearly);
        if (sku == null) return (false, "SKU anual não encontrado no Google Play.");
        return await LaunchPurchaseAsync(sku);
    }

    // Neste momento, deixe o "restore" como false (você já controla Premium pelo Firebase).
    // Quando migrar para Billing v5+ ou um binding mais completo, fazemos restore de verdade.
    public Task<bool> HasActivePremiumAsync(CancellationToken ct = default)
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
    /// QuerySkuDetails no binding 6.2.1 varia. Para compilar SEM depender de overloads,
    /// usamos o ISkuDetailsResponseListener "clássico", mas chamamos o método síncrono se existir,
    /// ou usamos reflexão para chamar QuerySkuDetailsAsync corretamente.
    /// </summary>
    private async Task<SkuDetails?> GetSkuDetailsSafeAsync(string skuId)
    {
        // Cache
        if (string.Equals(skuId, SkuMonthly, StringComparison.OrdinalIgnoreCase) && _skuMonthly != null) return _skuMonthly;
        if (string.Equals(skuId, SkuYearly, StringComparison.OrdinalIgnoreCase) && _skuYearly != null) return _skuYearly;

        await InitializeAsync();

        var skus = new List<string> { skuId };

        var skuParams = SkuDetailsParams.NewBuilder()
            .SetSkusList(skus)
            .SetType(BillingClient.SkuType.Subs)
            .Build();

        // Tenta chamar QuerySkuDetailsAsync(SkuDetailsParams, ISkuDetailsResponseListener)
        // ou QuerySkuDetailsAsync(SkuDetailsParams) dependendo do binding.
        var tcs = new TaskCompletionSource<SkuDetails?>();

        try
        {
            // 1) Tentativa: assinatura com 2 args
            var mi2 = _client!.GetType().GetMethod("QuerySkuDetailsAsync", new[] { typeof(SkuDetailsParams), typeof(ISkuDetailsResponseListener) });
            if (mi2 != null)
            {
                mi2.Invoke(_client, new object[]
                {
                    skuParams,
                    new SkuDetailsListener((result, list) =>
                    {
                        if (!IsOk(result) || list == null || list.Count == 0) { tcs.TrySetResult(null); return; }
                        tcs.TrySetResult(list[0]);
                    })
                });

                var got = await tcs.Task;

                CacheSku(skuId, got);
                return got;
            }

            // 2) Tentativa: assinatura com 1 arg (e retorno void, mas callback interno do binding)
            var mi1 = _client!.GetType().GetMethod("QuerySkuDetailsAsync", new[] { typeof(SkuDetailsParams) });
            if (mi1 != null)
            {
                // Alguns bindings retornam void e disparam evento interno; outros retornam Task-like.
                // Aqui não temos hook, então retornamos null para não quebrar.
                mi1.Invoke(_client, new object[] { skuParams });
                return null;
            }

            // 3) Sem método -> não dá para buscar detalhes de preço nesse binding atual.
            return null;
        }
        catch
        {
            return null;
        }
    }

    private void CacheSku(string skuId, SkuDetails? got)
    {
        if (got == null) return;

        if (string.Equals(skuId, SkuMonthly, StringComparison.OrdinalIgnoreCase)) _skuMonthly = got;
        if (string.Equals(skuId, SkuYearly, StringComparison.OrdinalIgnoreCase)) _skuYearly = got;
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
