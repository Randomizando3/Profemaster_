using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class HomePage : ContentPage
{
    private readonly LocalStore _store;

    public HomePage(LocalStore store)
    {
        InitializeComponent();
        _store = store;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var session = await _store.LoadSessionAsync();
        EmailLabel.Text = session?.Email ?? "";

        if (session == null || string.IsNullOrWhiteSpace(session.IdToken))
            await Shell.Current.GoToAsync("///login");
    }

    private async void OnGoAgenda(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///tabs/agenda");

    private async void OnGoInstitutions(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///tabs/institutions");

    private async void OnGoPlans(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///tabs/plans");

    private async void OnGoExams(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///tabs/exams");

    private async void OnGoEvents(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///tabs/events");

    private async void OnLessons(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///tabs/lessons");

    private async void OnGoTools(object sender, EventArgs e)
    => await Shell.Current.GoToAsync("tools");

    private async void OnGoHelp(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("help");

    private async void OnGoUpgrade(object sender, EventArgs e)
    => await Shell.Current.GoToAsync("upgrade");


    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        await _store.ClearSessionAsync();
        await Shell.Current.GoToAsync("///login");
    }
}
