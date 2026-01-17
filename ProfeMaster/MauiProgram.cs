using Microsoft.Extensions.Logging;
using ProfeMaster.Pages;
using ProfeMaster.Services;

namespace ProfeMaster;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(new HttpClient());

        builder.Services.AddSingleton<LocalStore>();
        builder.Services.AddSingleton<FirebaseAuthService>();
        builder.Services.AddSingleton<FirebaseDbService>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<InstitutionsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
