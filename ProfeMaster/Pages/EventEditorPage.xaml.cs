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

    private async void OnClose(object sender, EventArgs e)
    {
        // valida mínimo para não criar “lixo”
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

        // UI template usa Items (com day reference)
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

    private sealed class EventAttractionVm
    {
        public EventDayVm Day { get; }
        public EventAttractionItem Item { get; }

        public string Title
        {
            get => Item.Title;
            set => Item.Title = value ?? "";
        }

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

        public string Notes
        {
            get => Item.Notes;
            set => Item.Notes = value ?? "";
        }

        public EventAttractionVm(EventDayVm day, EventAttractionItem item)
        {
            Day = day;
            Item = item;
        }
    }
}
