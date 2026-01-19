using ProfeMaster.Pages;

namespace ProfeMaster;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("home", typeof(HomePage));

        Routing.RegisterRoute("classes", typeof(ClassesPage));
        Routing.RegisterRoute("students", typeof(StudentsPage));

        Routing.RegisterRoute("lesson_editor", typeof(LessonEditorPage));
        Routing.RegisterRoute("plan_editor", typeof(PlanEditorPage));
        Routing.RegisterRoute("plan_details", typeof(PlanDetailsPage));
        Routing.RegisterRoute("event_editor", typeof(EventEditorPage));
        Routing.RegisterRoute("event_details", typeof(EventDetailsPage));
        Routing.RegisterRoute("exam_editor", typeof(ExamEditorPage));
    }
}
