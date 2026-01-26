namespace ProfeMaster.Pages.Help;

public partial class TutorialsPage : ContentPage
{
    public TutorialsPage()
    {
        InitializeComponent();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");
}
