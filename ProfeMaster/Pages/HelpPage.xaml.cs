namespace ProfeMaster.Pages;

public partial class HelpPage : ContentPage
{
    public HelpPage()
    {
        InitializeComponent();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnGoTutorials(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("help_tutorials");

    private async void OnGoFaqs(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("help_faqs");

    private async void OnGoContact(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("help_contact");
}
