using System.Net.Http.Json;
using ProfeMaster.Config;
using ProfeMaster.Models;

namespace ProfeMaster.Services;

public sealed class FirebaseAuthService
{
    private readonly HttpClient _http;

    public FirebaseAuthService(HttpClient http)
    {
        _http = http;
    }

    private static string SignUpUrl => $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseConfig.ApiKey}";
    private static string SignInUrl => $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseConfig.ApiKey}";

    // ✅ NOVO: endpoint de reset
    private static string SendOobCodeUrl => $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConfig.ApiKey}";

    private sealed class AuthReq
    {
        public string email { get; set; } = "";
        public string password { get; set; } = "";
        public bool returnSecureToken { get; set; } = true;
    }

    private sealed class AuthRes
    {
        public string idToken { get; set; } = "";
        public string refreshToken { get; set; } = "";
        public string localId { get; set; } = "";
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

    // ✅ NOVO: request de reset
    private sealed class SendOobReq
    {
        public string requestType { get; set; } = "PASSWORD_RESET";
        public string email { get; set; } = "";
    }

    public async Task<(bool ok, string message, UserSession? session)> SignInAsync(string email, string password)
        => await AuthAsync(SignInUrl, email, password);

    public async Task<(bool ok, string message, UserSession? session)> SignUpAsync(string email, string password)
        => await AuthAsync(SignUpUrl, email, password);

    // ✅ NOVO: dispara email de recuperação (Firebase envia o e-mail)
    public async Task<(bool ok, string message)> SendPasswordResetEmailAsync(string email)
    {
        email = (email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email))
            return (false, "Informe o e-mail.");

        var req = new SendOobReq { requestType = "PASSWORD_RESET", email = email };

        using var resp = await _http.PostAsJsonAsync(SendOobCodeUrl, req);
        if (resp.IsSuccessStatusCode)
            return (true, "Se o e-mail existir, você receberá um link para redefinir a senha.");

        // erro do Firebase
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<FirebaseErr>();
            var msg = err?.error?.message ?? "Falha ao solicitar recuperação.";
            msg = msg switch
            {
                "EMAIL_NOT_FOUND" => "Se o e-mail existir, você receberá um link para redefinir a senha.", // não “vaza” info
                "INVALID_EMAIL" => "E-mail inválido.",
                "USER_DISABLED" => "Usuário desabilitado.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Muitas tentativas. Tente novamente mais tarde.",
                _ => msg
            };
            return (false, msg);
        }
        catch
        {
            return (false, "Falha ao solicitar recuperação. Verifique sua conexão.");
        }
    }

    private async Task<(bool ok, string message, UserSession? session)> AuthAsync(string url, string email, string password)
    {
        var req = new AuthReq { email = email.Trim(), password = password, returnSecureToken = true };

        using var resp = await _http.PostAsJsonAsync(url, req);
        if (resp.IsSuccessStatusCode)
        {
            var data = await resp.Content.ReadFromJsonAsync<AuthRes>();
            if (data == null || string.IsNullOrWhiteSpace(data.idToken) || string.IsNullOrWhiteSpace(data.localId))
                return (false, "Resposta inválida do Firebase.", null);

            var session = new UserSession
            {
                Uid = data.localId,
                Email = data.email ?? email,
                IdToken = data.idToken,
                RefreshToken = data.refreshToken ?? "",
                CreatedAt = DateTimeOffset.UtcNow
            };

            return (true, "OK", session);
        }

        // erro do Firebase
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<FirebaseErr>();
            var msg = err?.error?.message ?? "Falha ao autenticar.";
            msg = msg switch
            {
                "EMAIL_NOT_FOUND" => "E-mail não encontrado.",
                "INVALID_PASSWORD" => "Senha inválida.",
                "USER_DISABLED" => "Usuário desabilitado.",
                "EMAIL_EXISTS" => "Este e-mail já está cadastrado.",
                "OPERATION_NOT_ALLOWED" => "Operação não permitida no Firebase Auth.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Muitas tentativas. Tente novamente mais tarde.",
                _ => msg
            };
            return (false, msg, null);
        }
        catch
        {
            return (false, "Falha ao autenticar. Verifique sua conexão.", null);
        }
    }
}
