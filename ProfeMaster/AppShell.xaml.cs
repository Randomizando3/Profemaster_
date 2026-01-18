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
        Routing.RegisterRoute("classes", typeof(ClassesPage));
        Routing.RegisterRoute("students", typeof(StudentsPage));
        Routing.RegisterRoute("agenda", typeof(AgendaPage));
        Routing.RegisterRoute("plans", typeof(PlansPage));
        Routing.RegisterRoute("exams", typeof(ExamsPage));
        Routing.RegisterRoute("events", typeof(EventsPage));
        Routing.RegisterRoute("lessons", typeof(LessonsPage));
        Routing.RegisterRoute("lesson_editor", typeof(LessonEditorPage));



    }
}
