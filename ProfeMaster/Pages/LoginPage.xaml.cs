using System.Net.Http.Json;
using ProfeMaster.Config;
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
            await Shell.Current.GoToAsync("home");
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        SetStatus("");
        var email = EmailEntry.Text?.Trim() ?? "";
        var pass = PassEntry.Text ?? "";

        if (!IsValidEmail(email))
        {
            SetStatus("Informe um e-mail válido.");
            return;
        }
        if (pass.Length < 6)
        {
            SetStatus("A senha deve ter ao menos 6 caracteres.");
            return;
        }

        try
        {
            SetBusy(true);

            var (ok, message, session) = await _auth.SignInAsync(email, pass);
            if (!ok || session == null)
            {
                SetStatus(message);
                return;
            }

            await _store.SaveSessionAsync(session);
            await Shell.Current.GoToAsync("home");
        }
        catch
        {
            SetStatus("Falha ao entrar. Verifique sua conexão.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnGoRegisterClicked(object sender, EventArgs e)
    {
        SetStatus("");
        await Shell.Current.GoToAsync("register");
    }

    private async void OnForgotPasswordTapped(object sender, TappedEventArgs e)
    {
        SetStatus("");

        var defaultEmail = (EmailEntry.Text ?? "").Trim();

        var email = await DisplayPromptAsync(
            "Recuperar senha",
            "Digite seu e-mail para receber o link de redefinição:",
            accept: "Enviar",
            cancel: "Cancelar",
            placeholder: "email@dominio.com",
            initialValue: defaultEmail,
            maxLength: 120,
            keyboard: Keyboard.Email);

        email = (email ?? "").Trim();

        if (string.IsNullOrWhiteSpace(email))
            return;

        if (!IsValidEmail(email))
        {
            await DisplayAlert("Atenção", "Informe um e-mail válido.", "OK");
            return;
        }

        try
        {
            SetBusy(true);

            var ok = await SendPasswordResetAsync(email);
            if (ok)
                await DisplayAlert("Pronto!", "Se o e-mail existir, você receberá um link para redefinir a senha.", "OK");
            else
                await DisplayAlert("Atenção", "Não foi possível solicitar a redefinição agora. Verifique sua conexão e tente novamente.", "OK");
        }
        catch
        {
            await DisplayAlert("Atenção", "Falha ao solicitar redefinição. Tente novamente.", "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ===== Helpers UI =====
    private void SetBusy(bool busy)
    {
        Loading.IsVisible = busy;
        Loading.IsRunning = busy;

        LoginButton.IsEnabled = !busy;
        EmailEntry.IsEnabled = !busy;
        PassEntry.IsEnabled = !busy;
    }

    private void SetStatus(string message)
    {
        StatusLabel.Text = message ?? "";
        StatusLabel.IsVisible = !string.IsNullOrWhiteSpace(StatusLabel.Text);
    }

    private static bool IsValidEmail(string email)
        => email.Length >= 5 && email.Contains('@') && email.Contains('.');

    // ===== Firebase Reset Password (Identity Toolkit) =====
    private static string SendOobCodeUrl
        => $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConfig.ApiKey}";

    private sealed class ResetReq
    {
        public string requestType { get; set; } = "PASSWORD_RESET";
        public string email { get; set; } = "";
    }

    private sealed class FirebaseErr
    {
        public FirebaseErrBody error { get; set; } = new();
        public sealed class FirebaseErrBody
        {
            public string message { get; set; } = "";
        }
    }

    private async Task<bool> SendPasswordResetAsync(string email)
    {
        // Obs.: Firebase muitas vezes retorna OK mesmo se o e-mail não existir (por segurança),
        // então tratamos sucesso como "enviamos a solicitação".
        using var http = new HttpClient();
        var req = new ResetReq { email = email.Trim(), requestType = "PASSWORD_RESET" };

        using var resp = await http.PostAsJsonAsync(SendOobCodeUrl, req);
        if (resp.IsSuccessStatusCode)
            return true;

        // Se quiser, você pode mapear mensagens aqui (INVALID_EMAIL, EMAIL_NOT_FOUND, etc.)
        // mas o ideal para UX é manter genérico.
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<FirebaseErr>();
            _ = err?.error?.message; // mantém pra debug se precisar
        }
        catch { }

        return false;
    }
}
