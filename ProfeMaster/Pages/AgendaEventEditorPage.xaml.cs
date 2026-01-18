using System.ComponentModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class AgendaEventEditorPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    private ScheduleEvent _ev;
    private readonly string _modeLabel;

    private string _uid = "";
    private string _token = "";

    // caches p/ vínculo
    private List<Lesson> _lessons = new();
    private List<LessonPlan> _plans = new();
    private List<EventItem> _events = new();
    private List<ExamItem> _exams = new();

    private const string NoneLabel = "(Sem vínculo)";

    private readonly List<string> _kinds = new()
    {
        "Aula",
        "Plano de aula",
        "Evento",
        "Prova"
    };

    private bool _isLoadingUi = true;

    public AgendaEventEditorPage(LocalStore store, FirebaseDbService db, ScheduleEvent ev, string modeLabel)
    {
        InitializeComponent();

        _store = store;
        _db = db;
        _ev = ev;
        _modeLabel = modeLabel;

        Title = string.IsNullOrWhiteSpace(ev?.Id) ? "Novo item da Agenda" : "Editar item da Agenda";

        KindPicker.ItemsSource = _kinds;

        RenderBaseFields();
        ApplyKindToUi();

        _isLoadingUi = false;
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

        await LoadLinkSourcesAsync();
        BuildLinkPickers();

        RenderBaseFields();
        ApplyKindToUi();
    }

    // =========================
    // Load data for link pickers
    // =========================
    private async Task LoadLinkSourcesAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_ev.InstitutionId) && !string.IsNullOrWhiteSpace(_ev.ClassId))
                _lessons = await _db.GetLessonsByClassAsync(_uid, _ev.InstitutionId, _ev.ClassId, _token);
            else
                _lessons = await _db.GetLessonsAllAsync(_uid, _token);

            _lessons = _lessons
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();
        }
        catch { _lessons = new(); }

        try
        {
            if (!string.IsNullOrWhiteSpace(_ev.InstitutionId) && !string.IsNullOrWhiteSpace(_ev.ClassId))
                _plans = await _db.GetPlansByClassAsync(_uid, _ev.InstitutionId, _ev.ClassId, _token);
            else
                _plans = await _db.GetPlansAllAsync(_uid, _token);

            _plans = _plans
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Date)
                .ToList();
        }
        catch { _plans = new(); }

        try
        {
            _events = await _db.GetEventsAllAsync(_uid, _token);
            _events = _events
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
        catch { _events = new(); }

        try
        {
            _exams = await _db.GetExamsAllAsync(_uid, _token);
            _exams = _exams
                .OrderByDescending(x => TryGetExamDate(x) ?? DateTime.MinValue)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();
        }
        catch { _exams = new(); }
    }

    private void BuildLinkPickers()
    {
        _isLoadingUi = true;
        try
        {
            LessonPicker.ItemsSource = new List<string> { NoneLabel }
                .Concat(_lessons.Select(l => string.IsNullOrWhiteSpace(l.Title) ? "(Sem título)" : l.Title.Trim()))
                .ToList();

            PlanPicker.ItemsSource = new List<string> { NoneLabel }
                .Concat(_plans.Select(p => string.IsNullOrWhiteSpace(p.Title) ? "(Sem título)" : p.Title.Trim()))
                .ToList();

            EventPicker.ItemsSource = new List<string> { NoneLabel }
                .Concat(_events.Select(e => string.IsNullOrWhiteSpace(e.Title) ? "(Sem título)" : e.Title.Trim()))
                .ToList();

            ExamPicker.ItemsSource = new List<string> { NoneLabel }
                .Concat(_exams.Select(e => string.IsNullOrWhiteSpace(e.Title) ? "(Sem título)" : e.Title.Trim()))
                .ToList();

            SelectLinkedOnPickers();
        }
        finally
        {
            _isLoadingUi = false;
        }
    }

    private void SelectLinkedOnPickers()
    {
        LessonPicker.SelectedIndex = 0;
        PlanPicker.SelectedIndex = 0;
        EventPicker.SelectedIndex = 0;
        ExamPicker.SelectedIndex = 0;

        if (string.IsNullOrWhiteSpace(_ev.LinkedKind) || string.IsNullOrWhiteSpace(_ev.LinkedId))
            return;

        if (_ev.LinkedKind == "Aula")
        {
            var idx = _lessons.FindIndex(x => x.Id == _ev.LinkedId);
            LessonPicker.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        else if (_ev.LinkedKind == "Plano")
        {
            var idx = _plans.FindIndex(x => x.Id == _ev.LinkedId);
            PlanPicker.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        else if (_ev.LinkedKind == "Evento")
        {
            var idx = _events.FindIndex(x => x.Id == _ev.LinkedId);
            EventPicker.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        else if (_ev.LinkedKind == "Prova")
        {
            var idx = _exams.FindIndex(x => x.Id == _ev.LinkedId);
            ExamPicker.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
    }

    // =========================
    // Render base fields
    // =========================
    private void RenderBaseFields()
    {
        if (string.IsNullOrWhiteSpace(_ev.Kind))
            _ev.Kind = string.IsNullOrWhiteSpace(_ev.Type) ? "Aula" : _ev.Type.Trim();

        KindPicker.SelectedIndex = KindToIndex(_ev.Kind);

        TitleEntry.Text = _ev.Title ?? "";
        DescEditor.Text = _ev.Description ?? "";

        var start = _ev.Start == default ? DateTime.Now : _ev.Start;
        var end = _ev.End == default ? start.AddMinutes(50) : _ev.End;
        if (end < start) end = start.AddMinutes(50);

        StartDatePicker.Date = start.Date;
        EndDatePicker.Date = end.Date;
        StartTimePicker.Time = start.TimeOfDay;
        EndTimePicker.Time = end.TimeOfDay;

        NormalizeRangeByKind(CurrentKindNormalized());

        BtnClearLink.IsVisible = !string.IsNullOrWhiteSpace(_ev.LinkedId);
        SyncKindVisual();
    }

    private static int KindToIndex(string kind)
        => kind switch
        {
            "Aula" => 0,
            "Plano" => 1,
            "Plano de aula" => 1,
            "Evento" => 2,
            "Prova" => 3,
            _ => 0
        };

    private void ApplyKindToUi()
    {
        var kind = CurrentKindNormalized();

        LinkLessonBox.IsVisible = kind == "Aula";
        LinkPlanBox.IsVisible = kind == "Plano";
        LinkEventBox.IsVisible = kind == "Evento";
        LinkExamBox.IsVisible = kind == "Prova";

        BtnClearLink.IsVisible = !string.IsNullOrWhiteSpace(_ev.LinkedId);

        RangeHintLabel.Text = kind switch
        {
            "Plano" => "Plano de aula: o intervalo pode ocupar vários dias. Selecione um plano para preencher automaticamente.",
            "Evento" => "Evento: defina início e fim (pode ser vários dias). Ao vincular um evento existente, o intervalo é preenchido automaticamente.",
            "Prova" => "Prova: normalmente é um único dia. Ajuste o horário se quiser.",
            _ => "Aula: normalmente é um único dia. Ajuste o horário e descrição se quiser."
        };

        // <<< ESSENCIAL: mantém chip e combobox visual sincronizados
        SyncKindVisual();
    }


    private string CurrentKindNormalized()
    {
        var idx = KindPicker.SelectedIndex;
        var label = (idx >= 0 && idx < _kinds.Count) ? _kinds[idx] : "Aula";
        return label switch
        {
            "Plano de aula" => "Plano",
            _ => label
        };
    }

    // =========================
    // Kind changed
    // =========================
    private void OnKindChanged(object sender, EventArgs e)
    {
        if (_isLoadingUi) return;

        var kind = CurrentKindNormalized();
        _ev.Kind = kind;

        _ev.LinkedKind = "";
        _ev.LinkedId = "";
        _ev.LinkedTitle = "";

        BtnClearLink.IsVisible = false;

        NormalizeRangeByKind(kind);

        // ApplyKindToUi já chama SyncKindVisual()
        ApplyKindToUi();
    }


    private void NormalizeRangeByKind(string kind)
    {
        // força coerência de datas/horas baseado no tipo
        var start = ComposeStart();
        var end = ComposeEnd();

        if (end < start)
            end = start.AddMinutes(50);

        if (kind is "Aula" or "Prova")
        {
            // força 1 dia
            EndDatePicker.Date = StartDatePicker.Date;
            if (end.Date != start.Date)
                end = start.Date.Add(end.TimeOfDay);

            if (end < start)
                end = start.AddMinutes(50);

            EndTimePicker.Time = end.TimeOfDay;
        }
        else
        {
            // multi-dia permitido, apenas garante end >= start
            if (ComposeEnd() < ComposeStart())
            {
                EndDatePicker.Date = StartDatePicker.Date;
                EndTimePicker.Time = StartTimePicker.Time.Add(TimeSpan.FromMinutes(50));
            }
        }
    }

    // =========================
    // Link changed handlers
    // =========================
    private void OnLessonLinkedChanged(object sender, EventArgs e)
    {
        if (_isLoadingUi) return;

        if (LessonPicker.SelectedIndex <= 0)
        {
            ClearLinked();
            return;
        }

        var lesson = _lessons[LessonPicker.SelectedIndex - 1];
        _ev.LinkedKind = "Aula";
        _ev.LinkedId = lesson.Id;
        _ev.LinkedTitle = lesson.Title ?? "";

        if (string.IsNullOrWhiteSpace(TitleEntry.Text))
            TitleEntry.Text = string.IsNullOrWhiteSpace(lesson.Title) ? "Aula" : lesson.Title.Trim();

        BtnClearLink.IsVisible = true;
    }

    private void OnPlanLinkedChanged(object sender, EventArgs e)
    {
        if (_isLoadingUi) return;

        if (PlanPicker.SelectedIndex <= 0)
        {
            ClearLinked();
            return;
        }

        var plan = _plans[PlanPicker.SelectedIndex - 1];

        _ev.LinkedKind = "Plano";
        _ev.LinkedId = plan.Id;
        _ev.LinkedTitle = plan.Title ?? "";

        if (string.IsNullOrWhiteSpace(TitleEntry.Text))
            TitleEntry.Text = string.IsNullOrWhiteSpace(plan.Title) ? "Plano de aula" : plan.Title.Trim();

        var start = (plan.StartDate != default ? plan.StartDate.Date : plan.Date.Date);
        var end = (plan.EndDate != default ? plan.EndDate.Date : plan.Date.Date);
        if (end < start) end = start;

        var startTime = StartTimePicker.Time == default ? new TimeSpan(8, 0, 0) : StartTimePicker.Time;
        var endTime = EndTimePicker.Time == default ? new TimeSpan(18, 0, 0) : EndTimePicker.Time;

        StartDatePicker.Date = start;
        EndDatePicker.Date = end;
        StartTimePicker.Time = startTime;
        EndTimePicker.Time = endTime;

        // garante multi-dia permitido
        if (CurrentKindNormalized() != "Plano")
        {
            KindPicker.SelectedIndex = KindToIndex("Plano");
            _ev.Kind = "Plano";
            ApplyKindToUi();
        }

        BtnClearLink.IsVisible = true;
    }

    private void OnEventLinkedChanged(object sender, EventArgs e)
    {
        if (_isLoadingUi) return;

        if (EventPicker.SelectedIndex <= 0)
        {
            ClearLinked();
            return;
        }

        var item = _events[EventPicker.SelectedIndex - 1];

        _ev.LinkedKind = "Evento";
        _ev.LinkedId = item.Id;
        _ev.LinkedTitle = item.Title ?? "";

        if (string.IsNullOrWhiteSpace(TitleEntry.Text))
            TitleEntry.Text = string.IsNullOrWhiteSpace(item.Title) ? "Evento" : item.Title.Trim();

        if (string.IsNullOrWhiteSpace(DescEditor.Text))
            DescEditor.Text = item.Description ?? "";

        // ===== NOVO: preenche intervalo (multi-dia) se existir no EventItem =====
        var (s, en) = TryGetEventRange(item);

        if (s.HasValue)
        {
            var startDate = s.Value.Date;
            var endDate = en?.Date ?? startDate;
            if (endDate < startDate) endDate = startDate;

            var startTime = StartTimePicker.Time == default ? new TimeSpan(8, 0, 0) : StartTimePicker.Time;
            var endTime = EndTimePicker.Time == default ? new TimeSpan(18, 0, 0) : EndTimePicker.Time;

            StartDatePicker.Date = startDate;
            EndDatePicker.Date = endDate;
            StartTimePicker.Time = startTime;
            EndTimePicker.Time = endTime;
        }

        // garante que está em "Evento"
        if (CurrentKindNormalized() != "Evento")
        {
            KindPicker.SelectedIndex = KindToIndex("Evento");
            _ev.Kind = "Evento";
            ApplyKindToUi();
        }

        NormalizeRangeByKind("Evento");

        BtnClearLink.IsVisible = true;
    }

    private void OnExamLinkedChanged(object sender, EventArgs e)
    {
        if (_isLoadingUi) return;

        if (ExamPicker.SelectedIndex <= 0)
        {
            ClearLinked();
            return;
        }

        var exam = _exams[ExamPicker.SelectedIndex - 1];

        _ev.LinkedKind = "Prova";
        _ev.LinkedId = exam.Id;
        _ev.LinkedTitle = exam.Title ?? "";

        if (string.IsNullOrWhiteSpace(TitleEntry.Text))
            TitleEntry.Text = string.IsNullOrWhiteSpace(exam.Title) ? "Prova" : exam.Title.Trim();

        var d = TryGetExamDate(exam);
        if (d.HasValue)
        {
            StartDatePicker.Date = d.Value.Date;
            EndDatePicker.Date = d.Value.Date;

            if (StartTimePicker.Time == default) StartTimePicker.Time = new TimeSpan(8, 0, 0);
            if (EndTimePicker.Time == default) EndTimePicker.Time = new TimeSpan(9, 0, 0);
        }

        // garante 1 dia
        if (CurrentKindNormalized() != "Prova")
        {
            KindPicker.SelectedIndex = KindToIndex("Prova");
            _ev.Kind = "Prova";
            ApplyKindToUi();
        }

        NormalizeRangeByKind("Prova");

        BtnClearLink.IsVisible = true;
    }

    private void OnClearLinked(object sender, EventArgs e)
    {
        ClearLinked();
        SelectLinkedOnPickers();
        BtnClearLink.IsVisible = false;
    }

    private void ClearLinked()
    {
        _ev.LinkedKind = "";
        _ev.LinkedId = "";
        _ev.LinkedTitle = "";
        BtnClearLink.IsVisible = false;
    }

    // =========================
    // date/time consistency
    // =========================
    private void OnAnyDateChanged(object sender, DateChangedEventArgs e)
    {
        if (_isLoadingUi) return;
        NormalizeRangeByKind(CurrentKindNormalized());
    }

    private void OnAnyTimeChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingUi) return;
        if (e.PropertyName != nameof(TimePicker.Time)) return;
        NormalizeRangeByKind(CurrentKindNormalized());
    }

    private DateTime ComposeStart()
        => StartDatePicker.Date.Date.Add(StartTimePicker.Time);

    private DateTime ComposeEnd()
        => EndDatePicker.Date.Date.Add(EndTimePicker.Time);

    // =========================
    // Save/Cancel
    // =========================
    private async void OnCancel(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    private async void OnSave(object sender, EventArgs e)
    {
        var title = (TitleEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Erro", "Informe o nome do item.", "OK");
            return;
        }

        var kind = CurrentKindNormalized();
        _ev.Kind = kind;

        // se você ainda usa Type em algum lugar, mantém coerente
        _ev.Type = kind switch
        {
            "Plano" => "Plano",
            _ => kind
        };

        _ev.Title = title;
        _ev.Description = (DescEditor.Text ?? "").Trim();

        NormalizeRangeByKind(kind);

        var start = ComposeStart();
        var end = ComposeEnd();
        if (end < start) end = start.AddMinutes(50);

        // Aula/Prova sempre 1 dia (Normalize já faz, mas reforço)
        if (kind is "Aula" or "Prova")
            end = start.Date.Add(end.TimeOfDay);

        if (end < start) end = start.AddMinutes(50);

        _ev.Start = start;
        _ev.End = end;

        _ev.Id = string.IsNullOrWhiteSpace(_ev.Id) ? Guid.NewGuid().ToString("N") : _ev.Id;

        try
        {
            await _db.UpsertAgendaAllAsync(_uid, _token, _ev);

            if (!string.IsNullOrWhiteSpace(_ev.InstitutionId) && !string.IsNullOrWhiteSpace(_ev.ClassId))
                await _db.UpsertAgendaByClassAsync(_uid, _ev.InstitutionId, _ev.ClassId, _token, _ev);

            await Navigation.PopModalAsync();
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível salvar o item da agenda.", "OK");
        }
    }

    // =========================
    // Helpers: dates from ExamItem/EventItem (reflection-safe)
    // =========================
    private static DateTime? TryGetExamDate(ExamItem exam)
    {
        try
        {
            var t = exam.GetType();

            var pDate = t.GetProperty("Date");
            if (pDate != null && pDate.PropertyType == typeof(DateTime))
                return (DateTime)pDate.GetValue(exam)!;

            var pStart = t.GetProperty("Start");
            if (pStart != null && pStart.PropertyType == typeof(DateTime))
                return (DateTime)pStart.GetValue(exam)!;

            var pStartDate = t.GetProperty("StartDate");
            if (pStartDate != null && pStartDate.PropertyType == typeof(DateTime))
                return (DateTime)pStartDate.GetValue(exam)!;
        }
        catch { }

        return null;
    }

    private static (DateTime? Start, DateTime? End) TryGetEventRange(EventItem item)
    {
        try
        {
            var t = item.GetType();

            DateTime? start = null;
            DateTime? end = null;

            // Start / End
            var pStart = t.GetProperty("Start");
            if (pStart != null && pStart.PropertyType == typeof(DateTime))
                start = (DateTime)pStart.GetValue(item)!;

            var pEnd = t.GetProperty("End");
            if (pEnd != null && pEnd.PropertyType == typeof(DateTime))
                end = (DateTime)pEnd.GetValue(item)!;

            // StartDate / EndDate
            if (!start.HasValue)
            {
                var pStartDate = t.GetProperty("StartDate");
                if (pStartDate != null && pStartDate.PropertyType == typeof(DateTime))
                    start = (DateTime)pStartDate.GetValue(item)!;
            }

            if (!end.HasValue)
            {
                var pEndDate = t.GetProperty("EndDate");
                if (pEndDate != null && pEndDate.PropertyType == typeof(DateTime))
                    end = (DateTime)pEndDate.GetValue(item)!;
            }

            // Date (single)
            if (!start.HasValue)
            {
                var pDate = t.GetProperty("Date");
                if (pDate != null && pDate.PropertyType == typeof(DateTime))
                    start = (DateTime)pDate.GetValue(item)!;
            }

            // fallback end = start
            if (start.HasValue && !end.HasValue)
                end = start.Value;

            return (start, end);
        }
        catch
        {
            return (null, null);
        }
    }

    private void SyncKindVisual()
    {
        // kind NORMALIZADO (Aula, Plano, Evento, Prova)
        var kind = CurrentKindNormalized();

        // texto que aparece no app (você pediu "Plano de Aulas")
        var display = kind switch
        {
            "Plano" => "Plano de Aulas",
            _ => kind
        };

        // atualiza labels visuais
        if (KindDisplay != null) KindDisplay.Text = display;
        if (KindChipLabel != null) KindChipLabel.Text = display;

        // cor do chip (igual AgendaPage)
        if (KindChip != null)
        {
            KindChip.BackgroundColor = kind switch
            {
                "Aula" => Color.FromArgb("#8B2CE2"),
                "Plano" => Color.FromArgb("#23BFC2"),
                "Evento" => Color.FromArgb("#F04646"),
                "Prova" => Color.FromArgb("#0B8F3A"),
                _ => Color.FromArgb("#8B2CE2")
            };
        }
    }

}
