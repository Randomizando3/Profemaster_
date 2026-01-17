using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class PlanEditorPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;

    private LessonPlan _plan;
    private string _uid = "";
    private string _token = "";

    // contexto (modo geral ou turma)
    private readonly bool _hasClassContext;

    public LessonPlan Result => _plan;

    public PlanEditorPage(FirebaseDbService db, LocalStore store, LessonPlan plan)
    {
        InitializeComponent();
        _db = db;
        _store = store;
        _plan = plan;
        _hasClassContext = !string.IsNullOrWhiteSpace(plan.InstitutionId) && !string.IsNullOrWhiteSpace(plan.ClassId);

        TitleEntry.Text = _plan.Title;
        DatePick.Date = _plan.Date == default ? DateTime.Today : _plan.Date;

        ObjEditor.Text = _plan.Objectives;
        ContentEditor.Text = _plan.Content;
        StepsEditor.Text = _plan.Steps;
        EvalEditor.Text = _plan.Evaluation;
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
    }

    private async void OnCancel(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    private async void OnSave(object sender, EventArgs e)
    {
        _plan.Title = (TitleEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(_plan.Title))
        {
            await DisplayAlert("Erro", "Informe o título.", "OK");
            return;
        }

        _plan.Date = DatePick.Date;
        _plan.Objectives = (ObjEditor.Text ?? "").Trim();
        _plan.Content = (ContentEditor.Text ?? "").Trim();
        _plan.Steps = (StepsEditor.Text ?? "").Trim();
        _plan.Evaluation = (EvalEditor.Text ?? "").Trim();

        // sempre grava no all
        var okAll = await _db.UpsertPlanAllAsync(_uid, _token, _plan);
        if (!okAll)
        {
            await DisplayAlert("Erro", "Falha ao salvar (all).", "OK");
            return;
        }

        // se tiver contexto de turma, grava também em byClass
        if (_hasClassContext)
        {
            await _db.UpsertPlanByClassAsync(_uid, _plan.InstitutionId, _plan.ClassId, _token, _plan);
        }

        await Navigation.PopModalAsync();
    }

    // cria/edita um evento na agenda baseado no plano (opcional)
    private async void OnLinkAgendaClicked(object sender, EventArgs e)
    {
        // Abrimos o editor de evento já preenchido (título=plano)
        var ev = new ScheduleEvent
        {
            Title = $"Plano: {(TitleEntry.Text ?? _plan.Title).Trim()}",
            Type = "Aula",
            Description = "Vinculado a Plano de Aula",
            Start = DatePick.Date.AddHours(8),
            End = DatePick.Date.AddHours(9),
            InstitutionId = _plan.InstitutionId,
            InstitutionName = _plan.InstitutionName,
            ClassId = _plan.ClassId,
            ClassName = _plan.ClassName,
            LinkedPlanId = _plan.Id,
            LinkedPlanTitle = _plan.Title,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await Navigation.PushModalAsync(new AgendaEventEditorPage(_db, _store, ev));
    }
}
