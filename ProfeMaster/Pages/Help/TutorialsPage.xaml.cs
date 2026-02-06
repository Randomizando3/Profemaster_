using ProfeMaster.Pages.Help.Tutoriais;

namespace ProfeMaster.Pages.Help;

public partial class TutorialsPage : ContentPage
{
    public TutorialsPage()
    {
        InitializeComponent();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnOpenTutorial(object sender, TappedEventArgs e)
    {
        var fileName = e.Parameter?.ToString();
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        // (Opcional) Título amigável
        var title = fileName switch
        {
            "instituicoes_turmas_alunos.html" => "Instituição, turmas e alunos",
            "agenda_como_utilizar.html" => "Como utilizar a agenda",
            "aulas_criar_gerenciar.html" => "Aulas: criar e gerenciar",
            "planos_criar_gerenciar.html" => "Planos de aula: criar e gerenciar",
            "eventos_criar_gerenciar.html" => "Eventos: criar e gerenciar",
            "provas_criar_gerenciar.html" => "Provas: criar e gerenciar",
            _ => "Tutorial"
        };

        // Modal (com página própria). Envolvo em NavigationPage para ter comportamento mais “app-like”
        var modal = new NavigationPage(new TutorialModalPage(fileName, title));
        // Modal SEM NavigationPage (sem barra azul)
        await Navigation.PushModalAsync(new ProfeMaster.Pages.Help.Tutoriais.TutorialModalPage(fileName, title));

    }
}
