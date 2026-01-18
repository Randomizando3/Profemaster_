using ProfeMaster.Models;
using ProfeMaster.Services;

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

    public PlanDetailsPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage, LessonPlan plan)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _storage = storage;
        _plan = plan;

        RenderHeader();
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

        EnsureSlotMigration();
        RebuildPreviewUi();
    }

    // =========================
    // HEADER
    // =========================
    private void RenderHeader()
    {
        TitleLabel.Text = string.IsNullOrWhiteSpace(_plan.Title) ? "(Sem título)" : _plan.Title.Trim();

        var start = _plan.StartDate == default ? (_plan.Date == default ? DateTime.Today : _plan.Date.Date) : _plan.StartDate.Date;
        var end = _plan.EndDate == default ? start : _plan.EndDate.Date;
        if (end < start) (start, end) = (end, start);

        var classInfo = $"{_plan.InstitutionName} • {_plan.ClassName}".Trim();
        classInfo = classInfo.Trim(' ', '•');

        MetaLabel.Text = $"{start:dd/MM/yyyy} → {end:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(classInfo) ? "" : $" • {classInfo}");

        ObsLabel.Text = string.IsNullOrWhiteSpace(_plan.Observations)
            ? "Observações: -"
            : $"Observações: {_plan.Observations.Trim()}";

        // Agenda (preview apenas)
        if (string.IsNullOrWhiteSpace(_plan.LinkedEventId))
            AgendaLinkLabel.Text = "Nenhum vínculo.";
        else
            AgendaLinkLabel.Text = $"Vinculado: {_plan.LinkedEventTitle}";

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
                RenderHeader();
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

            // mantém seu padrão (Lesson tem UpdatedAt no seu projeto)
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
                // preview: mostra um item “vazio”
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
        EnsureSlotMigration();
        RebuildPreviewUi();
    }

    private async void OnEdit(object sender, EventArgs e)
    {
        // Edição centralizada no Editor (sem duplicar no details)
        await Navigation.PushModalAsync(new PlanEditorPage(_db, _store, _plan));

        // ao voltar, recarrega preview
        await ReloadFromCloud();
        await LoadLessonsAsync();
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

        // construtor correto (CS7036): (db, store, storage, lesson)
        await Navigation.PushModalAsync(new LessonEditorPage(_db, _store, _storage, lesson));

        // volta e atualiza preview (caso tenha mudado thumb/título da aula, etc.)
        await LoadLessonsAsync();
        await ReloadFromCloud();
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
