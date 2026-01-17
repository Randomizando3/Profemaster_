using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class AgendaPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    public ObservableCollection<ScheduleEvent> Items { get; } = new();

    private string _uid = "";
    private string _token = "";

    private bool _modeAll = true;
    private string _institutionId = "";
    private string _institutionName = "";
    private string _classId = "";
    private string _className = "";

    public AgendaPage(LocalStore store, FirebaseDbService db)
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

        await LoadCacheThenCloudAsync();
    }

    private async Task LoadCacheThenCloudAsync()
    {
        // cache
        if (_modeAll)
        {
            SubLabel.Text = "Geral (todas as turmas)";
            var cached = await _store.LoadAgendaAllCacheAsync();
            if (cached != null && cached.Count > 0 && Items.Count == 0)
            {
                foreach (var x in cached.OrderBy(x => x.Start)) Items.Add(x);
            }
            await LoadAllFromCloudAsync();
        }
        else
        {
            SubLabel.Text = $"Turma: {_institutionName} • {_className}";
            var cached = await _store.LoadAgendaClassCacheAsync(_institutionId, _classId);
            if (cached != null && cached.Count > 0 && Items.Count == 0)
            {
                foreach (var x in cached.OrderBy(x => x.Start)) Items.Add(x);
            }
            await LoadClassFromCloudAsync();
        }
    }

    private async Task LoadAllFromCloudAsync()
    {
        try
        {
            var list = await _db.GetAgendaAllAsync(_uid, _token);
            Items.Clear();
            foreach (var e in list) Items.Add(e);
            await _store.SaveAgendaAllCacheAsync(list);
        }
        catch { }
    }

    private async Task LoadClassFromCloudAsync()
    {
        if (string.IsNullOrWhiteSpace(_institutionId) || string.IsNullOrWhiteSpace(_classId))
            return;

        try
        {
            var list = await _db.GetAgendaByClassAsync(_uid, _institutionId, _classId, _token);
            Items.Clear();
            foreach (var e in list) Items.Add(e);
            await _store.SaveAgendaClassCacheAsync(_institutionId, _classId, list);
        }
        catch { }
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        if (_modeAll) await LoadAllFromCloudAsync();
        else await LoadClassFromCloudAsync();
    }

    private async void OnModeAllClicked(object sender, EventArgs e)
    {
        _modeAll = true;
        Items.Clear();
        await LoadCacheThenCloudAsync();
    }

    private async void OnModeClassClicked(object sender, EventArgs e)
    {
        // Selecionar instituição e turma
        var instId = await Prompt("Filtrar por turma", "InstitutionId (copie do item de Instituições):");
        if (string.IsNullOrWhiteSpace(instId)) return;

        var clsId = await Prompt("Filtrar por turma", "ClassId (copie do item de Turmas):");
        if (string.IsNullOrWhiteSpace(clsId)) return;

        var instName = await Prompt("Nome da instituição", "Para exibir na tela (opcional):") ?? "";
        var clsName = await Prompt("Nome da turma", "Para exibir na tela (opcional):") ?? "";

        _modeAll = false;
        _institutionId = instId.Trim();
        _classId = clsId.Trim();
        _institutionName = instName.Trim();
        _className = clsName.Trim();

        Items.Clear();
        await LoadCacheThenCloudAsync();
    }

    private async void OnClearFilterClicked(object sender, EventArgs e)
    {
        _modeAll = true;
        _institutionId = _institutionName = _classId = _className = "";
        Items.Clear();
        await LoadCacheThenCloudAsync();
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        // MVP: prompts rápidos
        var title = await Prompt("Novo evento", "Título:");
        if (string.IsNullOrWhiteSpace(title)) return;

        var type = await Prompt("Tipo", "Aula / Prova / Evento:", def: "Aula") ?? "Aula";
        var instName = await Prompt("Instituição", "Nome (opcional):", def: _institutionName) ?? "";
        var className = await Prompt("Turma", "Nome (opcional):", def: _className) ?? "";

        // datas (simplificado)
        var startStr = await Prompt("Início", "Data/hora (ex: 2026-01-20 08:00):", def: DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        if (!DateTime.TryParse(startStr, out var start))
        {
            await DisplayAlert("Erro", "Data de início inválida.", "OK");
            return;
        }

        var endStr = await Prompt("Fim", "Data/hora (ex: 2026-01-20 09:00):", def: start.AddHours(1).ToString("yyyy-MM-dd HH:mm"));
        if (!DateTime.TryParse(endStr, out var end))
        {
            await DisplayAlert("Erro", "Data de fim inválida.", "OK");
            return;
        }

        var desc = await Prompt("Descrição", "Opcional:") ?? "";

        // vínculos: se estiver em modo turma, já fixa
        var ev = new ScheduleEvent
        {
            Title = title.Trim(),
            Type = (type ?? "Aula").Trim(),
            Description = desc.Trim(),
            Start = start,
            End = end,
            InstitutionId = _institutionId,
            InstitutionName = instName.Trim(),
            ClassId = _classId,
            ClassName = className.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Sempre grava na agenda geral
        var okAll = await _db.UpsertAgendaAllAsync(_uid, _token, ev);
        if (!okAll)
        {
            await DisplayAlert("Erro", "Não foi possível salvar na agenda geral.", "OK");
            return;
        }

        // Se tiver turma definida, grava também na agenda por turma
        if (!string.IsNullOrWhiteSpace(ev.InstitutionId) && !string.IsNullOrWhiteSpace(ev.ClassId))
        {
            await _db.UpsertAgendaByClassAsync(_uid, ev.InstitutionId, ev.ClassId, _token, ev);
        }

        Items.Add(ev);
        await PersistCacheAfterChangeAsync();
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not ScheduleEvent ev) return;

        var title = await Prompt("Editar", "Título:", def: ev.Title);
        if (string.IsNullOrWhiteSpace(title)) return;

        var type = await Prompt("Editar", "Tipo:", def: ev.Type) ?? ev.Type;
        var desc = await Prompt("Editar", "Descrição:", def: ev.Description) ?? ev.Description;

        var startStr = await Prompt("Editar", "Início (yyyy-MM-dd HH:mm):", def: ev.Start.ToString("yyyy-MM-dd HH:mm"));
        if (!DateTime.TryParse(startStr, out var start))
        {
            await DisplayAlert("Erro", "Data de início inválida.", "OK");
            return;
        }

        var endStr = await Prompt("Editar", "Fim (yyyy-MM-dd HH:mm):", def: ev.End.ToString("yyyy-MM-dd HH:mm"));
        if (!DateTime.TryParse(endStr, out var end))
        {
            await DisplayAlert("Erro", "Data de fim inválida.", "OK");
            return;
        }

        ev.Title = title.Trim();
        ev.Type = type.Trim();
        ev.Description = desc.Trim();
        ev.Start = start;
        ev.End = end;

        var okAll = await _db.UpsertAgendaAllAsync(_uid, _token, ev);
        if (!okAll)
        {
            await DisplayAlert("Erro", "Não foi possível atualizar na agenda geral.", "OK");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ev.InstitutionId) && !string.IsNullOrWhiteSpace(ev.ClassId))
        {
            await _db.UpsertAgendaByClassAsync(_uid, ev.InstitutionId, ev.ClassId, _token, ev);
        }

        // força refresh visual
        var idx = Items.IndexOf(ev);
        if (idx >= 0)
        {
            Items.RemoveAt(idx);
            Items.Insert(idx, ev);
        }

        await PersistCacheAfterChangeAsync();
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not ScheduleEvent ev) return;

        var confirm = await DisplayAlert("Excluir", $"Excluir \"{ev.Title}\"?", "Sim", "Não");
        if (!confirm) return;

        await _db.DeleteAgendaAllAsync(_uid, _token, ev.Id);

        if (!string.IsNullOrWhiteSpace(ev.InstitutionId) && !string.IsNullOrWhiteSpace(ev.ClassId))
        {
            await _db.DeleteAgendaByClassAsync(_uid, ev.InstitutionId, ev.ClassId, _token, ev.Id);
        }

        Items.Remove(ev);
        await PersistCacheAfterChangeAsync();
    }

    private async Task PersistCacheAfterChangeAsync()
    {
        var list = Items.OrderBy(x => x.Start).ToList();
        if (_modeAll)
            await _store.SaveAgendaAllCacheAsync(list);
        else
            await _store.SaveAgendaClassCacheAsync(_institutionId, _classId, list);
    }

    private Task<string?> Prompt(string title, string msg, string? def = null)
        => DisplayPromptAsync(title, msg, "OK", "Cancelar", initialValue: def);
}
