using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class ResetPasswordPage : ContentPage
{
    private readonly FirebaseAuthService _auth;

    public ResetPasswordPage(FirebaseAuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnSend(object sender, EventArgs e)
    {
        var email = (EmailEntry.Text ?? "").Trim();

        var (ok, message) = await _auth.SendPasswordResetEmailAsync(email);
        await DisplayAlert(ok ? "Pronto" : "Atenção", message, "OK");

        if (ok)
            await Shell.Current.GoToAsync("..");
    }
}
