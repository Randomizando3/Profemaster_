namespace ProfeMaster.Pages.Help;

public partial class FaqsPage : ContentPage
{
    public FaqsPage()
    {
        InitializeComponent();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");
}
