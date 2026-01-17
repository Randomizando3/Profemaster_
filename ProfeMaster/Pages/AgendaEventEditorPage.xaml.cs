using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class AgendaEventEditorPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;

    private ScheduleEvent _ev;
    private string _uid = "";
    private string _token = "";

    private readonly List<Institution> _institutions = new();
    private readonly List<Classroom> _classes = new();

    private const string CreateNewClassLabel = "+ Criar nova turma...";

    public AgendaEventEditorPage(FirebaseDbService db, LocalStore store, ScheduleEvent ev)
    {
        InitializeComponent();
        _db = db;
        _store = store;
        _ev = ev;

        TitleEntry.Text = _ev.Title ?? "";
        TypeEntry.Text = string.IsNullOrWhiteSpace(_ev.Type) ? "Aula" : _ev.Type;

        DatePick.Date = _ev.Start == default ? DateTime.Today : _ev.Start.Date;
        StartPick.Time = (_ev.Start == default ? DateTime.Today.AddHours(8) : _ev.Start).TimeOfDay;
        EndPick.Time = (_ev.End == default ? DateTime.Today.AddHours(9) : _ev.End).TimeOfDay;

        DescEditor.Text = _ev.Description ?? "";

        LinkLabel.Text = string.IsNullOrWhiteSpace(_ev.LinkedPlanId)
            ? ""
            : $"Vinculado ao plano: {_ev.LinkedPlanTitle}";
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

        await LoadInstitutionsAsync(selectExisting: true);
    }

    private async Task LoadInstitutionsAsync(bool selectExisting)
    {
        try
        {
            var list = await _db.GetInstitutionsAsync(_uid, _token);
            _institutions.Clear();
            _institutions.AddRange(list);

            InstitutionPicker.ItemsSource = _institutions.Select(x => x.Name).ToList();

            if (_institutions.Count == 0)
            {
                InstitutionPicker.SelectedIndex = -1;
                ClassPicker.ItemsSource = new List<string>();
                ClassPicker.SelectedIndex = -1;
                return;
            }

            if (selectExisting && !string.IsNullOrWhiteSpace(_ev.InstitutionId))
            {
                var idx = _institutions.FindIndex(x => x.Id == _ev.InstitutionId);
                InstitutionPicker.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else if (InstitutionPicker.SelectedIndex < 0)
            {
                InstitutionPicker.SelectedIndex = 0;
            }

            await LoadClassesAsync(selectExisting: selectExisting);
        }
        catch
        {
            InstitutionPicker.ItemsSource = new List<string>();
            ClassPicker.ItemsSource = new List<string>();
            InstitutionPicker.SelectedIndex = -1;
            ClassPicker.SelectedIndex = -1;
        }
    }

    private async Task LoadClassesAsync(bool selectExisting)
    {
        _classes.Clear();
        ClassPicker.ItemsSource = new List<string>();
        ClassPicker.SelectedIndex = -1;

        if (InstitutionPicker.SelectedIndex < 0 || InstitutionPicker.SelectedIndex >= _institutions.Count)
            return;

        var inst = _institutions[InstitutionPicker.SelectedIndex];

        try
        {
            var list = await _db.GetClassesAsync(_uid, inst.Id, _token);
            _classes.AddRange(list);

            var items = _classes.Select(x => x.Name).ToList();
            items.Add(CreateNewClassLabel);
            ClassPicker.ItemsSource = items;

            if (selectExisting && !string.IsNullOrWhiteSpace(_ev.ClassId))
            {
                var idx = _classes.FindIndex(x => x.Id == _ev.ClassId);
                ClassPicker.SelectedIndex = idx >= 0 ? idx : (items.Count > 1 ? 0 : -1);
            }
            else
            {
                // se o evento não tem turma, deixa sem seleção (ou seleciona 1ª se existir)
                ClassPicker.SelectedIndex = _classes.Count > 0 ? 0 : -1;
            }
        }
        catch
        {
            ClassPicker.ItemsSource = new List<string> { CreateNewClassLabel };
            ClassPicker.SelectedIndex = -1;
        }
    }

    private async void OnInstitutionChanged(object sender, EventArgs e)
    {
        // ao trocar instituição, recarrega turmas
        await LoadClassesAsync(selectExisting: false);
    }

    private async void OnClassChanged(object sender, EventArgs e)
    {
        if (ClassPicker.SelectedIndex < 0) return;

        var items = ClassPicker.ItemsSource as IList<string>;
        if (items == null || items.Count == 0) return;

        if (items[ClassPicker.SelectedIndex] == CreateNewClassLabel)
        {
            // volta a seleção anterior “segura”
            ClassPicker.SelectedIndex = _classes.Count > 0 ? 0 : -1;

            // abre turmas para criar
            // a tela ClassesPage já existe; ela deve criar turma dentro da instituição atual.
            await Shell.Current.GoToAsync("classes");

            // quando voltar, o OnAppearing recarrega e você seleciona a turma recém criada.
            return;
        }
    }

    private async void OnCancel(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    private async void OnSave(object sender, EventArgs e)
    {
        // valida
        var title = (TitleEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Erro", "Informe o título.", "OK");
            return;
        }

        if (InstitutionPicker.SelectedIndex < 0 || InstitutionPicker.SelectedIndex >= _institutions.Count)
        {
            await DisplayAlert("Erro", "Selecione uma instituição.", "OK");
            return;
        }

        var inst = _institutions[InstitutionPicker.SelectedIndex];

        Classroom? cls = null;
        if (ClassPicker.SelectedIndex >= 0 && ClassPicker.SelectedIndex < _classes.Count)
        {
            cls = _classes[ClassPicker.SelectedIndex];
        }
        else
        {
            await DisplayAlert("Erro", "Selecione uma turma.", "OK");
            return;
        }

        _ev.Title = title;
        _ev.Type = (TypeEntry.Text ?? "Aula").Trim();
        _ev.Description = (DescEditor.Text ?? "").Trim();

        var date = DatePick.Date;
        _ev.Start = date.Add(StartPick.Time);
        _ev.End = date.Add(EndPick.Time);
        if (_ev.End <= _ev.Start) _ev.End = _ev.Start.AddHours(1);

        // aplica vínculo
        _ev.InstitutionId = inst.Id;
        _ev.InstitutionName = inst.Name;
        _ev.ClassId = cls.Id;
        _ev.ClassName = cls.Name;

        // salva
        var okAll = await _db.UpsertAgendaAllAsync(_uid, _token, _ev);
        if (!okAll)
        {
            await DisplayAlert("Erro", "Falha ao salvar na agenda geral.", "OK");
            return;
        }

        await _db.UpsertAgendaByClassAsync(_uid, _ev.InstitutionId, _ev.ClassId, _token, _ev);

        await Navigation.PopModalAsync();
    }
}
