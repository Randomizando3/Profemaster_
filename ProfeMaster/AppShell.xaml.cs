using ProfeMaster.Pages;
using ProfeMaster.Pages.Tools;
using ProfeMaster.Pages.Help;

namespace ProfeMaster;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("home", typeof(HomePage));
        Routing.RegisterRoute("reset-password", typeof(ResetPasswordPage));

        Routing.RegisterRoute("classes", typeof(ClassesPage));
        Routing.RegisterRoute("students", typeof(StudentsPage));

        Routing.RegisterRoute("lesson_editor", typeof(LessonEditorPage));
        Routing.RegisterRoute("plan_editor", typeof(PlanEditorPage));
        Routing.RegisterRoute("plan_details", typeof(PlanDetailsPage));
        Routing.RegisterRoute("event_editor", typeof(EventEditorPage));
        Routing.RegisterRoute("event_details", typeof(EventDetailsPage));
        Routing.RegisterRoute("exam_editor", typeof(ExamEditorPage));
        Routing.RegisterRoute("tools", typeof(ToolsPage));
        Routing.RegisterRoute("upgrade", typeof(UpgradePage));
        Routing.RegisterRoute("tools/calculator", typeof(CalculatorPage));
        Routing.RegisterRoute("tools/calendar", typeof(CalendarToolPage));
        Routing.RegisterRoute("tools/notes", typeof(NotesPostItPage));
        Routing.RegisterRoute("help", typeof(HelpPage));
        Routing.RegisterRoute("help_tutorials", typeof(TutorialsPage));
        Routing.RegisterRoute("help_faqs", typeof(FaqsPage));
        Routing.RegisterRoute("help_contact", typeof(ContactPage));

    }
}
