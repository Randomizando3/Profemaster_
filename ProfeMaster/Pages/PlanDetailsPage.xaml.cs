using ProfeMaster.Models;
using ProfeMaster.Services;

using System.Reflection;

namespace ProfeMaster.Pages;

public partial class PlanDetailsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;
    private readonly FirebaseStorageService _storage;

    private LessonPlan _plan;

    private string _uid = "";
    private string _token = "";

    // para abrir aula
    private List<Lesson> _lessons = new();

    // VMs
    private readonly List<SlotDayVm> _days = new();

    // vínculo resolvido (pode vir do plano OU da agenda)
    private string _resolvedAgendaEventId = "";
    private string _resolvedAgendaEventTitle = "";

    public PlanDetailsPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage, LessonPlan plan)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _storage = storage;
        _plan = plan;

        RenderHeader(loadingAgenda: true);
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
        await LoadLessonsAsync();

        // tenta resolver vínculo da agenda (mesmo se o plano não tiver LinkedEventId)
        await ResolveAgendaLinkAsync();

        EnsureSlotMigration();
        RebuildPreviewUi();
    }

    // =========================
    // HEADER
    // =========================
    private void RenderHeader(bool loadingAgenda)
    {
        TitleLabel.Text = string.IsNullOrWhiteSpace(_plan.Title) ? "(Sem título)" : _plan.Title.Trim();

        var start = _plan.StartDate == default
            ? (_plan.Date == default ? DateTime.Today : _plan.Date.Date)
            : _plan.StartDate.Date;

        var end = _plan.EndDate == default ? start : _plan.EndDate.Date;
        if (end < start) (start, end) = (end, start);

        var classInfo = $"{_plan.InstitutionName} • {_plan.ClassName}".Trim();
        classInfo = classInfo.Trim(' ', '•');

        MetaLabel.Text = $"{start:dd/MM/yyyy} → {end:dd/MM/yyyy}" +
                         (string.IsNullOrWhiteSpace(classInfo) ? "" : $" • {classInfo}");

        ObsLabel.Text = string.IsNullOrWhiteSpace(_plan.Observations)
            ? "Observações: -"
            : $"Observações: {_plan.Observations.Trim()}";

        if (loadingAgenda)
        {
            AgendaLinkLabel.Text = "Carregando vínculo...";
        }
        else
        {
            // prioridade: vínculo resolvido (agenda ou plano)
            var id = (_resolvedAgendaEventId ?? "").Trim();
            var title = (_resolvedAgendaEventTitle ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(id))
            {
                AgendaLinkLabel.Text = string.IsNullOrWhiteSpace(title)
                    ? "Vinculado: (sem título)"
                    : $"Vinculado: {title}";
            }
            else
            {
                AgendaLinkLabel.Text = "Nenhum vínculo.";
            }
        }

        // Thumb offline-first
        try
        {
            var localPath = _plan.ThumbLocalPath ?? "";
            var url = _plan.ThumbUrl ?? "";

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                ThumbImage.Source = ImageSource.FromFile(localPath);
            else if (!string.IsNullOrWhiteSpace(url))
                ThumbImage.Source = new UriImageSource { Uri = new Uri(url), CachingEnabled = true };
            else
                ThumbImage.Source = null;
        }
        catch
        {
            ThumbImage.Source = null;
        }
    }

    // =========================
    // VÍNCULO AGENDA (RESOLVE MESMO SE NÃO ESTIVER NO PLANO)
    // =========================
    private async Task ResolveAgendaLinkAsync()
    {
        try
        {
            // 1) Se o plano já tem o vínculo salvo, usa direto
            var planLinkedId = (_plan.LinkedEventId ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(planLinkedId))
            {
                _resolvedAgendaEventId = planLinkedId;
                _resolvedAgendaEventTitle = (_plan.LinkedEventTitle ?? "").Trim();
                RenderHeader(loadingAgenda: false);
                return;
            }

            // 2) Caso não tenha no plano, tenta localizar evento na agenda que aponta para este plano
            //    Fazendo isso por reflection para não depender do nome exato dos métodos.
            var candidates = await TryGetAgendaEventsByReflectionAsync();
            if (candidates != null)
            {
                var found = FindAgendaEventLinkedToPlan(candidates, _plan.Id);
                if (found != null)
                {
                    _resolvedAgendaEventId = (GetString(found, "Id", "EventId", "Key") ?? "").Trim();
                    _resolvedAgendaEventTitle = (GetString(found, "Title", "Name", "EventTitle") ?? "").Trim();
                }
            }
        }
        catch
        {
            // ignora
        }
        finally
        {
            RenderHeader(loadingAgenda: false);
        }
    }

    private async Task<IEnumerable<object>?> TryGetAgendaEventsByReflectionAsync()
    {
        // Métodos prováveis no seu FirebaseDbService (tentamos alguns nomes comuns)
        // Assinaturas comuns:
        //   Task<List<T>> GetAgendaAllAsync(string uid, string token)
        //   Task<List<T>> GetAgendaByClassAsync(string uid, string instId, string classId, string token)
        //   Task<List<T>> GetEventsAllAsync(string uid, string token)
        //   Task<List<T>> GetAgendaAsync(string uid, string token)
        var methodNames = new[]
        {
            "GetAgendaAllAsync",
            "GetAgendaAsync",
            "GetEventsAllAsync",
            "GetEventsAsync",
            "GetAgendaByClassAsync",
            "GetEventsByClassAsync"
        };

        foreach (var name in methodNames)
        {
            try
            {
                var mi = _db.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) continue;

                var pars = mi.GetParameters();

                object? taskObj = null;

                // by class (uid, instId, classId, token)
                if (pars.Length == 4)
                {
                    var instId = (_plan.InstitutionId ?? "").Trim();
                    var classId = (_plan.ClassId ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(instId) || string.IsNullOrWhiteSpace(classId))
                        continue;

                    taskObj = mi.Invoke(_db, new object[] { _uid, instId, classId, _token });
                }
                // all (uid, token)
                else if (pars.Length == 2)
                {
                    taskObj = mi.Invoke(_db, new object[] { _uid, _token });
                }
                else
                {
                    continue;
                }

                if (taskObj == null) continue;

                // await Task<...>
                await (Task)taskObj;

                // pega Result via reflection
                var resultProp = taskObj.GetType().GetProperty("Result");
                var result = resultProp?.GetValue(taskObj);

                if (result is System.Collections.IEnumerable enumerable)
                {
                    var list = new List<object>();
                    foreach (var it in enumerable)
                    {
                        if (it != null) list.Add(it);
                    }
                    return list;
                }
            }
            catch
            {
                // tenta o próximo nome
            }
        }

        return null;
    }

    private object? FindAgendaEventLinkedToPlan(IEnumerable<object> events, string planId)
    {
        foreach (var ev in events)
        {
            try
            {
                // tenta várias propriedades prováveis de link
                var linked =
                    GetString(ev, "PlanId", "LessonPlanId", "LinkedPlanId", "LinkedPlan", "PlanKey", "PlanRef") ??
                    GetString(ev, "LinkedId", "RefId");

                if (!string.IsNullOrWhiteSpace(linked) && linked.Trim() == planId)
                    return ev;

                // às vezes o objeto tem um sub-objeto (Event) com propriedades
                var sub = GetObject(ev, "Event", "AgendaEvent", "Data");
                if (sub != null)
                {
                    var linked2 =
                        GetString(sub, "PlanId", "LessonPlanId", "LinkedPlanId", "PlanKey", "PlanRef") ??
                        GetString(sub, "LinkedId", "RefId");

                    if (!string.IsNullOrWhiteSpace(linked2) && linked2.Trim() == planId)
                        return ev;
                }
            }
            catch
            {
                // ignora
            }
        }

        return null;
    }

    private static string? GetString(object obj, params string[] propNames)
    {
        foreach (var p in propNames)
        {
            var pi = obj.GetType().GetProperty(p, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi == null) continue;

            var v = pi.GetValue(obj);
            if (v == null) continue;

            return v.ToString();
        }
        return null;
    }

    private static object? GetObject(object obj, params string[] propNames)
    {
        foreach (var p in propNames)
        {
            var pi = obj.GetType().GetProperty(p, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi == null) continue;

            var v = pi.GetValue(obj);
            if (v != null) return v;
        }
        return null;
    }

    // =========================
    // CLOUD
    // =========================
    private async Task ReloadFromCloud()
    {
        try
        {
            LessonPlan? updated = null;

            if (!string.IsNullOrWhiteSpace(_plan.InstitutionId) && !string.IsNullOrWhiteSpace(_plan.ClassId))
            {
                var list = await _db.GetPlansByClassAsync(_uid, _plan.InstitutionId, _plan.ClassId, _token);
                updated = list.FirstOrDefault(x => x.Id == _plan.Id);
            }

            if (updated == null)
            {
                var all = await _db.GetPlansAllAsync(_uid, _token);
                updated = all.FirstOrDefault(x => x.Id == _plan.Id);
            }

            if (updated != null)
            {
                _plan = updated;

                // se o plano veio atualizado com vínculo, atualiza resolved também
                _resolvedAgendaEventId = (_plan.LinkedEventId ?? "").Trim();
                _resolvedAgendaEventTitle = (_plan.LinkedEventTitle ?? "").Trim();

                RenderHeader(loadingAgenda: true);
            }
        }
        catch
        {
            // não quebra a tela
        }
    }

    private async Task LoadLessonsAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_plan.InstitutionId) && !string.IsNullOrWhiteSpace(_plan.ClassId))
                _lessons = await _db.GetLessonsByClassAsync(_uid, _plan.InstitutionId, _plan.ClassId, _token);
            else
                _lessons = await _db.GetLessonsAllAsync(_uid, _token);

            _lessons = _lessons
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();
        }
        catch
        {
            _lessons = new();
        }
    }

    // =========================
    // PREVIEW SLOTS
    // =========================
    private void EnsureSlotMigration()
    {
        _plan.Slots ??= new();
        foreach (var s in _plan.Slots)
        {
            s.EnsureMigrated();
            s.Items ??= new();
        }
    }

    private void RebuildPreviewUi()
    {
        _plan.Slots ??= new();
        EnsureSlotMigration();

        if (_plan.Slots.Count == 0)
        {
            EmptySlotsLabel.IsVisible = true;
            SlotsList.ItemsSource = null;
            return;
        }

        EmptySlotsLabel.IsVisible = false;

        _days.Clear();

        foreach (var day in _plan.Slots.OrderBy(s => s.Date))
        {
            var vm = new SlotDayVm(day);

            day.Items ??= new();

            if (day.Items.Count == 0)
            {
                vm.Items.Add(new SlotItemPreviewVm(day, new LessonSlotItem
                {
                    StartTime = new TimeSpan(8, 0, 0),
                    EndTime = new TimeSpan(9, 0, 0),
                    LessonId = "",
                    LessonTitle = ""
                })
                {
                    IsPlaceholder = true
                });
            }
            else
            {
                foreach (var it in day.Items.OrderBy(i => i.StartTime))
                    vm.Items.Add(new SlotItemPreviewVm(day, it));
            }

            _days.Add(vm);
        }

        SlotsList.ItemsSource = null;
        SlotsList.ItemsSource = _days;
    }

    // =========================
    // UI ACTIONS
    // =========================
    private async void OnReload(object sender, EventArgs e)
    {
        await ReloadFromCloud();
        await LoadLessonsAsync();
        await ResolveAgendaLinkAsync();
        EnsureSlotMigration();
        RebuildPreviewUi();
    }

    private async void OnEdit(object sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new PlanEditorPage(_db, _store, _plan));

        await ReloadFromCloud();
        await LoadLessonsAsync();
        await ResolveAgendaLinkAsync();
        EnsureSlotMigration();
        RebuildPreviewUi();
    }

    private async void OnBack(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnOpenSlotItemLesson(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.CommandParameter is not SlotItemPreviewVm vm) return;

        if (vm.IsPlaceholder || string.IsNullOrWhiteSpace(vm.Item.LessonId))
        {
            await DisplayAlert("Info", "Nenhuma aula vinculada neste horário.", "OK");
            return;
        }

        var lesson = _lessons.FirstOrDefault(x => x.Id == vm.Item.LessonId);
        if (lesson == null)
        {
            await DisplayAlert("Info", "Aula não encontrada. Clique em “Atualizar”.", "OK");
            return;
        }

        await Navigation.PushModalAsync(new LessonEditorPage(_db, _store, _storage, lesson));

        await LoadLessonsAsync();
        await ReloadFromCloud();
        await ResolveAgendaLinkAsync();
        EnsureSlotMigration();
        RebuildPreviewUi();
    }

    // =========================
    // VMs
    // =========================
    private sealed class SlotDayVm
    {
        public LessonSlot Slot { get; }
        public string DateLabel { get; }
        public List<SlotItemPreviewVm> Items { get; } = new();

        public SlotDayVm(LessonSlot slot)
        {
            Slot = slot;
            var d = slot.Date.Date;
            DateLabel = $"{d:dd/MM/yyyy} • {d:dddd}";
        }
    }

    private sealed class SlotItemPreviewVm
    {
        public LessonSlot Slot { get; }
        public LessonSlotItem Item { get; }

        public bool IsPlaceholder { get; set; }

        public string TimeText => $"{Item.StartTime:hh\\:mm} → {Item.EndTime:hh\\:mm}";

        public string LessonTitleText
            => string.IsNullOrWhiteSpace(Item.LessonTitle) ? "(Sem aula)" : Item.LessonTitle.Trim();

        public string StatusText
        {
            get
            {
                if (IsPlaceholder) return "Nenhuma aula cadastrada neste dia.";
                if (string.IsNullOrWhiteSpace(Item.LessonId)) return "Vazio (sem vínculo).";
                return "Vinculada ao plano.";
            }
        }

        public SlotItemPreviewVm(LessonSlot slot, LessonSlotItem item)
        {
            Slot = slot;
            Item = item;
        }
    }
}
