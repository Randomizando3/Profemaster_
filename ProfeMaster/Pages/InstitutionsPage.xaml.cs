using System.Collections.ObjectModel;
using ProfeMaster.Config;
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

    // Editor state
    private Institution? _editing = null;

    private readonly List<string> _typeOptions = new()
    {
        "Pública",
        "Privada",
        "Particular",
        "Escola",
        "Curso",
        "Outros"
    };

    public InstitutionsPage(LocalStore store, FirebaseDbService db)
    {
        InitializeComponent();
        _store = store;
        _db = db;

        BindingContext = this;

        // Picker options
        TypePicker.ItemsSource = _typeOptions;
        TypePicker.SelectedIndex = 0;
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

        // aplica override dev (se você configurou no AppFlags)
        AppFlags.TryApplyDevOverride(_uid);

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
        => await LoadFromCloudAsync();

    // =========================
    // LIMITES (Instituições)
    // =========================
    private static int GetInstitutionsLimit(PlanTier tier) => tier switch
    {
        PlanTier.SuperPremium => int.MaxValue,
        PlanTier.Premium => 20,
        _ => 5
    };

    private async Task<bool> EnsureCanCreateInstitutionAsync()
    {
        var limit = GetInstitutionsLimit(AppFlags.CurrentPlan);
        if (limit == int.MaxValue) return true;

        // +1 porque ele está tentando criar mais uma
        if (Institutions.Count + 1 <= limit) return true;

        var planName = AppFlags.CurrentPlan == PlanTier.Free ? "Grátis" : "Premium";
        var msg = $"Você atingiu o limite do plano {planName}.\n\n" +
                  $"• Limite atual: {limit} instituições\n" +
                  $"• Para criar mais, faça upgrade.";

        var go = await DisplayAlert("Limite atingido", msg, "Ver planos", "Cancelar");
        if (go)
            await Shell.Current.GoToAsync("upgrade");

        return false;
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        // LIMITE: antes de abrir editor
        if (!await EnsureCanCreateInstitutionAsync()) return;

        OpenEditorForNew();
    }

    private void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Institution inst) return;

        OpenEditorForEdit(inst);
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

    // =========================
    // Overlay Editor (Add/Edit)
    // =========================
    private void OpenEditorForNew()
    {
        _editing = null;

        EditorTitleLabel.Text = "Nova instituição";
        NameEntry.Text = "";
        NotesEditor.Text = "";

        TypePicker.SelectedIndex = 0;

        EditorOverlay.IsVisible = true;
        NameEntry.Focus();
    }

    private void OpenEditorForEdit(Institution inst)
    {
        _editing = inst;

        EditorTitleLabel.Text = "Editar instituição";
        NameEntry.Text = inst.Name ?? "";
        NotesEditor.Text = inst.Notes ?? "";

        var idx = _typeOptions.FindIndex(x => string.Equals(x, inst.Type ?? "", StringComparison.OrdinalIgnoreCase));
        TypePicker.SelectedIndex = idx >= 0 ? idx : 0;

        EditorOverlay.IsVisible = true;
        NameEntry.Focus();
    }

    private void CloseEditor()
    {
        EditorOverlay.IsVisible = false;
        _editing = null;
    }

    private void OnEditorClose(object sender, EventArgs e) => CloseEditor();
    private void OnEditorCancel(object sender, EventArgs e) => CloseEditor();

    private async void OnEditorSave(object sender, EventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Erro", "Informe o nome da instituição.", "OK");
            return;
        }

        var type = "";
        if (TypePicker.SelectedIndex >= 0 && TypePicker.SelectedIndex < _typeOptions.Count)
            type = _typeOptions[TypePicker.SelectedIndex];
        if (string.IsNullOrWhiteSpace(type))
            type = "Escola";

        var notes = (NotesEditor.Text ?? "").Trim();

        var isNew = _editing == null;

        // LIMITE: valida também no salvar, para evitar bypass
        if (isNew)
        {
            if (!await EnsureCanCreateInstitutionAsync())
                return;
        }

        if (isNew)
        {
            var inst = new Institution
            {
                Name = name,
                Type = type,
                Notes = notes,
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
        else
        {
            _editing!.Name = name;
            _editing.Type = type;
            _editing.Notes = notes;

            var ok = await _db.UpsertInstitutionAsync(_uid, _token, _editing);
            if (!ok)
            {
                await DisplayAlert("Erro", "Não foi possível atualizar no servidor.", "OK");
                return;
            }

            // força refresh visual
            var idx = Institutions.IndexOf(_editing);
            if (idx >= 0)
            {
                Institutions.RemoveAt(idx);
                Institutions.Insert(idx, _editing);
            }

            await _store.SaveInstitutionsCacheAsync(Institutions.ToList());
        }

        CloseEditor();
    }
}
