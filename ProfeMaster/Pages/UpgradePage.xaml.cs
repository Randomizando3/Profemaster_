// Pages/UpgradePage.xaml.cs
using ProfeMaster.Config;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class UpgradePage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;

    private string _uid = "";
    private string _token = "";

    public UpgradePage(FirebaseDbService db, LocalStore store)
    {
        InitializeComponent();
        _db = db;
        _store = store;

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

        // Puxa do Firebase para "ficar certinho no servidor"
        var remote = await _db.GetUserPremiumAsync(_uid, _token);
        if (remote != null)
        {
            AppFlags.ApplyPremium(remote.IsPremium, remote.IsPremiumUntil);
        }

        RefreshUiFromFlags();
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

    private async void OnSetPremium30(object sender, EventArgs e)
    {
        // 30 dias a partir de agora
        var until = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        await ApplyPremiumAndSave(until);
    }

    private async void OnSetPremiumYear(object sender, EventArgs e)
    {
        // 365 dias a partir de agora (ano)
        var until = DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();
        await ApplyPremiumAndSave(until);
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

        // tags
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
    }
}
