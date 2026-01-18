using ProfeMaster.Models;
using ProfeMaster.Services;
using System.ComponentModel;

namespace ProfeMaster.Pages;

public partial class PlanEditorPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;

    private LessonPlan _plan;
    private string _uid = "";
    private string _token = "";

    private readonly bool _hasClassContext;

    // ===== NOVO: controle de criação/cancelamento =====
    private readonly bool _isNew;
    private bool _hasSavedOnce = false;
    private bool _canceling = false;
    private bool _autoSaveEnabled = true;

    // Aulas carregadas para vincular
    private List<Lesson> _lessons = new();

    // ViewModels
    private readonly List<SlotDayVm> _days = new();

    public LessonPlan Result => _plan;

    // Evita gravações em cascata enquanto monta UI / carrega
    private bool _isLoadingUi = true;

    // Evita múltiplos saves simultâneos (throttle simples)
    private bool _saveQueued = false;
    private bool _saving = false;

    // ===== ALTERADO: recebe isNew =====
    public PlanEditorPage(FirebaseDbService db, LocalStore store, LessonPlan plan, bool isNew = false)
    {
        InitializeComponent();
        _db = db;
        _store = store;
        _plan = plan;

        _isNew = isNew;

        _hasClassContext = !string.IsNullOrWhiteSpace(plan.InstitutionId) && !string.IsNullOrWhiteSpace(plan.ClassId);

        TitleEntry.Text = _plan.Title ?? "";
        ObsEditor.Text = _plan.Observations ?? "";

        // thumb
        RenderThumb();

        // intervalo inicial
        var start = _plan.StartDate == default ? DateTime.Today : _plan.StartDate.Date;
        var end = _plan.EndDate == default ? start : _plan.EndDate.Date;

        // fallback do Date antigo
        if (_plan.StartDate == default && _plan.EndDate == default && _plan.Date != default)
        {
            start = _plan.Date.Date;
            end = _plan.Date.Date;
        }

        StartDatePick.Date = start;
        EndDatePick.Date = end;

        _plan.Slots ??= new();
        EnsureSlotMigration();
        BuildSlots(start, end, silent: true);
        RebuildSlotsUi();

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

        await ReloadLessonsAsync();
        RebuildSlotsUi();
    }

    // =========================
    // THUMB
    // =========================
    private void RenderThumb()
    {
        try
        {
            var localPath = _plan.ThumbLocalPath ?? "";
            var url = _plan.ThumbUrl ?? "";

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                ThumbImage.Source = ImageSource.FromFile(localPath);
                ThumbInfoLabel.Text = Path.GetFileName(localPath);
            }
            else if (!string.IsNullOrWhiteSpace(url))
            {
                ThumbImage.Source = new UriImageSource { Uri = new Uri(url), CachingEnabled = true };
                ThumbInfoLabel.Text = "Capa (online)";
            }
            else
            {
                ThumbImage.Source = null;
                ThumbInfoLabel.Text = "Nenhuma capa selecionada.";
            }
        }
        catch
        {
            ThumbImage.Source = null;
            ThumbInfoLabel.Text = "Nenhuma capa selecionada.";
        }
    }

    private async void OnPickThumbClicked(object sender, EventArgs e)
    {
        try
        {
            // PickPhoto funciona em Android/iOS; no Windows/mac pode variar.
            if (!MediaPicker.Default.IsCaptureSupported && DeviceInfo.Platform != DevicePlatform.Android && DeviceInfo.Platform != DevicePlatform.iOS)
            {
                // Mesmo assim tentamos PickPhoto (em algumas plataformas funciona)
            }

            var photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Selecione a capa do plano"
            });

            if (photo == null) return;

            // copia para AppData (não depende do path externo)
            var ext = Path.GetExtension(photo.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

            var dir = FileSystem.AppDataDirectory;
            var fileName = $"plan_thumb_{_plan.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            var dest = Path.Combine(dir, fileName);

            await using (var src = await photo.OpenReadAsync())
            await using (var dst = File.OpenWrite(dest))
            {
                await src.CopyToAsync(dst);
            }

            _plan.ThumbLocalPath = dest;
            // mantém ThumbUrl como está (sem upload nesta etapa)

            RenderThumb();
            QueueAutoSave();
        }
        catch (PermissionException)
        {
            await DisplayAlert("Permissão", "Permita acesso às fotos/arquivos para escolher a capa.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Falha ao selecionar a capa: {ex.Message}", "OK");
        }
    }

    private async void OnRemoveThumbClicked(object sender, EventArgs e)
    {
        try
        {
            var local = _plan.ThumbLocalPath ?? "";
            _plan.ThumbLocalPath = "";
            // não apaga ThumbUrl automaticamente; só remove o local
            RenderThumb();
            QueueAutoSave();

            // opcional: tenta apagar arquivo local antigo (se existir)
            if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
            {
                try { File.Delete(local); } catch { /* ignora */ }
            }
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível remover a capa.", "OK");
        }
    }

    // =========================
    // SLOTS / LESSONS
    // =========================
    private void EnsureSlotMigration()
    {
        _plan.Slots ??= new();
        foreach (var s in _plan.Slots)
        {
            s.EnsureMigrated();

            // garante pelo menos 1 item por dia, para facilitar UI
            s.Items ??= new();
            if (s.Items.Count == 0)
            {
                s.Items.Add(new LessonSlotItem
                {
                    StartTime = new TimeSpan(8, 0, 0),
                    EndTime = new TimeSpan(9, 0, 0)
                });
            }
        }
    }

    private async Task ReloadLessonsAsync()
    {
        try
        {
            var all = await _db.GetLessonsAllAsync(_uid, _token);

            if (_hasClassContext)
            {
                var filtered = all.Where(x =>
                        (x.InstitutionId ?? "") == (_plan.InstitutionId ?? "") &&
                        (x.ClassId ?? "") == (_plan.ClassId ?? ""))
                    .OrderByDescending(x => x.UpdatedAt)
                    .ThenByDescending(x => x.CreatedAt)
                    .ToList();

                _lessons = filtered.Count > 0 ? filtered : all;
            }
            else
            {
                _lessons = all;
            }

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

    private async void OnReloadLessons(object sender, EventArgs e)
    {
        await ReloadLessonsAsync();
        RebuildSlotsUi();
        await DisplayAlert("OK", "Aulas atualizadas.", "Fechar");
    }

    private void UpdateSlotsInfo()
    {
        _plan.Slots ??= new();
        SlotsInfoLabel.Text = $"Slots: {_plan.Slots.Count}";
    }

    private void BuildSlots(DateTime start, DateTime end, bool silent)
    {
        start = start.Date;
        end = end.Date;

        if (end < start) (start, end) = (end, start);

        _plan.StartDate = start;
        _plan.EndDate = end;

        _plan.Slots ??= new();
        EnsureSlotMigration();

        // preserva por data
        var map = _plan.Slots
            .GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => g.First());

        var slots = new List<LessonSlot>();

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (map.TryGetValue(d, out var existing))
            {
                existing.Date = d;
                existing.EnsureMigrated();

                // garante ao menos 1 item para UI
                existing.Items ??= new();
                if (existing.Items.Count == 0)
                {
                    existing.Items.Add(new LessonSlotItem
                    {
                        StartTime = new TimeSpan(8, 0, 0),
                        EndTime = new TimeSpan(9, 0, 0)
                    });
                }

                slots.Add(existing);
            }
            else
            {
                slots.Add(new LessonSlot
                {
                    Date = d,
                    Items = new List<LessonSlotItem>
                    {
                        new LessonSlotItem
                        {
                            StartTime = new TimeSpan(8,0,0),
                            EndTime = new TimeSpan(9,0,0)
                        }
                    }
                });
            }
        }

        _plan.Slots = slots;
        UpdateSlotsInfo();

        if (!silent)
            RebuildSlotsUi();
    }

    private void RebuildSlotsUi()
    {
        _plan.Slots ??= new();
        EnsureSlotMigration();

        UpdateSlotsInfo();

        if (_plan.Slots.Count == 0)
        {
            EmptySlotsLabel.IsVisible = true;
            SlotsList.ItemsSource = null;
            return;
        }

        EmptySlotsLabel.IsVisible = false;

        _days.Clear();

        foreach (var slot in _plan.Slots.OrderBy(s => s.Date))
        {
            var dayVm = new SlotDayVm(slot);
            dayVm.Items.Clear();

            foreach (var item in slot.Items)
            {
                var itemVm = new SlotItemVm(slot, item);
                FillLessonPicker(itemVm);
                dayVm.Items.Add(itemVm);
            }

            _days.Add(dayVm);
        }

        SlotsList.ItemsSource = null;
        SlotsList.ItemsSource = _days;
    }

    private void FillLessonPicker(SlotItemVm vm)
    {
        vm.LessonPickerItems.Clear();
        vm.LessonPickerItems.Add("(Vazio)");

        foreach (var l in _lessons)
            vm.LessonPickerItems.Add(string.IsNullOrWhiteSpace(l.Title) ? "(Sem título)" : l.Title.Trim());

        vm.LessonPickerItems.Add("+ Criar nova aula...");

        // index atual
        if (!string.IsNullOrWhiteSpace(vm.Item.LessonId))
        {
            var idx = _lessons.FindIndex(x => x.Id == vm.Item.LessonId);
            vm.LessonPickerSelectedIndex = idx >= 0 ? idx + 1 : 0; // +1 por causa do vazio
        }
        else
        {
            vm.LessonPickerSelectedIndex = 0;
        }

        vm.RefreshStatus();
    }

    // ===== Eventos do intervalo =====
    private void OnDatesChanged(object sender, DateChangedEventArgs e)
    {
        if (_isLoadingUi) return;

        BuildSlots(StartDatePick.Date, EndDatePick.Date, silent: false);
        QueueAutoSave();
    }

    // ===== Adicionar item (aula) dentro do dia =====
    private async void OnAddSlotItem(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.CommandParameter is not SlotDayVm dayVm) return;

        var newItem = new LessonSlotItem
        {
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(9, 0, 0)
        };

        dayVm.Slot.Items ??= new();
        dayVm.Slot.Items.Add(newItem);

        var itemVm = new SlotItemVm(dayVm.Slot, newItem);
        FillLessonPicker(itemVm);
        dayVm.Items.Add(itemVm);

        RebuildSlotsUi();
        QueueAutoSave();
    }

    // ===== Picker mudou =====
    private async void OnSlotItemLessonChanged(object sender, EventArgs e)
    {
        if (_isLoadingUi) return;

        if (sender is not Picker p) return;
        if (p.BindingContext is not SlotItemVm vm) return;

        var idx = vm.LessonPickerSelectedIndex;

        // criar nova aula
        if (idx == vm.LessonPickerItems.Count - 1)
        {
            vm.LessonPickerSelectedIndex = 0;
            vm.Item.LessonId = "";
            vm.Item.LessonTitle = "";
            vm.RefreshStatus();

            QueueAutoSave();
            await Shell.Current.GoToAsync("lessons");
            return;
        }

        // vazio
        if (idx <= 0)
        {
            vm.Item.LessonId = "";
            vm.Item.LessonTitle = "";
            vm.RefreshStatus();

            QueueAutoSave();
            return;
        }

        // aula real
        var lesson = _lessons[idx - 1];
        vm.Item.LessonId = lesson.Id;
        vm.Item.LessonTitle = lesson.Title ?? "";
        vm.RefreshStatus();

        QueueAutoSave();
    }

    private async void OnSlotItemTimeChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingUi) return;
        if (e.PropertyName != nameof(TimePicker.Time)) return;

        foreach (var day in _plan.Slots)
        {
            foreach (var item in day.Items)
            {
                if (item.EndTime <= item.StartTime)
                    item.EndTime = item.StartTime.Add(TimeSpan.FromMinutes(50));
            }
        }

        QueueAutoSave();
    }

    // ===== Remover item =====
    private async void OnRemoveSlotItem(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.CommandParameter is not SlotItemVm vm) return;

        vm.Slot.Items ??= new();
        vm.Slot.Items.Remove(vm.Item);

        if (vm.Slot.Items.Count == 0)
        {
            vm.Slot.Items.Add(new LessonSlotItem
            {
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(9, 0, 0)
            });
        }

        RebuildSlotsUi();
        QueueAutoSave();
    }

    // ===== Abrir aula =====
    private async void OnOpenSlotItemLesson(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.CommandParameter is not SlotItemVm vm) return;

        if (string.IsNullOrWhiteSpace(vm.Item.LessonId))
        {
            await DisplayAlert("Info", "Nenhuma aula selecionada neste item.", "OK");
            return;
        }

        var lesson = _lessons.FirstOrDefault(x => x.Id == vm.Item.LessonId);
        if (lesson == null)
        {
            await DisplayAlert("Info", "Aula não encontrada. Clique em “Atualizar aulas”.", "OK");
            return;
        }

        var storage = Shell.Current?.Handler?.MauiContext?.Services.GetService<FirebaseStorageService>();
        if (storage == null)
        {
            await DisplayAlert("Erro", "FirebaseStorageService não está registrado no DI.", "OK");
            return;
        }

        await Navigation.PushModalAsync(new LessonEditorPage(_db, _store, storage, lesson));

        await ReloadLessonsAsync();
        RebuildSlotsUi();
    }

    private async Task PersistPlanAsync()
    {
        if (_canceling) return;
        if (string.IsNullOrWhiteSpace(_uid) || string.IsNullOrWhiteSpace(_token))
            return;

        _plan.Title = (TitleEntry.Text ?? "").Trim();
        _plan.Observations = (ObsEditor.Text ?? "").Trim();

        _plan.StartDate = StartDatePick.Date.Date;
        _plan.EndDate = EndDatePick.Date.Date;
        if (_plan.EndDate < _plan.StartDate) _plan.EndDate = _plan.StartDate;

        _plan.Date = _plan.StartDate;

        await _db.UpsertPlanAllAsync(_uid, _token, _plan);

        if (_hasClassContext)
            await _db.UpsertPlanByClassAsync(_uid, _plan.InstitutionId, _plan.ClassId, _token, _plan);

        _hasSavedOnce = true;
    }

    private void QueueAutoSave()
    {
        if (_isLoadingUi) return;
        if (_canceling) return;
        if (!_autoSaveEnabled) return;

        _saveQueued = true;
        _ = RunAutoSaveLoop();
    }

    private async Task RunAutoSaveLoop()
    {
        if (_saving) return;

        _saving = true;
        try
        {
            while (_saveQueued)
            {
                if (_canceling) break;

                _saveQueued = false;
                await Task.Delay(180);

                if (_canceling) break;
                await PersistPlanAsync();
            }
        }
        finally
        {
            _saving = false;
        }
    }

    private async void OnCancel(object sender, EventArgs e)
    {
        _canceling = true;
        _autoSaveEnabled = false;
        _saveQueued = false;

        // Se estava criando e já chegou a salvar alguma vez, remove o registro
        if (_isNew && _hasSavedOnce && !string.IsNullOrWhiteSpace(_uid) && !string.IsNullOrWhiteSpace(_token))
        {
            try
            {
                await _db.DeletePlanAllAsync(_uid, _token, _plan.Id);

                if (!string.IsNullOrWhiteSpace(_plan.InstitutionId) && !string.IsNullOrWhiteSpace(_plan.ClassId))
                    await _db.DeletePlanByClassAsync(_uid, _plan.InstitutionId, _plan.ClassId, _token, _plan.Id);
            }
            catch
            {
                // ignora falhas de delete no cancel
            }
        }

        await Navigation.PopModalAsync();
    }

    private async void OnSave(object sender, EventArgs e)
    {
        var title = (TitleEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Erro", "Informe o título do plano.", "OK");
            return;
        }

        _autoSaveEnabled = false;
        _saveQueued = false;

        await PersistPlanAsync();
        await Navigation.PopModalAsync();
    }

    // =========================
    // VMs internos (mínimos)
    // =========================
    private sealed class SlotDayVm
    {
        public LessonSlot Slot { get; }
        public string DateLabel { get; }

        public List<SlotItemVm> Items { get; } = new();

        public SlotDayVm(LessonSlot slot)
        {
            Slot = slot;
            var d = slot.Date.Date;
            DateLabel = $"{d:dd/MM/yyyy} • {d:dddd}";
        }
    }

    private sealed class SlotItemVm
    {
        public LessonSlot Slot { get; }
        public LessonSlotItem Item { get; }

        public List<string> LessonPickerItems { get; } = new();
        public int LessonPickerSelectedIndex { get; set; } = 0;

        public TimeSpan StartTime
        {
            get => Item.StartTime;
            set => Item.StartTime = value;
        }

        public TimeSpan EndTime
        {
            get => Item.EndTime;
            set => Item.EndTime = value;
        }

        public string StatusLabel { get; private set; } = "";

        public SlotItemVm(LessonSlot slot, LessonSlotItem item)
        {
            Slot = slot;
            Item = item;
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            var title = string.IsNullOrWhiteSpace(Item.LessonTitle) ? "(Sem aula)" : Item.LessonTitle.Trim();
            StatusLabel = $"Selecionada: {title} • {Item.StartTime:hh\\:mm} → {Item.EndTime:hh\\:mm}";
        }
    }
}
