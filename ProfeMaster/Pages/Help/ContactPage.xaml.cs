using System.Net.Http.Json;
using ProfeMaster.Services;

namespace ProfeMaster.Pages.Help;

public partial class ContactPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;

    private string _uid = "";
    private string _token = "";

    public ContactPage(FirebaseDbService db, LocalStore store)
    {
        InitializeComponent();
        _db = db;
        _store = store;
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
        _token = session.IdToken ?? "";
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private void OnClear(object sender, EventArgs e)
    {
        NameEntry.Text = "";
        MessageEditor.Text = "";
        StatusLabel.Text = "";
    }

    private async void OnSend(object sender, EventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        var msg = (MessageEditor.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Erro", "Informe seu nome.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(msg))
        {
            await DisplayAlert("Erro", "Informe a mensagem.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(_uid))
        {
            await DisplayAlert("Erro", "Sessão inválida. Faça login novamente.", "OK");
            return;
        }

        try
        {
            StatusLabel.Text = "Enviando...";

            var ticketId = Guid.NewGuid().ToString("N");
            var payload = new HelpTicket
            {
                Id = ticketId,
                Name = name,
                Message = msg,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var ok = await _db.PutRawAsync($"users/{_uid}/tickets/{ticketId}", _token, payload);
            if (!ok)
            {
                StatusLabel.Text = "";
                await DisplayAlert("Erro", "Não foi possível enviar o ticket.", "OK");
                return;
            }

            StatusLabel.Text = "Enviado com sucesso.";
            await DisplayAlert("OK", "Mensagem enviada. Obrigado.", "OK");

            // opcional: limpa após enviar
            NameEntry.Text = "";
            MessageEditor.Text = "";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "";
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

    private sealed class HelpTicket
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
