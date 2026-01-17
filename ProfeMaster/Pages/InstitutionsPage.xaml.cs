using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class InstitutionsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    public ObservableCollection<Institution> Institutions { get; } = new();

    private string _uid = "";
    private string _token = "";

    public InstitutionsPage(LocalStore store, FirebaseDbService db)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        BindingContext = this;
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

        // mostra cache instantâneo
        var cached = await _store.LoadInstitutionsCacheAsync();
        if (cached != null && cached.Count > 0 && Institutions.Count == 0)
        {
            foreach (var c in cached) Institutions.Add(c);
        }

        await LoadFromCloudAsync();
    }

    private async Task LoadFromCloudAsync()
    {
        try
        {
            var list = await _db.GetInstitutionsAsync(_uid, _token);

            Institutions.Clear();
            foreach (var i in list) Institutions.Add(i);

            await _store.SaveInstitutionsCacheAsync(list);
        }
        catch
        {
            // se falhar, fica com cache
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await LoadFromCloudAsync();
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Nova instituição", "Nome da instituição:", "Salvar", "Cancelar", "Ex: Escola Estadual...");
        if (string.IsNullOrWhiteSpace(name)) return;

        var type = await DisplayPromptAsync("Tipo", "Pública / Privada / Particular:", "Salvar", "Cancelar", "Ex: Pública");
        type = string.IsNullOrWhiteSpace(type) ? "Escola" : type.Trim();

        var notes = await DisplayPromptAsync("Observações", "Opcional:", "Salvar", "Pular", "Ex: 8º ano A, sala 12");

        var inst = new Institution
        {
            Name = name.Trim(),
            Type = type.Trim(),
            Notes = (notes ?? "").Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var ok = await _db.UpsertInstitutionAsync(_uid, _token, inst);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível salvar no servidor. Verifique a conexão.", "OK");
            return;
        }

        Institutions.Insert(0, inst);
        await _store.SaveInstitutionsCacheAsync(Institutions.ToList());
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Institution inst) return;

        var name = await DisplayPromptAsync("Editar", "Nome:", "Salvar", "Cancelar", initialValue: inst.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        var type = await DisplayPromptAsync("Editar", "Tipo:", "Salvar", "Cancelar", initialValue: inst.Type);
        if (string.IsNullOrWhiteSpace(type)) type = inst.Type;

        var notes = await DisplayPromptAsync("Editar", "Observações:", "Salvar", "Cancelar", initialValue: inst.Notes);

        inst.Name = name.Trim();
        inst.Type = type.Trim();
        inst.Notes = (notes ?? "").Trim();

        var ok = await _db.UpsertInstitutionAsync(_uid, _token, inst);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível atualizar no servidor.", "OK");
            return;
        }

        // força refresh visual (CollectionView costuma atualizar, mas garantimos)
        var idx = Institutions.IndexOf(inst);
        if (idx >= 0)
        {
            Institutions.RemoveAt(idx);
            Institutions.Insert(idx, inst);
        }

        await _store.SaveInstitutionsCacheAsync(Institutions.ToList());
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Institution inst) return;

        var confirm = await DisplayAlert("Excluir", $"Deseja excluir \"{inst.Name}\"?", "Sim", "Não");
        if (!confirm) return;

        var ok = await _db.DeleteInstitutionAsync(_uid, _token, inst.Id);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível excluir no servidor.", "OK");
            return;
        }

        Institutions.Remove(inst);
        await _store.SaveInstitutionsCacheAsync(Institutions.ToList());
    }

    private async void OnClassesClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Institution inst) return;

        await Shell.Current.GoToAsync($"classes?institutionId={Uri.EscapeDataString(inst.Id)}&institutionName={Uri.EscapeDataString(inst.Name)}");
    }

}
