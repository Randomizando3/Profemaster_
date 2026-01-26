// Pages/EventEditorPage.xaml.cs
using System.ComponentModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class EventEditorPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;

    private EventItem _event;
    private string _uid = "";
    private string _token = "";

    private bool _isLoadingUi = true;
    private bool _saveQueued = false;
    private bool _saving = false;

    private readonly List<EventDayVm> _days = new();

    public EventEditorPage(FirebaseDbService db, LocalStore store, EventItem item)
    {
        InitializeComponent();
        _db = db;
        _store = store;
        _event = item;

        TitleEntry.Text = _event.Title ?? "";
        DescEditor.Text = _event.Description ?? "";

        var start = _event.StartDate == default ? DateTime.Today : _event.StartDate.Date;
        var end = _event.EndDate == default ? start : _event.EndDate.Date;
        if (end < start) end = start;

        StartDatePick.Date = start;
        EndDatePick.Date = end;

        _event.Slots ??= new();
        _event.EnsureSlotsMigrated();

        BuildSlots(start, end, silent: true);
        RebuildSlotsUi();

        // ? capa
        RefreshThumbPreview();

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
    }

    // =========================
    // ? CAPA (ThumbLocalPath / ThumbUrl)
    // =========================
    private void RefreshThumbPreview()
    {
        try
        {
            var local = (_event.ThumbLocalPath ?? "").Trim();
            var url = (_event.ThumbUrl ?? "").Trim();

            ImageSource? src = null;

            if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
                src = ImageSource.FromFile(local);
            else if (!string.IsNullOrWhiteSpace(url))
                src = ImageSource.FromUri(new Uri(url));

            ThumbPreview.Source = src;
            ThumbPlaceholder.IsVisible = (src == null);
        }
        catch
        {
            ThumbPreview.Source = null;
            ThumbPlaceholder.IsVisible = true;
        }
    }

    private async void OnPickThumb(object sender, EventArgs e)
    {
        try
        {
            // Preferir galeria, simples e funciona bem em Android/Windows
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Selecione uma capa"
            });

            if (result == null)
                return;

            // Copia para AppDataDirectory para garantir acesso posterior
            var ext = Path.GetExtension(result.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

            var fileName = $"event_thumb_{_event.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            var destPath = Path.Combine(FileSystem.AppDataDirectory, fileName);

            await using (var srcStream = await result.OpenReadAsync())
            await using (var destStream = File.Open(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await srcStream.CopyToAsync(destStream);
            }

            _event.ThumbLocalPath = destPath;

            // Se você já tiver ThumbUrl de algum fluxo externo, mantém.
            RefreshThumbPreview();

            // salva automático (sem exigir fechar)
            QueueAutoSave();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Não foi possível selecionar a capa.\n{ex.Message}", "OK");
        }
    }

    private async void OnRemoveThumb(object sender, EventArgs e)
    {
        try
        {
            // remove referência
            var old = (_event.ThumbLocalPath ?? "").Trim();
            _event.ThumbLocalPath = "";
            _event.ThumbUrl = "";

            RefreshThumbPreview();
            QueueAutoSave();

            // opcional: tentar apagar arquivo local
            if (!string.IsNullOrWhiteSpace(old) && File.Exists(old))
            {
                try { File.Delete(old); } catch { /* ignora */ }
            }
        }
        catch
        {
            // sem alert para não incomodar
        }
    }

    private void UpdateSlotsInfo()
    {
        _event.Slots ??= new();
        SlotsInfoLabel.Text = $"Dias: {_event.Slots.Count}";
    }

    private void BuildSlots(DateTime start, DateTime end, bool silent)
    {
        start = start.Date;
        end = end.Date;
        if (end < start) (start, end) = (end, start);

        _event.StartDate = start;
        _event.EndDate = end;

        _event.Slots ??= new();
        _event.EnsureSlotsMigrated();

        // preserva por data
        var map = _event.Slots
            .GroupBy(s => s.Date.Date)
            .ToDictionary(g => g.Key, g => g.First());

        var slots = new List<EventDaySlot>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (map.TryGetValue(d, out var existing))
            {
                existing.Date = d;
                existing.Items ??= new();
                if (existing.Items.Count == 0)
                {
                    existing.Items.Add(new EventAttractionItem
                    {
                        StartTime = new TimeSpan(8, 0, 0),
                        EndTime = new TimeSpan(9, 0, 0),
                        Title = ""
                    });
                }
                slots.Add(existing);
            }
            else
            {
                slots.Add(new EventDaySlot
                {
                    Date = d,
                    Items = new List<EventAttractionItem>
                    {
                        new EventAttractionItem
                        {
                            StartTime = new TimeSpan(8,0,0),
                            EndTime = new TimeSpan(9,0,0),
                            Title = ""
                        }
                    }
                });
            }
        }

        _event.Slots = slots;
        UpdateSlotsInfo();

        if (!silent)
            RebuildSlotsUi();
    }

    private void RebuildSlotsUi()
    {
        _event.Slots ??= new();
        _event.EnsureSlotsMigrated();
        UpdateSlotsInfo();

        if (_event.Slots.Count == 0)
        {
            EmptySlotsLabel.IsVisible = true;
            SlotsList.ItemsSource = null;
            return;
        }

        EmptySlotsLabel.IsVisible = false;
        _days.Clear();

        foreach (var slot in _event.Slots.OrderBy(s => s.Date))
            _days.Add(new EventDayVm(slot));

        SlotsList.ItemsSource = null;
        SlotsList.ItemsSource = _days;
    }

    // ===== Período mudou =====
    private void OnDatesChanged(object sender, DateChangedEventArgs e)
    {
        if (_isLoadingUi) return;

        BuildSlots(StartDatePick.Date, EndDatePick.Date, silent: false);
        QueueAutoSave();
    }

    // ===== Add/Remove atração =====
    private void OnAddAttraction(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.CommandParameter is not EventDayVm dayVm) return;

        dayVm.Slot.Items ??= new();
        dayVm.Slot.Items.Add(new EventAttractionItem
        {
            Title = "",
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(9, 0, 0)
        });

        RebuildSlotsUi();
        QueueAutoSave();
    }

    private void OnRemoveAttraction(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.CommandParameter is not EventAttractionVm vm) return;

        vm.Day.Slot.Items ??= new();
        vm.Day.Slot.Items.Remove(vm.Item);

        // mantém pelo menos 1 item por dia
        if (vm.Day.Slot.Items.Count == 0)
        {
            vm.Day.Slot.Items.Add(new EventAttractionItem
            {
                Title = "",
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(9, 0, 0)
            });
        }

        RebuildSlotsUi();
        QueueAutoSave();
    }

    // ===== Texto / Hora mudou =====
    private void OnAttractionTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingUi) return;
        QueueAutoSave();
    }

    private void OnAttractionTimeChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingUi) return;
        if (e.PropertyName != nameof(TimePicker.Time)) return;

        // normaliza end >= start
        foreach (var d in _event.Slots)
        {
            foreach (var it in d.Items)
            {
                if (it.EndTime <= it.StartTime)
                    it.EndTime = it.StartTime.Add(TimeSpan.FromMinutes(50));
            }
        }

        // garante atualização visual no Android (label hh:mm)
        RebuildSlotsUi();

        QueueAutoSave();
    }

    private async Task PersistAsync()
    {
        if (string.IsNullOrWhiteSpace(_uid) || string.IsNullOrWhiteSpace(_token))
            return;

        _event.Title = (TitleEntry.Text ?? "").Trim();
        _event.Description = (DescEditor.Text ?? "").Trim();

        _event.StartDate = StartDatePick.Date.Date;
        _event.EndDate = EndDatePick.Date.Date;
        if (_event.EndDate < _event.StartDate) _event.EndDate = _event.StartDate;

        _event.Slots ??= new();
        _event.EnsureSlotsMigrated();

        // ? ThumbLocalPath / ThumbUrl já estão no _event e vão junto no Upsert
        await _db.UpsertEventAsync(_uid, _token, _event);
    }

    private void QueueAutoSave()
    {
        if (_isLoadingUi) return;

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
                _saveQueued = false;
                await Task.Delay(200);
                await PersistAsync();
            }
        }
        finally
        {
            _saving = false;
        }
    }

    private async void OnCancel(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    // Botão X (topo) fecha sem salvar (mantido conforme seu último ajuste)
    private async void OnClose(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    // Botão gradiente "Fechar" salva e fecha
    private async void OnClosePrimary(object sender, EventArgs e)
    {
        var title = (TitleEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Erro", "Informe o nome do evento.", "OK");
            return;
        }

        await PersistAsync();
        await Navigation.PopModalAsync();
    }

    // ===== VMs =====
    private sealed class EventDayVm
    {
        public EventDaySlot Slot { get; }
        public string DateLabel { get; }

        public List<EventAttractionVm> Items { get; } = new();

        public EventDayVm(EventDaySlot slot)
        {
            Slot = slot;
            var d = slot.Date.Date;
            DateLabel = $"{d:dd/MM/yyyy} • {d:dddd}";

            slot.Items ??= new();
            foreach (var it in slot.Items)
                Items.Add(new EventAttractionVm(this, it));
        }
    }

    private sealed class EventAttractionVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public EventDayVm Day { get; }
        public EventAttractionItem Item { get; }

        public string Title
        {
            get => Item.Title;
            set
            {
                var v = value ?? "";
                if (Item.Title == v) return;
                Item.Title = v;
                OnPropertyChanged(nameof(Title));
            }
        }

        public TimeSpan StartTime
        {
            get => Item.StartTime;
            set
            {
                if (Item.StartTime == value) return;
                Item.StartTime = value;
                OnPropertyChanged(nameof(StartTime));
            }
        }

        public TimeSpan EndTime
        {
            get => Item.EndTime;
            set
            {
                if (Item.EndTime == value) return;
                Item.EndTime = value;
                OnPropertyChanged(nameof(EndTime));
            }
        }

        public string Notes
        {
            get => Item.Notes;
            set
            {
                var v = value ?? "";
                if (Item.Notes == v) return;
                Item.Notes = v;
                OnPropertyChanged(nameof(Notes));
            }
        }

        public EventAttractionVm(EventDayVm day, EventAttractionItem item)
        {
            Day = day;
            Item = item;
        }

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
