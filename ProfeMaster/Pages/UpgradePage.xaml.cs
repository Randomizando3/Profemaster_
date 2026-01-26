// Pages/UpgradePage.xaml.cs
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

        // 1) Puxa do Firebase (fonte do app)
        var remote = await _db.GetUserPremiumAsync(_uid, _token);
        if (remote != null)
            AppFlags.ApplyPremium(remote.IsPremium, remote.IsPremiumUntil);

        // 2) No Android, tenta inicializar Billing e puxar preços
        await TryInitBillingAndPrices();

        // 3) Best-effort: se Billing disser que tem subs, marca premium (sem servidor)
        await TrySyncPremiumFromBilling();

        RefreshUiFromFlags();
    }

    private async Task TryInitBillingAndPrices()
    {
        if (!_billing.IsSupported)
        {
            BillingHintLabel.Text = "Billing indisponível nesta plataforma (modo dev).";
            MonthlyPriceLabel.Text = "";
            YearlyPriceLabel.Text = "";
            return;
        }

        try
        {
            BillingHintLabel.Text = "Carregando preços…";
            await _billing.InitializeAsync();

            var (m, y) = await _billing.GetPriceLabelsAsync();

            MonthlyPriceLabel.Text = string.IsNullOrWhiteSpace(m) ? "" : $"Mensal: {m}";
            YearlyPriceLabel.Text = string.IsNullOrWhiteSpace(y) ? "" : $"Anual: {y}";

            BillingHintLabel.Text = "";
        }
        catch (Exception ex)
        {
            BillingHintLabel.Text = $"Billing: {ex.Message}";
        }
    }

    private async Task TrySyncPremiumFromBilling()
    {
        if (!_billing.IsSupported) return;

        try
        {
            var has = await _billing.HasActivePremiumAsync();
            if (has && !AppFlags.HasPremiumActive())
            {
                // Sem servidor, não temos expiry real. Por enquanto: deixa “ativo” com 30 dias.
                // Depois, no fluxo definitivo, você vai validar e calcular com servidor/RTDN.
                var until = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
                await ApplyPremiumAndSave(until);
            }
        }
        catch
        {
            // best-effort: não bloqueia UI
        }
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnSetFree(object sender, EventArgs e)
    {
        AppFlags.ApplyPremium(false, 0);

        var ok = await _db.SetUserPremiumAsync(_uid, _token, new UserPremiumState
        {
            IsPremium = false,
            IsPremiumUntil = 0
        });

        RefreshUiFromFlags();

        if (!ok)
            await DisplayAlert("Aviso", "Não foi possível salvar no Firebase. Verifique conexão e tente novamente.", "OK");
    }

    private async void OnBuyMonthly(object sender, EventArgs e)
    {
        // Android: Billing real. Outras plataformas: fallback dev (+30 dias)
        if (!_billing.IsSupported)
        {
            var until = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            await ApplyPremiumAndSave(until);
            return;
        }

        SetBusy(true);
        try
        {
            var (ok, err) = await _billing.PurchaseMonthlyAsync();
            if (!ok)
            {
                await DisplayAlert("Erro", err, "OK");
                return;
            }

            // Sem servidor: até 30 dias (depois você troca para validade real)
            var until = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            await ApplyPremiumAndSave(until);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnBuyYearly(object sender, EventArgs e)
    {
        if (!_billing.IsSupported)
        {
            var until = DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();
            await ApplyPremiumAndSave(until);
            return;
        }

        SetBusy(true);
        try
        {
            var (ok, err) = await _billing.PurchaseYearlyAsync();
            if (!ok)
            {
                await DisplayAlert("Erro", err, "OK");
                return;
            }

            var until = DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();
            await ApplyPremiumAndSave(until);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ApplyPremiumAndSave(long untilUnix)
    {
        AppFlags.ApplyPremium(true, untilUnix);

        var ok = await _db.SetUserPremiumAsync(_uid, _token, new UserPremiumState
        {
            IsPremium = true,
            IsPremiumUntil = untilUnix
        });

        RefreshUiFromFlags();

        if (!ok)
            await DisplayAlert("Aviso", "Não foi possível salvar no Firebase. Verifique conexão e tente novamente.", "OK");
    }

    private void RefreshUiFromFlags()
    {
        var active = AppFlags.HasPremiumActive();

        StatusBadgeLabel.Text = active ? "Premium" : "Grátis";

        FreeTagLabel.Text = active ? "Disponível" : "Selecionado";
        PremiumTagLabel.Text = active ? "Ativo" : "Disponível";

        if (active)
        {
            if (AppFlags.IsPremiumUntil > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(AppFlags.IsPremiumUntil).ToLocalTime();
                PremiumUntilLabel.Text = $"Válido até: {dt:dd/MM/yyyy HH:mm}";
            }
            else
            {
                PremiumUntilLabel.Text = "Válido: sem expiração (modo dev).";
            }
        }
        else
        {
            PremiumUntilLabel.Text = "";
        }

        // Desabilita compra se já está premium
        BuyMonthlyBtn.IsEnabled = !active;
        BuyYearlyBtn.IsEnabled = !active;
    }

    private void SetBusy(bool busy)
    {
        BuyMonthlyBtn.IsEnabled = !busy;
        BuyYearlyBtn.IsEnabled = !busy;
    }
}
