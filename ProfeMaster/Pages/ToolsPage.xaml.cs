// Pages/ToolsPage.xaml.cs
namespace ProfeMaster.Pages;

public partial class ToolsPage : ContentPage
{
    public ToolsPage()
    {
        InitializeComponent();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnOpenCalculator(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("tools/calculator");

    private async void OnOpenCalendarTool(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("tools/calendar");

    private async void OnOpenNotes(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("tools/notes");
}
