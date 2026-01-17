using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

[QueryProperty(nameof(InstitutionId), "institutionId")]
[QueryProperty(nameof(InstitutionName), "institutionName")]
[QueryProperty(nameof(ClassId), "classId")]
[QueryProperty(nameof(ClassName), "className")]
public partial class StudentsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    public ObservableCollection<StudentContact> Students { get; } = new();

    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";

    private string _uid = "";
    private string _token = "";

    public StudentsPage(LocalStore store, FirebaseDbService db)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        CtxLabel.Text = $"{InstitutionName} • {ClassName}";

        var session = await _store.LoadSessionAsync();
        if (session == null || string.IsNullOrWhiteSpace(session.Uid))
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        _uid = session.Uid;
        _token = session.IdToken;

        // cache primeiro
        if (!string.IsNullOrWhiteSpace(InstitutionId) && !string.IsNullOrWhiteSpace(ClassId))
        {
            var cached = await _store.LoadStudentsCacheAsync(InstitutionId, ClassId);
            if (cached != null && cached.Count > 0 && Students.Count == 0)
            {
                foreach (var s in cached) Students.Add(s);
            }

            await LoadFromCloudAsync();
        }
    }

    private async Task LoadFromCloudAsync()
    {
        try
        {
            var list = await _db.GetStudentsAsync(_uid, InstitutionId, ClassId, _token);
            Students.Clear();
            foreach (var s in list) Students.Add(s);
            await _store.SaveStudentsCacheAsync(InstitutionId, ClassId, list);
        }
        catch { }
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadFromCloudAsync();

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Novo aluno/contato", "Nome:", "Salvar", "Cancelar", "Ex: João Silva");
        if (string.IsNullOrWhiteSpace(name)) return;

        var phone = await DisplayPromptAsync("WhatsApp/Telefone", "Opcional:", "Salvar", "Pular", "Ex: 11 99999-9999") ?? "";
        var email = await DisplayPromptAsync("E-mail", "Opcional:", "Salvar", "Pular", "Ex: aluno@dominio.com") ?? "";
        var notes = await DisplayPromptAsync("Observações", "Opcional:", "Salvar", "Pular", "") ?? "";

        var st = new StudentContact
        {
            InstitutionId = InstitutionId,
            ClassroomId = ClassId,
            Name = name.Trim(),
            Phone = phone.Trim(),
            Email = email.Trim(),
            Notes = notes.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var ok = await _db.UpsertStudentAsync(_uid, InstitutionId, ClassId, _token, st);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível salvar o aluno/contato.", "OK");
            return;
        }

        Students.Insert(0, st);
        await _store.SaveStudentsCacheAsync(InstitutionId, ClassId, Students.ToList());
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not StudentContact st) return;

        var name = await DisplayPromptAsync("Editar", "Nome:", "Salvar", "Cancelar", initialValue: st.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        var phone = await DisplayPromptAsync("Editar", "WhatsApp/Telefone:", "Salvar", "Cancelar", initialValue: st.Phone) ?? st.Phone;
        var email = await DisplayPromptAsync("Editar", "E-mail:", "Salvar", "Cancelar", initialValue: st.Email) ?? st.Email;
        var notes = await DisplayPromptAsync("Editar", "Observações:", "Salvar", "Cancelar", initialValue: st.Notes) ?? st.Notes;

        st.Name = name.Trim();
        st.Phone = phone.Trim();
        st.Email = email.Trim();
        st.Notes = notes.Trim();

        var ok = await _db.UpsertStudentAsync(_uid, InstitutionId, ClassId, _token, st);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível atualizar.", "OK");
            return;
        }

        var idx = Students.IndexOf(st);
        if (idx >= 0)
        {
            Students.RemoveAt(idx);
            Students.Insert(idx, st);
        }

        await _store.SaveStudentsCacheAsync(InstitutionId, ClassId, Students.ToList());
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not StudentContact st) return;

        var confirm = await DisplayAlert("Excluir", $"Excluir \"{st.Name}\"?", "Sim", "Não");
        if (!confirm) return;

        var ok = await _db.DeleteStudentAsync(_uid, InstitutionId, ClassId, _token, st.Id);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível excluir.", "OK");
            return;
        }

        Students.Remove(st);
        await _store.SaveStudentsCacheAsync(InstitutionId, ClassId, Students.ToList());
    }
}
