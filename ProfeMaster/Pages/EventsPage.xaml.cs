// Pages/EventsPage.xaml.cs
using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class EventsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    private string _uid = "";
    private string _token = "";

    public sealed class EventCardVm
    {
        public EventItem Event { get; set; } = new();

        public string Title { get; set; } = "";
        public string ThumbLocalPath { get; set; } = "";
        public string IntervalText { get; set; } = "";
        public string SlotsText { get; set; } = "";
    }

    public ObservableCollection<EventCardVm> Items { get; } = new();

    public EventsPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage)
    {
        InitializeComponent();
        _store = store;
        _db = db;

        // storage não é necessário aqui (fica no editor/detalhes se você for adicionar thumb upload depois)
        BindingContext = this;

        List.ItemsSource = Items;
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

        await LoadAsync();
    }

    private static EventCardVm ToVm(EventItem e)
    {
        e.Slots ??= new();

        var start = e.StartDate == default ? DateTime.Today : e.StartDate.Date;
        var end = e.EndDate == default ? start : e.EndDate.Date;
        if (end < start) end = start;

        var intervalText = start == end
            ? $"Data: {start:dd/MM/yyyy}"
            : $"Período: {start:dd/MM/yyyy} → {end:dd/MM/yyyy}";

        return new EventCardVm
        {
            Event = e,
            Title = string.IsNullOrWhiteSpace(e.Title) ? "(Sem título)" : e.Title.Trim(),
            ThumbLocalPath = e.ThumbLocalPath ?? "",
            IntervalText = intervalText,
            SlotsText = $"Dias: {e.Slots.Count}"
        };
    }

    private async Task LoadAsync()
    {
        try
        {
            var all = await _db.GetEventsAllAsync(_uid, _token);

            // ordena por início (desc) e fallback por CreatedAt
            all = all
                .OrderByDescending(x => x.StartDate == default ? DateTime.MinValue : x.StartDate.Date)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();

            Items.Clear();
            foreach (var it in all)
                Items.Add(ToVm(it));

            EmptyLabel.IsVisible = Items.Count == 0;
        }
        catch
        {
            Items.Clear();
            EmptyLabel.IsVisible = true;
        }
    }

    private async void OnRefresh(object sender, EventArgs e) => await LoadAsync();

    private async void OnAdd(object sender, EventArgs e)
    {
        var item = new EventItem
        {
            Title = "",
            Description = "",
            StartDate = DateTime.Today,
            EndDate = DateTime.Today,
            CreatedAt = DateTimeOffset.UtcNow,
            Slots = new()
        };

        await Navigation.PushModalAsync(new EventEditorPage(_db, _store, item));
        await LoadAsync();
    }

    private async void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        var vm = e.CurrentSelection?.FirstOrDefault() as EventCardVm;
        if (vm == null) return;

        ((CollectionView)sender).SelectedItem = null;
        await Navigation.PushAsync(new EventDetailsPage(_store, _db, vm.Event));
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.BindingContext is not EventCardVm vm) return;

        await Navigation.PushModalAsync(new EventEditorPage(_db, _store, vm.Event));
        await LoadAsync();
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.BindingContext is not EventCardVm vm) return;

        var ev = vm.Event;
        var confirm = await DisplayAlert("Excluir", $"Excluir \"{ev.Title}\"?", "Sim", "Não");
        if (!confirm) return;

        await _db.DeleteEventAsync(_uid, _token, ev.Id);
        Items.Remove(vm);
        EmptyLabel.IsVisible = Items.Count == 0;
    }
}
