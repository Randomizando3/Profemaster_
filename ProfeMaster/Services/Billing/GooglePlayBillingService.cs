#if ANDROID
using Android.BillingClient.Api;
using Microsoft.Maui.ApplicationModel;
using ProfeMaster.Config;

using AndroidApp = Android.App.Application;

namespace ProfeMaster.Services.Billing;

public sealed class GooglePlayBillingService : Java.Lang.Object, IBillingService, IPurchasesUpdatedListener
{
    private const string PremiumMonthly = "premium_monthly";
    private const string PremiumYearly = "premium_yearly";
    private const string SuperMonthly = "superpremium_monthly";
    private const string SuperYearly = "superpremium_yearly";

    private readonly Dictionary<string, ProductDetails> _productCache = new(StringComparer.OrdinalIgnoreCase);
    private BillingClient? _client;
    private TaskCompletionSource<(bool ok, string err)>? _purchaseTcs;

    public bool IsSupported => true;

    private static bool IsOk(BillingResult r) => r != null && r.ResponseCode == 0;
    private static bool IsPurchased(Purchase p) => p != null && p.PurchaseState == PurchaseState.Purchased;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_client != null && _client.IsReady) return;

        var tcs = new TaskCompletionSource<bool>();

        _client = BillingClient.NewBuilder(AndroidApp.Context)
            .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
            .SetListener(this)
            .Build();

        _client.StartConnection(new BillingStateListener(
            result =>
            {
                if (IsOk(result)) tcs.TrySetResult(true);
                else tcs.TrySetException(new Exception($"Billing setup falhou: {result.ResponseCode} - {result.DebugMessage}"));
            },
            () => { }
        ));

        await tcs.Task;
    }

    public async Task<(string monthly, string yearly)> GetPriceLabelsAsync(PlanTier tier, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var monthly = await GetProductDetailsSafeAsync(GetProductId(tier, yearly: false));
        var yearly = await GetProductDetailsSafeAsync(GetProductId(tier, yearly: true));

        return (TryGetPriceString(monthly), TryGetPriceString(yearly));
    }

    public async Task<(bool ok, string err)> PurchaseMonthlyAsync(PlanTier tier, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var productId = GetProductId(tier, yearly: false);
        var product = await GetProductDetailsSafeAsync(productId);

        if (product == null)
            return (false, $"Produto mensal não encontrado no Google Play: {productId}");

        return await LaunchPurchaseAsync(product);
    }

    public async Task<(bool ok, string err)> PurchaseYearlyAsync(PlanTier tier, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var productId = GetProductId(tier, yearly: true);
        var product = await GetProductDetailsSafeAsync(productId);

        if (product == null)
            return (false, $"Produto anual não encontrado no Google Play: {productId}");

        return await LaunchPurchaseAsync(product);
    }

    public async Task<bool> HasActivePlanAsync(PlanTier tier, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var purchases = await QueryActiveSubscriptionsAsync();
        if (purchases.Count == 0) return false;

        var monthly = GetProductId(tier, yearly: false);
        var yearly = GetProductId(tier, yearly: true);

        return purchases.Any(p =>
            IsPurchased(p) &&
            (PurchaseContainsProduct(p, monthly) || PurchaseContainsProduct(p, yearly)));
    }

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

    private async Task<(bool ok, string err)> LaunchPurchaseAsync(ProductDetails product)
    {
        await InitializeAsync();

        var activity = Platform.CurrentActivity;
        if (activity == null)
            return (false, "Activity atual não disponível.");

        var productParamsBuilder = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(product);

        var offerToken = GetOfferToken(product);
        if (!string.IsNullOrWhiteSpace(offerToken))
            productParamsBuilder.SetOfferToken(offerToken);

        var flowParams = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList(new List<BillingFlowParams.ProductDetailsParams>
            {
                productParamsBuilder.Build()
            })
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

    private async Task<ProductDetails?> GetProductDetailsSafeAsync(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return null;

        if (_productCache.TryGetValue(productId, out var cached))
            return cached;

        await InitializeAsync();

        var product = QueryProductDetailsParams.Product.NewBuilder()
            .SetProductId(productId)
            .SetProductType(BillingClient.ProductType.Subs)
            .Build();

        var queryParams = QueryProductDetailsParams.NewBuilder()
            .SetProductList(new List<QueryProductDetailsParams.Product> { product })
            .Build();

        var response = await _client!.QueryProductDetailsAsync(queryParams);
        if (response == null || !IsOk(response.Result))
            return null;

        var got = response.ProductDetailsList?.FirstOrDefault()
            ?? response.ProductDetails?.FirstOrDefault();
        if (got != null) _productCache[productId] = got;
        return got;
    }

    private async Task<IList<Purchase>> QueryActiveSubscriptionsAsync()
    {
        await InitializeAsync();

        var query = QueryPurchasesParams.NewBuilder()
            .SetProductType(BillingClient.ProductType.Subs)
            .Build();

        var response = await _client!.QueryPurchasesAsync(query);
        return IsOk(response.Result) && response.Purchases != null
            ? response.Purchases
            : new List<Purchase>();
    }

    private static string TryGetPriceString(ProductDetails? product)
    {
        try
        {
            return product?
                .GetSubscriptionOfferDetails()?
                .FirstOrDefault()?
                .PricingPhases?
                .PricingPhaseList?
                .FirstOrDefault()?
                .FormattedPrice ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string GetOfferToken(ProductDetails product)
        => product.GetSubscriptionOfferDetails()?.FirstOrDefault()?.OfferToken ?? "";

    private static bool PurchaseContainsProduct(Purchase purchase, string productId)
    {
        try
        {
            return purchase.Products?.Any(p => string.Equals(p, productId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetProductId(PlanTier tier, bool yearly)
    {
        return tier switch
        {
            PlanTier.SuperPremium => yearly ? SuperYearly : SuperMonthly,
            PlanTier.Premium => yearly ? PremiumYearly : PremiumMonthly,
            _ => yearly ? PremiumYearly : PremiumMonthly
        };
    }

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

    private sealed class AckListener : Java.Lang.Object, IAcknowledgePurchaseResponseListener
    {
        private readonly Action<BillingResult> _cb;

        public AckListener(Action<BillingResult> cb)
        {
            _cb = cb;
        }

        public void OnAcknowledgePurchaseResponse(BillingResult billingResult) => _cb(billingResult);
    }
}
#endif
