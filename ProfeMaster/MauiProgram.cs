using Microsoft.Extensions.Logging;
using ProfeMaster.Pages;
using ProfeMaster.Services;
using Microsoft.Maui.LifecycleEvents;


#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
#endif

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
        builder.Services.AddSingleton<FirebaseStorageService>();
        builder.Services.AddSingleton<GroqQuizService>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<InstitutionsPage>();
        builder.Services.AddTransient<ClassesPage>();
        builder.Services.AddTransient<StudentsPage>();
        builder.Services.AddTransient<PlansPage>();
        builder.Services.AddTransient<PlanDetailsPage>();
        builder.Services.AddTransient<PlanEditorPage>();
        builder.Services.AddTransient<AgendaEventEditorPage>();
        builder.Services.AddTransient<AgendaEventDetailsPage>();
        builder.Services.AddTransient<ExamsPage>();
        builder.Services.AddTransient<EventsPage>();
        builder.Services.AddTransient<LessonsPage>();





#if WINDOWS
    builder.ConfigureLifecycleEvents(events =>
    {
        events.AddWindows(w =>
        {
            w.OnWindowCreated(window =>
            {
                try
                {
                    var hwnd = WindowNative.GetWindowHandle(window);
                    var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = AppWindow.GetFromWindowId(id);

                    // “Phone-ish”: largura ~390 e altura 700
                    appWindow.Resize(new SizeInt32(390, 700));

                    // Opcional: impede que maximize
                    if (appWindow.Presenter is OverlappedPresenter p)
                    {
                        p.IsMaximizable = false;
                        p.IsResizable = true; // deixe true se quiser ajustar manualmente
                    }
                }
                catch { /* ignore */ }
            });
        });
    }); 
#endif


#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
