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

        try
        {
            var session = await _store.LoadSessionAsync();
            if (session != null && !string.IsNullOrWhiteSpace(session.IdToken))
            {
                // garante shell carregado
                await Shell.Current.GoToAsync("//login");
                await Shell.Current.GoToAsync("home");
            }
        }
        catch
        {
            // ignore: evita quebrar start por timing do Shell
        }
    }
}
