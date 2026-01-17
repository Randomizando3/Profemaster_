using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class RegisterPage : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly LocalStore _store;

    public RegisterPage(FirebaseAuthService auth, LocalStore store)
    {
        InitializeComponent();
        _auth = auth;
        _store = store;
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
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

            var (ok, message, session) = await _auth.SignUpAsync(email, pass);
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
            StatusLabel.Text = "Falha ao cadastrar. Verifique sua conexão.";
        }
        finally
        {
            ((Button)sender).IsEnabled = true;
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
