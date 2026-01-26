// Pages/Tools/CalendarToolPage.xaml.cs
namespace ProfeMaster.Pages.Tools;

public partial class CalendarToolPage : ContentPage
{
    public CalendarToolPage()
    {
        InitializeComponent();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnOpenCalendar(object sender, EventArgs e)
    {
        // 1) tenta calendário nativo (Android)
        try
        {
#if ANDROID
            // Abre o calendário padrão do Android (geralmente resolve para o app nativo)
            var uri = new Uri("content://com.android.calendar/time/");
            await Launcher.Default.OpenAsync(uri);
            return;
#endif
        }
        catch
        {
            // fallback abaixo
        }

        // 2) fallback: Google Calendar web
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://calendar.google.com"));
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível abrir o calendário.", "OK");
        }
    }
}
