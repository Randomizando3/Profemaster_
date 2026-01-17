using ProfeMaster.Services;

namespace ProfeMaster;

public partial class App : Application
{
    private readonly LocalStore _store;

    public App(LocalStore store)
    {
        InitializeComponent();
        _store = store;

        MainPage = new AppShell();
    }

    protected override async void OnStart()
    {
        base.OnStart();

        var session = await _store.LoadSessionAsync();
        if (session != null && !string.IsNullOrWhiteSpace(session.IdToken))
        {
            await Shell.Current.GoToAsync("//login"); // garante shell carregado
            await Shell.Current.GoToAsync("home");
        }
    }
}
