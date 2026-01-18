using System.Reflection;
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

    // cache do item vinculado (para abrir detalhes)
    private Lesson? _linkedLesson;
    private LessonPlan? _linkedPlan;
    private EventItem? _linkedEvent;
    private ExamItem? _linkedExam;

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

        await ReloadFromCloud();

        // vínculo (atalho)
        await LoadLinkedTargetAsync();

        // legado: planos vinculados por LinkedEventId (só para Kind=Evento)
        await LoadLinkedPlansAsync();
    }

    private static string NormKind(string? s)
    {
        var v = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v)) return "Aula";
        return v;
    }

    private static string PrettyKind(string? s)
    {
        var k = NormKind(s);
        if (k.Equals("Plano", StringComparison.OrdinalIgnoreCase)) return "Plano de aula";
        return k;
    }

    private void Render()
    {
        TitleLabel.Text = string.IsNullOrWhiteSpace(_ev.Title) ? "(sem título)" : _ev.Title.Trim();

        var inst = string.IsNullOrWhiteSpace(_ev.InstitutionName) ? "" : _ev.InstitutionName.Trim();
        var cls = string.IsNullOrWhiteSpace(_ev.ClassName) ? "" : _ev.ClassName.Trim();
        MetaLabel.Text = string.Join(" • ", new[] { inst, cls }.Where(x => !string.IsNullOrWhiteSpace(x)));

        TypeLabel.Text = PrettyKind(_ev.Kind);

        PeriodLabel.Text = $"{_ev.Start:dddd, dd/MM/yyyy HH:mm} — {_ev.End:dddd, dd/MM/yyyy HH:mm}";
        DescLabel.Text = string.IsNullOrWhiteSpace(_ev.Description) ? "-" : _ev.Description.Trim();

        LinkCard.IsVisible = false;

        var showLinkedPlans = NormKind(_ev.Kind).Equals("Evento", StringComparison.OrdinalIgnoreCase);
        LinkedPlansCard.IsVisible = showLinkedPlans;
    }

    // ==========================
    // LINK: APENAS ATALHO (SEM EDITAR)
    // ==========================
    private async Task LoadLinkedTargetAsync()
    {
        _linkedLesson = null;
        _linkedPlan = null;
        _linkedEvent = null;
        _linkedExam = null;

        try
        {
            if (string.IsNullOrWhiteSpace(_ev.LinkedKind) || string.IsNullOrWhiteSpace(_ev.LinkedId))
            {
                LinkCard.IsVisible = false;
                return;
            }

            var lk = (_ev.LinkedKind ?? "").Trim();
            var id = (_ev.LinkedId ?? "").Trim();

            if (lk.Equals("Aula", StringComparison.OrdinalIgnoreCase))
            {
                var all = await _db.GetLessonsAllAsync(_uid, _token);
                _linkedLesson = all.FirstOrDefault(x => x.Id == id);

                if (_linkedLesson != null)
                {
                    LinkTitleLabel.Text = "Aula vinculada";
                    LinkInfoLabel.Text = string.IsNullOrWhiteSpace(_linkedLesson.Title) ? "(Sem título)" : _linkedLesson.Title.Trim();
                    BtnOpenLinked.Text = "Ver detalhes da aula";
                    LinkCard.IsVisible = true;
                    return;
                }
            }

            if (lk.Equals("Plano", StringComparison.OrdinalIgnoreCase))
            {
                var all = await _db.GetPlansAllAsync(_uid, _token);
                _linkedPlan = all.FirstOrDefault(x => x.Id == id);

                if (_linkedPlan != null)
                {
                    LinkTitleLabel.Text = "Plano de aula vinculado";
                    LinkInfoLabel.Text = string.IsNullOrWhiteSpace(_linkedPlan.Title) ? "(Sem título)" : _linkedPlan.Title.Trim();
                    BtnOpenLinked.Text = "Ver detalhes do plano";
                    LinkCard.IsVisible = true;
                    return;
                }
            }

            if (lk.Equals("Evento", StringComparison.OrdinalIgnoreCase))
            {
                var all = await _db.GetEventsAllAsync(_uid, _token);
                _linkedEvent = all.FirstOrDefault(x => x.Id == id);

                if (_linkedEvent != null)
                {
                    LinkTitleLabel.Text = "Evento vinculado";
                    LinkInfoLabel.Text = string.IsNullOrWhiteSpace(_linkedEvent.Title) ? "(Sem título)" : _linkedEvent.Title.Trim();
                    BtnOpenLinked.Text = "Ver detalhes do evento";
                    LinkCard.IsVisible = true;
                    return;
                }
            }

            if (lk.Equals("Prova", StringComparison.OrdinalIgnoreCase))
            {
                var all = await _db.GetExamsAllAsync(_uid, _token);
                _linkedExam = all.FirstOrDefault(x => x.Id == id);

                if (_linkedExam != null)
                {
                    LinkTitleLabel.Text = "Prova vinculada";
                    LinkInfoLabel.Text = string.IsNullOrWhiteSpace(_linkedExam.Title) ? "(Sem título)" : _linkedExam.Title.Trim();
                    BtnOpenLinked.Text = "Ver detalhes da prova";
                    LinkCard.IsVisible = true;
                    return;
                }
            }

            LinkCard.IsVisible = false;
        }
        catch
        {
            LinkCard.IsVisible = false;
        }
    }

    private async void OnOpenLinked(object sender, EventArgs e)
    {
        try
        {
            // Plano: abre detalhes (page conhecida, compila)
            if (_linkedPlan != null)
            {
                await Navigation.PushAsync(new PlanDetailsPage(_store, _db, _storage, _linkedPlan));
                return;
            }

            // Aula/Evento/Prova: abrir o item específico (Details ou Editor), sem depender de nomes/assinaturas fixas
            if (_linkedLesson != null)
            {
                var opened =
                    await TryOpenByReflectionAsync("ProfeMaster.Pages.LessonDetailsPage", _linkedLesson) ||
                    await TryOpenByReflectionAsync("ProfeMaster.Pages.LessonEditorPage", _linkedLesson);

                if (!opened) await Shell.Current.GoToAsync("lessons");
                return;
            }

            if (_linkedEvent != null)
            {
                var opened =
                    await TryOpenByReflectionAsync("ProfeMaster.Pages.EventDetailsPage", _linkedEvent) ||
                    await TryOpenByReflectionAsync("ProfeMaster.Pages.EventEditorPage", _linkedEvent);

                if (!opened) await Shell.Current.GoToAsync("events");
                return;
            }

            if (_linkedExam != null)
            {
                var opened =
                    await TryOpenByReflectionAsync("ProfeMaster.Pages.ExamDetailsPage", _linkedExam) ||
                    await TryOpenByReflectionAsync("ProfeMaster.Pages.ExamEditorPage", _linkedExam);

                if (!opened) await Shell.Current.GoToAsync("exams");
                return;
            }

            await DisplayAlert("Info", "Nenhum item vinculado encontrado.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

    /// <summary>
    /// Tenta abrir uma página pelo nome completo (namespace+classe), encontrando um construtor compatível
    /// com os objetos disponíveis: _store, _db, _storage e o model (linked).
    /// Não gera erro de compilação se a página não existir.
    /// </summary>
    private async Task<bool> TryOpenByReflectionAsync(string fullTypeName, object linkedModel)
    {
        try
        {
            var t = FindType(fullTypeName);
            if (t == null) return false;
            if (!typeof(Page).IsAssignableFrom(t)) return false;

            // pool de objetos disponíveis para casar com construtores (por tipo)
            var pool = new List<object> { _store, _db, _storage, linkedModel };

            // escolhe o construtor "mais específico" possível (mais parâmetros casados)
            ConstructorInfo? bestCtor = null;
            object[]? bestArgs = null;
            var bestScore = -1;

            foreach (var ctor in t.GetConstructors())
            {
                var ps = ctor.GetParameters();
                var args = new object[ps.Length];
                var used = new bool[pool.Count];
                var ok = true;
                var score = 0;

                for (int i = 0; i < ps.Length; i++)
                {
                    var pType = ps[i].ParameterType;

                    // tenta achar um objeto na pool que seja atribuível e ainda não usado
                    var found = false;
                    for (int j = 0; j < pool.Count; j++)
                    {
                        if (used[j]) continue;
                        var obj = pool[j];
                        if (obj != null && pType.IsAssignableFrom(obj.GetType()))
                        {
                            args[i] = obj;
                            used[j] = true;
                            found = true;
                            score++;
                            break;
                        }
                    }

                    // se não achou objeto compatível para esse parâmetro, falha esse ctor
                    if (!found)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok && score > bestScore)
                {
                    bestScore = score;
                    bestCtor = ctor;
                    bestArgs = args;
                }
            }

            if (bestCtor == null || bestArgs == null) return false;

            var page = bestCtor.Invoke(bestArgs) as Page;
            if (page == null) return false;

            await Navigation.PushAsync(page);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Type? FindType(string fullTypeName)
    {
        // tenta direto
        var direct = Type.GetType(fullTypeName);
        if (direct != null) return direct;

        // varre assemblies carregados
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
                if (t != null) return t;
            }
            catch { }
        }

        return null;
    }

    // ==========================
    // LEGADO: planos vinculados a evento
    // ==========================
    private async Task LoadLinkedPlansAsync()
    {
        try
        {
            if (!NormKind(_ev.Kind).Equals("Evento", StringComparison.OrdinalIgnoreCase))
            {
                LinkedPlansList.ItemsSource = new List<LessonPlan>();
                return;
            }

            _linkedPlans = await _db.GetPlansLinkedToEventAsync(_uid, _ev.Id, _token);
            LinkedPlansList.ItemsSource = _linkedPlans;
        }
        catch
        {
            LinkedPlansList.ItemsSource = new List<LessonPlan>();
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

        try
        {
            plan.LinkedEventId = "";
            plan.LinkedEventTitle = "";

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

    // ==========================
    // Reload/Edit/Delete
    // ==========================
    private async Task ReloadFromCloud()
    {
        try
        {
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
        var modeLabel = string.Join(" • ", new[] { _ev.InstitutionName, _ev.ClassName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        if (string.IsNullOrWhiteSpace(modeLabel))
            modeLabel = "Geral (todas as turmas)";

        await Navigation.PushModalAsync(new AgendaEventEditorPage(_store, _db, _ev, modeLabel));

        await ReloadFromCloud();
        await LoadLinkedTargetAsync();
        await LoadLinkedPlansAsync();
    }

    private async void OnDelete(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert("Excluir", $"Excluir \"{_ev.Title}\"?", "Sim", "Não");
        if (!confirm) return;

        await _db.DeleteAgendaAllAsync(_uid, _token, _ev.Id);

        if (!string.IsNullOrWhiteSpace(_ev.InstitutionId) && !string.IsNullOrWhiteSpace(_ev.ClassId))
            await _db.DeleteAgendaByClassAsync(_uid, _ev.InstitutionId, _ev.ClassId, _token, _ev.Id);

        await Navigation.PopAsync();
    }
}

