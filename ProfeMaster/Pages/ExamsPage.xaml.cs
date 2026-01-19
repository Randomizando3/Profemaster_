using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class ExamsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;
    private readonly GroqQuizService _quizSvc;

    private string _uid = "";
    private string _token = "";
    private List<ExamItem> _items = new();

    public ExamsPage(LocalStore store, FirebaseDbService db, GroqQuizService quizSvc)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _quizSvc = quizSvc;
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
            _items = (await _db.GetExamsAllAsync(_uid, _token))
                .OrderByDescending(x => x.CreatedAt)
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
        var item = new ExamItem();
        await Navigation.PushModalAsync(new ExamEditorPage(_db, _store, _quizSvc, item));
        await LoadAsync();
    }

    private async void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        var item = e.CurrentSelection?.FirstOrDefault() as ExamItem;
        if (item == null) return;

        ((CollectionView)sender).SelectedItem = null;

        await Navigation.PushModalAsync(new ExamEditorPage(_db, _store, _quizSvc, item));
        await LoadAsync();
    }
}
