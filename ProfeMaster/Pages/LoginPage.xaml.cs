using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class LoginPage : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly LocalStore _store;

    public LoginPage(FirebaseAuthService auth, LocalStore store)
    {
        InitializeComponent();
        _auth = auth;
        _store = store;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var session = await _store.LoadSessionAsync();
        if (session != null && !string.IsNullOrWhiteSpace(session.IdToken))
        {
            await Shell.Current.GoToAsync("home");
        }
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "";

        var email = EmailEntry.Text?.Trim() ?? "";
        var pass = PassEntry.Text ?? "";

        if (email.Length < 5 || !email.Contains("@"))
        {
            StatusLabel.Text = "Informe um e-mail válido.";
            return;
        }
        if (pass.Length < 6)
        {
            StatusLabel.Text = "A senha deve ter ao menos 6 caracteres.";
            return;
        }

        try
        {
            ((Button)sender).IsEnabled = false;

            var (ok, message, session) = await _auth.SignInAsync(email, pass);
            if (!ok || session == null)
            {
                StatusLabel.Text = message;
                return;
            }

            await _store.SaveSessionAsync(session);
            await Shell.Current.GoToAsync("home");
        }
        catch
        {
            StatusLabel.Text = "Falha ao entrar. Verifique sua conexão.";
        }
        finally
        {
            ((Button)sender).IsEnabled = true;
        }
    }

    private async void OnGoRegisterClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "";
        await Shell.Current.GoToAsync("register");
    }
}
