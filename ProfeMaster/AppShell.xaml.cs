using ProfeMaster.Pages;

namespace ProfeMaster;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("home", typeof(HomePage));
        Routing.RegisterRoute("institutions", typeof(InstitutionsPage));
    }
}
