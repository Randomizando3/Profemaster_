using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

[QueryProperty(nameof(InstitutionId), "institutionId")]
[QueryProperty(nameof(InstitutionName), "institutionName")]
public partial class ClassesPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    public ObservableCollection<Classroom> Classes { get; } = new();

    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";

    private string _uid = "";
    private string _token = "";

    public ClassesPage(LocalStore store, FirebaseDbService db)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        InstLabel.Text = string.IsNullOrWhiteSpace(InstitutionName) ? "" : InstitutionName;

        var session = await _store.LoadSessionAsync();
        if (session == null || string.IsNullOrWhiteSpace(session.Uid))
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        _uid = session.Uid;
        _token = session.IdToken;

        // cache primeiro
        if (!string.IsNullOrWhiteSpace(InstitutionId))
        {
            var cached = await _store.LoadClassesCacheAsync(InstitutionId);
            if (cached != null && cached.Count > 0 && Classes.Count == 0)
            {
                foreach (var c in cached) Classes.Add(c);
            }

            await LoadFromCloudAsync();
        }
    }

    private async Task LoadFromCloudAsync()
    {
        try
        {
            var list = await _db.GetClassesAsync(_uid, InstitutionId, _token);
            Classes.Clear();
            foreach (var c in list) Classes.Add(c);
            await _store.SaveClassesCacheAsync(InstitutionId, list);
        }
        catch { }
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadFromCloudAsync();

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Nova turma", "Nome:", "Salvar", "Cancelar", "Ex: 8º Ano A");
        if (string.IsNullOrWhiteSpace(name)) return;

        var period = await DisplayPromptAsync("Período", "Manhã/Tarde/Noite:", "Salvar", "Pular", "Ex: Manhã") ?? "";
        var room = await DisplayPromptAsync("Sala", "Opcional:", "Salvar", "Pular", "Ex: Sala 12") ?? "";
        var notes = await DisplayPromptAsync("Observações", "Opcional:", "Salvar", "Pular", "") ?? "";

        var cls = new Classroom
        {
            InstitutionId = InstitutionId,
            Name = name.Trim(),
            Period = period.Trim(),
            Room = room.Trim(),
            Notes = notes.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var ok = await _db.UpsertClassAsync(_uid, InstitutionId, _token, cls);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível salvar a turma.", "OK");
            return;
        }

        Classes.Insert(0, cls);
        await _store.SaveClassesCacheAsync(InstitutionId, Classes.ToList());
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Classroom cls) return;

        var name = await DisplayPromptAsync("Editar turma", "Nome:", "Salvar", "Cancelar", initialValue: cls.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        var period = await DisplayPromptAsync("Editar", "Período:", "Salvar", "Cancelar", initialValue: cls.Period) ?? cls.Period;
        var room = await DisplayPromptAsync("Editar", "Sala:", "Salvar", "Cancelar", initialValue: cls.Room) ?? cls.Room;
        var notes = await DisplayPromptAsync("Editar", "Observações:", "Salvar", "Cancelar", initialValue: cls.Notes) ?? cls.Notes;

        cls.Name = name.Trim();
        cls.Period = period.Trim();
        cls.Room = room.Trim();
        cls.Notes = notes.Trim();

        var ok = await _db.UpsertClassAsync(_uid, InstitutionId, _token, cls);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível atualizar a turma.", "OK");
            return;
        }

        var idx = Classes.IndexOf(cls);
        if (idx >= 0)
        {
            Classes.RemoveAt(idx);
            Classes.Insert(idx, cls);
        }

        await _store.SaveClassesCacheAsync(InstitutionId, Classes.ToList());
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Classroom cls) return;

        var confirm = await DisplayAlert("Excluir", $"Excluir a turma \"{cls.Name}\"?", "Sim", "Não");
        if (!confirm) return;

        var ok = await _db.DeleteClassAsync(_uid, InstitutionId, _token, cls.Id);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível excluir a turma.", "OK");
            return;
        }

        Classes.Remove(cls);
        await _store.SaveClassesCacheAsync(InstitutionId, Classes.ToList());
    }

    private async void OnStudentsClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Classroom cls) return;

        await Shell.Current.GoToAsync(
            $"students?institutionId={Uri.EscapeDataString(InstitutionId)}" +
            $"&institutionName={Uri.EscapeDataString(InstitutionName ?? "")}" +
            $"&classId={Uri.EscapeDataString(cls.Id)}" +
            $"&className={Uri.EscapeDataString(cls.Name)}"
        );
    }
}
