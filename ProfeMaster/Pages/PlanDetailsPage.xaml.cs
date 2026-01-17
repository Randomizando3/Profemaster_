using ProfeMaster.Config;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class PlanDetailsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;
    private readonly FirebaseStorageService _storage;

    private LessonPlan _plan;
    private string _uid = "";
    private string _token = "";

    private List<ScheduleEvent> _agendaEvents = new();
    private const string NoneAgendaLabel = "(Sem vínculo)";

    public PlanDetailsPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage, LessonPlan plan)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _storage = storage;
        _plan = plan;

        UploadBtn.IsVisible = AppFlags.EnableStorageUploads;

        Render();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var session = await _store.LoadSessionAsync();
        if (session == null || string.IsNullOrWhiteSpace(session.Uid))
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }
        _uid = session.Uid;
        _token = session.IdToken;

        await LoadAgendaPickerAsync();
        await ReloadFromCloud();
    }

    private async Task LoadAgendaPickerAsync()
    {
        try
        {
            // eventos recentes para não pesar
            _agendaEvents = await _db.GetAgendaAllRecentAsync(_uid, _token, daysBack: 180, daysForward: 365);

            var labels = new List<string> { NoneAgendaLabel };
            labels.AddRange(_agendaEvents.Select(e => $"{e.Start:dd/MM/yyyy HH:mm} • {e.Title}"));

            AgendaPicker.ItemsSource = labels;

            // seleção atual
            if (!string.IsNullOrWhiteSpace(_plan.LinkedEventId))
            {
                var idx = _agendaEvents.FindIndex(x => x.Id == _plan.LinkedEventId);
                AgendaPicker.SelectedIndex = idx >= 0 ? idx + 1 : 0;
            }
            else
            {
                AgendaPicker.SelectedIndex = 0;
            }

            AgendaLinkLabel.Text = string.IsNullOrWhiteSpace(_plan.LinkedEventId)
                ? "Nenhum evento vinculado."
                : $"Vinculado: {_plan.LinkedEventTitle}";
        }
        catch
        {
            AgendaPicker.ItemsSource = new List<string> { NoneAgendaLabel };
            AgendaPicker.SelectedIndex = 0;
            AgendaLinkLabel.Text = "Nenhum evento vinculado.";
        }
    }


    private async void OnSaveAgendaLink(object sender, EventArgs e)
    {
        if (AgendaPicker.SelectedIndex <= 0)
        {
            // desvincular
            _plan.LinkedEventId = "";
            _plan.LinkedEventTitle = "";
            await PersistPlanAsync();
            AgendaLinkLabel.Text = "Nenhum evento vinculado.";
            return;
        }

        var ev = _agendaEvents[AgendaPicker.SelectedIndex - 1];
        _plan.LinkedEventId = ev.Id;
        _plan.LinkedEventTitle = ev.Title;

        await PersistPlanAsync();
        AgendaLinkLabel.Text = $"Vinculado: {ev.Title}";
    }



    private void Render()
    {
        TitleLabel.Text = _plan.Title;
        MetaLabel.Text = $"{_plan.Date:dd/MM/yyyy} • {_plan.InstitutionName} • {_plan.ClassName}";

        ObjLabel.Text = string.IsNullOrWhiteSpace(_plan.Objectives) ? "-" : _plan.Objectives;
        ContentLabel.Text = string.IsNullOrWhiteSpace(_plan.Content) ? "-" : _plan.Content;
        StepsLabel.Text = string.IsNullOrWhiteSpace(_plan.Steps) ? "-" : _plan.Steps;
        EvalLabel.Text = string.IsNullOrWhiteSpace(_plan.Evaluation) ? "-" : _plan.Evaluation;

        _plan.MaterialsV2 ??= new();
        MatList.ItemsSource = null;
        MatList.ItemsSource = _plan.MaterialsV2.OrderByDescending(x => x.CreatedAt).ToList();
    }

    private async Task ReloadFromCloud()
    {
        // busca pelo modo apropriado: se tem turma, tenta byClass; se não, all.
        try
        {
            LessonPlan? updated = null;

            if (!string.IsNullOrWhiteSpace(_plan.InstitutionId) && !string.IsNullOrWhiteSpace(_plan.ClassId))
            {
                var list = await _db.GetPlansByClassAsync(_uid, _plan.InstitutionId, _plan.ClassId, _token);
                updated = list.FirstOrDefault(x => x.Id == _plan.Id);
            }

            if (updated == null)
            {
                var all = await _db.GetPlansAllAsync(_uid, _token);
                updated = all.FirstOrDefault(x => x.Id == _plan.Id);
            }

            if (updated != null)
            {
                _plan = updated;
                Render();
            }
        }
        catch { }
    }

    private async Task PersistPlanAsync()
    {
        // grava all sempre
        await _db.UpsertPlanAllAsync(_uid, _token, _plan);

        // se tiver turma, grava byClass
        if (!string.IsNullOrWhiteSpace(_plan.InstitutionId) && !string.IsNullOrWhiteSpace(_plan.ClassId))
            await _db.UpsertPlanByClassAsync(_uid, _plan.InstitutionId, _plan.ClassId, _token, _plan);
    }

    private async void OnEdit(object sender, EventArgs e)
    {
        // abre o editor com DatePicker
        await Navigation.PushModalAsync(new PlanEditorPage(_db, _store, _plan));
        // quando voltar, recarrega do cloud
        await ReloadFromCloud();
    }

    private async void OnLinkAgenda(object sender, EventArgs e)
    {
        var ev = new ScheduleEvent
        {
            Title = $"Plano: {_plan.Title}",
            Type = "Aula",
            Description = "Vinculado a Plano de Aula",
            Start = _plan.Date.AddHours(8),
            End = _plan.Date.AddHours(9),
            InstitutionId = _plan.InstitutionId,
            InstitutionName = _plan.InstitutionName,
            ClassId = _plan.ClassId,
            ClassName = _plan.ClassName,
            LinkedPlanId = _plan.Id,
            LinkedPlanTitle = _plan.Title
        };

        await Navigation.PushModalAsync(new AgendaEventEditorPage(_db, _store, ev));
    }

    // ===== Materiais =====

    private async void OnAddLink(object sender, EventArgs e)
    {
        var title = await DisplayPromptAsync("Novo link", "Título:", "Salvar", "Cancelar", "Ex: Vídeo aula");
        if (string.IsNullOrWhiteSpace(title)) return;

        var url = await DisplayPromptAsync("Novo link", "URL:", "Salvar", "Cancelar", "https://");
        if (string.IsNullOrWhiteSpace(url)) return;

        _plan.MaterialsV2 ??= new();
        _plan.MaterialsV2.Add(new LessonMaterial
        {
            Kind = MaterialKind.Link,
            Title = title.Trim(),
            Url = url.Trim()
        });

        await PersistPlanAsync();
        Render();
    }

    private async void OnUpload(object sender, EventArgs e)
    {
        if (!AppFlags.EnableStorageUploads)
        {
            await DisplayAlert("Info", "Uploads desabilitados por flag. Use links.", "OK");
            return;
        }

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Selecione um arquivo" });
            if (file == null) return;

            await using var stream = await file.OpenReadAsync();

            var storagePath = $"users/{_uid}/plans/{_plan.Id}/{file.FileName}";
            var contentType = file.ContentType ?? "application/octet-stream";

            var (ok, dl, path, err) = await _storage.UploadAsync(_token, storagePath, file.FileName, contentType, stream);
            if (!ok)
            {
                await DisplayAlert("Erro", err, "OK");
                return;
            }

            _plan.MaterialsV2 ??= new();
            _plan.MaterialsV2.Add(new LessonMaterial
            {
                Kind = MaterialKind.StorageFile,
                Title = file.FileName,
                Url = dl,
                StoragePath = path,
                ContentType = contentType
            });

            await PersistPlanAsync();
            Render();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

    private async void OnOpenMaterial(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not LessonMaterial m) return;

        if (string.IsNullOrWhiteSpace(m.Url))
        {
            await DisplayAlert("Erro", "URL vazia.", "OK");
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(m.Url);
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível abrir o link.", "OK");
        }
    }

    private async void OnDeleteMaterial(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not LessonMaterial m) return;

        var confirm = await DisplayAlert("Excluir", $"Remover \"{m.Title}\"?", "Sim", "Não");
        if (!confirm) return;

        _plan.MaterialsV2 ??= new();
        _plan.MaterialsV2.Remove(m);

        await PersistPlanAsync();
        Render();
    }

   

}
