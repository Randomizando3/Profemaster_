using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class HomePage : ContentPage
{
    private readonly LocalStore _store;

    public HomePage(LocalStore store)
    {
        InitializeComponent();
        _store = store;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var session = await _store.LoadSessionAsync();
        EmailLabel.Text = session?.Email ?? "";
        if (session == null || string.IsNullOrWhiteSpace(session.IdToken))
        {
            await Shell.Current.GoToAsync("//login");
        }
    }

    private async void OnGoInstitutions(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("institutions");
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        await _store.ClearSessionAsync();
        await Shell.Current.GoToAsync("//login");
    }
}
