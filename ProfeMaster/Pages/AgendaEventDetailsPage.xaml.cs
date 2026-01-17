using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class AgendaEventDetailsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;
    private readonly FirebaseStorageService _storage;


    private ScheduleEvent _ev;
    private string _uid = "";
    private string _token = "";

    private List<LessonPlan> _linkedPlans = new();
    private List<LessonPlan> _allPlans = new();


    public AgendaEventDetailsPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage, ScheduleEvent ev)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _storage = storage;
        _ev = ev;
        Render();
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

        await LoadLinkedPlansAsync();
        await ReloadFromCloud();
    }


    private async Task LoadLinkedPlansAsync()
    {
        try
        {
            _linkedPlans = await _db.GetPlansLinkedToEventAsync(_uid, _ev.Id, _token);
            LinkedPlansList.ItemsSource = _linkedPlans;
        }
        catch
        {
            LinkedPlansList.ItemsSource = new List<LessonPlan>();
        }
    }


    private async void OnLinkPlan(object sender, EventArgs e)
    {
        try
        {
            _allPlans = await _db.GetPlansAllAsync(_uid, _token);

            // remove os já vinculados
            var candidates = _allPlans.Where(p => p.LinkedEventId != _ev.Id).ToList();
            if (candidates.Count == 0)
            {
                await DisplayAlert("Info", "Não há planos disponíveis para vincular.", "OK");
                return;
            }

            var labels = candidates.Select(p => $"{p.Date:dd/MM/yyyy} • {p.Title}").ToArray();
            var pick = await DisplayActionSheet("Vincular plano", "Cancelar", null, labels);
            if (pick == null || pick == "Cancelar") return;

            var idx = Array.IndexOf(labels, pick);
            if (idx < 0) return;

            var plan = candidates[idx];
            plan.LinkedEventId = _ev.Id;
            plan.LinkedEventTitle = _ev.Title;

            // salva plano
            await _db.UpsertPlanAllAsync(_uid, _token, plan);
            if (!string.IsNullOrWhiteSpace(plan.InstitutionId) && !string.IsNullOrWhiteSpace(plan.ClassId))
                await _db.UpsertPlanByClassAsync(_uid, plan.InstitutionId, plan.ClassId, _token, plan);

            await LoadLinkedPlansAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }


    private async void OnOpenPlan(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not LessonPlan plan) return;

        await Navigation.PushAsync(new PlanDetailsPage(_store, _db, _storage, plan));
    }


    private async void OnUnlinkPlan(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not LessonPlan plan) return;

        var confirm = await DisplayAlert("Desvincular", $"Desvincular \"{plan.Title}\" deste evento?", "Sim", "Não");
        if (!confirm) return;

        plan.LinkedEventId = "";
        plan.LinkedEventTitle = "";

        await _db.UpsertPlanAllAsync(_uid, _token, plan);
        if (!string.IsNullOrWhiteSpace(plan.InstitutionId) && !string.IsNullOrWhiteSpace(plan.ClassId))
            await _db.UpsertPlanByClassAsync(_uid, plan.InstitutionId, plan.ClassId, _token, plan);

        await LoadLinkedPlansAsync();
    }



    private void Render()
    {
        TitleLabel.Text = string.IsNullOrWhiteSpace(_ev.Title) ? "(sem título)" : _ev.Title;

        var inst = string.IsNullOrWhiteSpace(_ev.InstitutionName) ? "" : _ev.InstitutionName;
        var cls = string.IsNullOrWhiteSpace(_ev.ClassName) ? "" : _ev.ClassName;
        MetaLabel.Text = string.Join(" • ", new[] { inst, cls }.Where(x => !string.IsNullOrWhiteSpace(x)));

        TypeLabel.Text = string.IsNullOrWhiteSpace(_ev.Type) ? "-" : _ev.Type;
        PeriodLabel.Text = $"{_ev.Start:dd/MM/yyyy HH:mm} — {_ev.End:dd/MM/yyyy HH:mm}";
        DescLabel.Text = string.IsNullOrWhiteSpace(_ev.Description) ? "-" : _ev.Description;

        LinkLabel.Text = string.IsNullOrWhiteSpace(_ev.LinkedPlanId)
            ? ""
            : $"Vinculado ao plano: {_ev.LinkedPlanTitle}";
    }

    private async Task ReloadFromCloud()
    {
        try
        {
            // tenta buscar do all e substituir o objeto atual se achar
            var all = await _db.GetAgendaAllAsync(_uid, _token);
            var updated = all.FirstOrDefault(x => x.Id == _ev.Id);
            if (updated != null)
            {
                _ev = updated;
                Render();
            }
        }
        catch { }
    }

    private async void OnEdit(object sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new AgendaEventEditorPage(_db, _store, _ev));
        await ReloadFromCloud(); // reflete alterações
    }

    private async void OnDelete(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert("Excluir", $"Excluir \"{_ev.Title}\"?", "Sim", "Não");
        if (!confirm) return;

        await _db.DeleteAgendaAllAsync(_uid, _token, _ev.Id);

        if (!string.IsNullOrWhiteSpace(_ev.InstitutionId) && !string.IsNullOrWhiteSpace(_ev.ClassId))
        {
            await _db.DeleteAgendaByClassAsync(_uid, _ev.InstitutionId, _ev.ClassId, _token, _ev.Id);
        }

        await Navigation.PopAsync();
    }
}
