// Pages/EventDetailsPage.xaml.cs
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class EventDetailsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    private EventItem _event;

    private string _uid = "";
    private string _token = "";

    private readonly List<DayVm> _days = new();

    public EventDetailsPage(LocalStore store, FirebaseDbService db, EventItem item)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _event = item;

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
        Render();
    }

    private void Render()
    {
        TitleLabel.Text = string.IsNullOrWhiteSpace(_event.Title) ? "(Sem título)" : _event.Title.Trim();

        var start = _event.StartDate == default ? DateTime.Today : _event.StartDate.Date;
        var end = _event.EndDate == default ? start : _event.EndDate.Date;
        if (end < start) end = start;

        MetaLabel.Text = start == end
            ? $"{start:dd/MM/yyyy}"
            : $"{start:dd/MM/yyyy} → {end:dd/MM/yyyy}";

        DescLabel.Text = string.IsNullOrWhiteSpace(_event.Description) ? "Descrição: -" : $"Descrição: {_event.Description.Trim()}";

        // thumb offline-first
        try
        {
            var local = _event.ThumbLocalPath ?? "";
            var url = _event.ThumbUrl ?? "";

            if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
                ThumbImage.Source = ImageSource.FromFile(local);
            else if (!string.IsNullOrWhiteSpace(url))
                ThumbImage.Source = new UriImageSource { Uri = new Uri(url), CachingEnabled = true };
            else
                ThumbImage.Source = null;
        }
        catch { ThumbImage.Source = null; }

        BuildPreview();
    }

    private void BuildPreview()
    {
        _event.Slots ??= new();
        _event.EnsureSlotsMigrated();

        _days.Clear();

        foreach (var day in _event.Slots.OrderBy(x => x.Date))
        {
            var vm = new DayVm(day);
            _days.Add(vm);
        }

        EmptyLabel.IsVisible = _days.Count == 0;

        DaysList.ItemsSource = null;
        DaysList.ItemsSource = _days;
    }

    private async Task ReloadFromCloud()
    {
        try
        {
            var all = await _db.GetEventsAllAsync(_uid, _token);
            var updated = all.FirstOrDefault(x => x.Id == _event.Id);
            if (updated != null) _event = updated;
        }
        catch { }
    }

    private async void OnEdit(object sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new EventEditorPage(_db, _store, _event));
        await ReloadFromCloud();
        Render();
    }

    private async void OnReload(object sender, EventArgs e)
    {
        await ReloadFromCloud();
        Render();
    }

    private sealed class DayVm
    {
        public string DateLabel { get; }
        public List<ItemVm> Items { get; } = new();

        public DayVm(EventDaySlot slot)
        {
            var d = slot.Date.Date;
            DateLabel = $"{d:dd/MM/yyyy} • {d:dddd}";

            slot.Items ??= new();
            foreach (var it in slot.Items.OrderBy(x => x.StartTime))
                Items.Add(new ItemVm(it));
        }
    }

    private sealed class ItemVm
    {
        public string Title { get; }
        public string TimeText { get; }

        public ItemVm(EventAttractionItem it)
        {
            Title = string.IsNullOrWhiteSpace(it.Title) ? "(Sem nome)" : it.Title.Trim();
            TimeText = $"{it.StartTime:hh\\:mm} → {it.EndTime:hh\\:mm}";
        }
    }
}
