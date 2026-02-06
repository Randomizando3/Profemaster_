using ProfeMaster.Config;
using ProfeMaster.Models;
using ProfeMaster.Services;
using ProfeMaster.Services.Billing;

namespace ProfeMaster.Pages;

public partial class UpgradePage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;
    private readonly IBillingService _billing;

    private string _uid = "";
    private string _token = "";

    public UpgradePage(FirebaseDbService db, LocalStore store, IBillingService billing)
    {
        InitializeComponent();
        _db = db;
        _store = store;
        _billing = billing;

        RefreshUiFromFlags();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var session = await _store.LoadSessionAsync();
        if (session == null || string.IsNullOrWhiteSpace(session.Uid))
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        _uid = session.Uid;
        _token = session.IdToken;

        // 0) DEV override (3 UIDs de teste) - se bater, não precisa Firebase
        var forced = AppFlags.TryApplyDevOverride(_uid);

        // 1) Firebase (fonte do app) - só se não foi forçado por UID
        if (!forced)
        {
            var remote = await _db.GetUserPremiumAsync(_uid, _token);
            if (remote != null)
            {
                var (tier, until) = remote.ToPlan();
                AppFlags.ApplyPlan(tier, until);
            }
            else
            {
                AppFlags.ApplyPlan(PlanTier.Free, 0);
            }
        }

        // 2) Billing init e preços
        await TryInitBillingAndPrices();

        // 3) (Opcional) restore best-effort - por enquanto mantém off
        // await TrySyncFromBilling();

        RefreshUiFromFlags();
    }

    private async Task TryInitBillingAndPrices()
    {
        if (!_billing.IsSupported)
        {
            BillingHintLabel.Text = "Billing indisponível nesta plataforma (modo dev).";
            PremiumMonthlyPriceLabel.Text = "";
            PremiumYearlyPriceLabel.Text = "";
            SuperMonthlyPriceLabel.Text = "";
            SuperYearlyPriceLabel.Text = "";
            return;
        }

        try
        {
            BillingHintLabel.Text = "Carregando preços…";
            await _billing.InitializeAsync();

            var (pm, py) = await _billing.GetPriceLabelsAsync(PlanTier.Premium);
            PremiumMonthlyPriceLabel.Text = string.IsNullOrWhiteSpace(pm) ? "" : $"Mensal: {pm}";
            PremiumYearlyPriceLabel.Text = string.IsNullOrWhiteSpace(py) ? "" : $"Anual: {py}";

            var (sm, sy) = await _billing.GetPriceLabelsAsync(PlanTier.SuperPremium);
            SuperMonthlyPriceLabel.Text = string.IsNullOrWhiteSpace(sm) ? "" : $"Mensal: {sm}";
            SuperYearlyPriceLabel.Text = string.IsNullOrWhiteSpace(sy) ? "" : $"Anual: {sy}";

            // Se o binding não conseguiu preço, deixa uma dica
            if (string.IsNullOrWhiteSpace(pm) && string.IsNullOrWhiteSpace(py) &&
                string.IsNullOrWhiteSpace(sm) && string.IsNullOrWhiteSpace(sy))
            {
                BillingHintLabel.Text = "Os preços podem aparecer apenas na tela do Google Play ao confirmar a compra.";
            }
            else
            {
                BillingHintLabel.Text = "";
            }
        }
        catch (Exception ex)
        {
            BillingHintLabel.Text = $"Billing: {ex.Message}";
        }
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    // ===== FREE =====
    private async void OnSetFree(object sender, EventArgs e)
    {
        await ApplyPlanAndSave(PlanTier.Free, 0);
    }

    // ===== PREMIUM =====
    private async void OnBuyPremiumMonthly(object sender, EventArgs e)
        => await BuyAndApplyAsync(PlanTier.Premium, days: 30);

    private async void OnBuyPremiumYearly(object sender, EventArgs e)
        => await BuyAndApplyAsync(PlanTier.Premium, days: 365);

    // ===== SUPER =====
    private async void OnBuySuperMonthly(object sender, EventArgs e)
        => await BuyAndApplyAsync(PlanTier.SuperPremium, days: 30);

    private async void OnBuySuperYearly(object sender, EventArgs e)
        => await BuyAndApplyAsync(PlanTier.SuperPremium, days: 365);

    private async Task BuyAndApplyAsync(PlanTier tier, int days)
    {
        // Se forçou por UID, não deixa comprar (evita confusão de teste)
        if (AppFlags.TryApplyDevOverride(_uid))
        {
            RefreshUiFromFlags();
            await DisplayAlert("Modo teste", "Este usuário está com plano forçado por UID (dev override).", "OK");
            return;
        }

        // Outras plataformas: fallback dev
        if (!_billing.IsSupported)
        {
            var untilDev = DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds();
            await ApplyPlanAndSave(tier, untilDev);
            return;
        }

        SetBusy(true);
        try
        {
            var (ok, err) = days >= 365
                ? await _billing.PurchaseYearlyAsync(tier)
                : await _billing.PurchaseMonthlyAsync(tier);

            if (!ok)
            {
                await DisplayAlert("Erro", err, "OK");
                return;
            }

            // Sem validação server-side: grava “até +X dias”
            var until = DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds();
            await ApplyPlanAndSave(tier, until);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ApplyPlanAndSave(PlanTier tier, long untilUnixSeconds)
    {
        AppFlags.ApplyPlan(tier, untilUnixSeconds);

        var state = UserPremiumState.FromPlan(tier, untilUnixSeconds);
        var ok = await _db.SetUserPremiumAsync(_uid, _token, state);

        RefreshUiFromFlags();

        if (!ok)
            await DisplayAlert("Aviso", "Não foi possível salvar no Firebase. Verifique conexão e tente novamente.", "OK");
    }

    private void RefreshUiFromFlags()
    {
        var plan = AppFlags.CurrentPlan;
        var active = AppFlags.HasPlanActive();

        StatusBadgeLabel.Text = plan switch
        {
            PlanTier.SuperPremium => "SuperPremium",
            PlanTier.Premium => "Premium",
            _ => "Grátis"
        };

        FreeTagLabel.Text = plan == PlanTier.Free ? "Selecionado" : "Disponível";
        PremiumTagLabel.Text = plan == PlanTier.Premium ? "Ativo" : "Disponível";
        SuperTagLabel.Text = plan == PlanTier.SuperPremium ? "Ativo" : "Disponível";

        if (plan != PlanTier.Free && active)
        {
            if (AppFlags.PlanUntil > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(AppFlags.PlanUntil).ToLocalTime();
                PlanUntilLabel.Text = $"Válido até: {dt:dd/MM/yyyy HH:mm}";
            }
            else
            {
                PlanUntilLabel.Text = "Válido: sem expiração (modo dev).";
            }
        }
        else
        {
            PlanUntilLabel.Text = "";
        }

        // Compras:
        BuyPremiumMonthlyBtn.IsEnabled = plan == PlanTier.Free;
        BuyPremiumYearlyBtn.IsEnabled = plan == PlanTier.Free;

        // SuperPremium: permite upgrade de Free/Premium
        BuySuperMonthlyBtn.IsEnabled = plan != PlanTier.SuperPremium;
        BuySuperYearlyBtn.IsEnabled = plan != PlanTier.SuperPremium;
    }

    private void SetBusy(bool busy)
    {
        BuyPremiumMonthlyBtn.IsEnabled = !busy && AppFlags.CurrentPlan == PlanTier.Free;
        BuyPremiumYearlyBtn.IsEnabled = !busy && AppFlags.CurrentPlan == PlanTier.Free;

        BuySuperMonthlyBtn.IsEnabled = !busy && AppFlags.CurrentPlan != PlanTier.SuperPremium;
        BuySuperYearlyBtn.IsEnabled = !busy && AppFlags.CurrentPlan != PlanTier.SuperPremium;
    }
}
