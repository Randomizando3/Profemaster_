using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class LessonsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;
    private readonly FirebaseStorageService _storage;

    private string _uid = "";
    private string _token = "";

    private List<Lesson> _items = new();

    public LessonsPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _storage = storage;
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

    private async Task LoadAsync()
    {
        try
        {
            _items = await _db.GetLessonsAllAsync(_uid, _token);
            _items = _items
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();

            List.ItemsSource = _items;
            EmptyLabel.IsVisible = _items.Count == 0;
        }
        catch
        {
            _items = new();
            List.ItemsSource = _items;
            EmptyLabel.IsVisible = true;
        }
    }

    private async void OnAdd(object sender, EventArgs e)
    {
        var lesson = new Lesson
        {
            Title = "",
            Description = "",
            DurationMinutes = 50,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            MaterialsV2 = new()
        };

        await Navigation.PushModalAsync(new LessonEditorPage(_db, _store, _storage, lesson));
        await LoadAsync();
    }

    private async void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        var lesson = e.CurrentSelection?.FirstOrDefault() as Lesson;
        if (lesson == null) return;

        ((CollectionView)sender).SelectedItem = null;

        await Navigation.PushModalAsync(new LessonEditorPage(_db, _store, _storage, lesson));
        await LoadAsync();
    }
}
