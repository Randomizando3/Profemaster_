using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class AgendaPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;
    private readonly FirebaseStorageService _storage;

    public ObservableCollection<ScheduleEvent> Items { get; } = new();
    public ObservableCollection<AgendaRow> Rows { get; } = new();

    private string _uid = "";
    private string _token = "";

    private bool _modeAll = true;

    private readonly List<Institution> _institutions = new();
    private readonly List<Classroom> _classes = new();

    private Institution? _selectedInst;
    private Classroom? _selectedClass;

    private bool _showPast = false;
    private bool _dateFilterEnabled = false;
    private DateTime _dateFilter = DateTime.Today;

    public AgendaPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage)
    {
        InitializeComponent();

        _store = store;
        _db = db;
        _storage = storage;

        FilterDatePicker.Date = DateTime.Today;
        BindingContext = this;
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

        await LoadInstitutionsAsync();
        await LoadCacheThenCloudAsync();
    }

    private static string NormKind(string? s)
    {
        var v = (s ?? "").Trim();
        return string.IsNullOrWhiteSpace(v) ? "Aula" : v;
    }

    private static bool IsMultiDayKind(string kind)
    {
        // Plano de aula e Evento se comportam como multi-dia
        return kind.Equals("Plano de aula", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("Plano", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("Evento", StringComparison.OrdinalIgnoreCase);
    }

    // Expande um ScheduleEvent em ocorrências por dia quando ele cruza datas
    private static IEnumerable<(DateTime Day, ScheduleEvent Ev)> ExpandByDay(IEnumerable<ScheduleEvent> source)
    {
        foreach (var ev in source)
        {
            var kind = NormKind(ev.Kind);
            var s = ev.Start;
            var e = ev.End;

            // segurança
            if (e < s) (s, e) = (e, s);

            var startDay = s.Date;
            var endDay = e.Date;

            // Se não é multi-dia ou é no mesmo dia: retorna 1 ocorrência
            if (!IsMultiDayKind(kind) || startDay == endDay)
            {
                yield return (startDay, ev);
                continue;
            }

            // Multi-dia: retorna uma ocorrência por dia
            for (var d = startDay; d <= endDay; d = d.AddDays(1))
            {
                // Reaproveita o mesmo objeto (sem clone) porque sua tela details precisa do Id original.
                // A lista só usa o dia para agrupamento; o evento continua abrindo o mesmo Id.
                yield return (d, ev);
            }
        }
    }

    private async Task LoadInstitutionsAsync()
    {
        try
        {
            var list = await _db.GetInstitutionsAsync(_uid, _token);
            _institutions.Clear();
            _institutions.AddRange(list);

            InstitutionPicker.ItemsSource = _institutions.Select(x => x.Name).ToList();

            if (_institutions.Count > 0 && InstitutionPicker.SelectedIndex < 0)
                InstitutionPicker.SelectedIndex = 0;
        }
        catch
        {
            _institutions.Clear();
            InstitutionPicker.ItemsSource = new List<string>();
            InstitutionPicker.SelectedIndex = -1;
        }
    }

    private async Task LoadClassesAsync(string institutionId)
    {
        try
        {
            var list = await _db.GetClassesAsync(_uid, institutionId, _token);
            _classes.Clear();
            _classes.AddRange(list);

            ClassPicker.ItemsSource = _classes.Select(x => x.Name).ToList();
            ClassPicker.SelectedIndex = _classes.Count > 0 ? 0 : -1;
        }
        catch
        {
            _classes.Clear();
            ClassPicker.ItemsSource = new List<string>();
            ClassPicker.SelectedIndex = -1;
        }
    }

    private async Task LoadCacheThenCloudAsync()
    {
        Items.Clear();

        if (_modeAll)
        {
            PickersBox.IsVisible = false;
            ModeLabel.Text = "Geral (todas as turmas)";

            var cached = await _store.LoadAgendaAllCacheAsync();
            if (cached != null)
            {
                foreach (var x in cached.OrderBy(x => x.Start))
                    Items.Add(x);
            }

            BuildRows();
            await LoadAllFromCloudAsync();
        }
        else
        {
            PickersBox.IsVisible = true;

            if (_selectedInst == null || _selectedClass == null)
            {
                ModeLabel.Text = "Selecione a instituição e a turma";
                Rows.Clear();
                EmptyLabel.IsVisible = false;
                return;
            }

            ModeLabel.Text = $"{_selectedInst.Name} • {_selectedClass.Name}";

            var cached = await _store.LoadAgendaClassCacheAsync(_selectedInst.Id, _selectedClass.Id);
            if (cached != null)
            {
                foreach (var x in cached.OrderBy(x => x.Start))
                    Items.Add(x);
            }

            BuildRows();
            await LoadClassFromCloudAsync(_selectedInst.Id, _selectedClass.Id);
        }
    }

    private async Task LoadAllFromCloudAsync()
    {
        try
        {
            var list = await _db.GetAgendaAllAsync(_uid, _token);
            Items.Clear();

            foreach (var e in list.OrderBy(x => x.Start))
                Items.Add(e);

            await _store.SaveAgendaAllCacheAsync(list);
            BuildRows();
        }
        catch { }
    }

    private async Task LoadClassFromCloudAsync(string institutionId, string classId)
    {
        try
        {
            var list = await _db.GetAgendaByClassAsync(_uid, institutionId, classId, _token);
            Items.Clear();

            foreach (var e in list.OrderBy(x => x.Start))
                Items.Add(e);

            await _store.SaveAgendaClassCacheAsync(institutionId, classId, list);
            BuildRows();
        }
        catch { }
    }

    private void BuildRows()
    {
        Rows.Clear();

        var today = DateTime.Today;

        // 1) aplica filtros no "range de dias"
        DateTime? filterDay = null;

        if (_dateFilterEnabled)
        {
            filterDay = _dateFilter.Date;
            FilterLabel.Text = $"Filtrado: {filterDay:dd/MM/yyyy}";
        }
        else
        {
            FilterLabel.Text = _showPast ? "Mostrando anteriores + futuros" : "A partir de hoje";
        }

        // 2) expande multi-dia e filtra por dia
        var expanded = ExpandByDay(Items);

        if (filterDay.HasValue)
        {
            expanded = expanded.Where(x => x.Day.Date == filterDay.Value);
        }
        else
        {
            if (!_showPast)
                expanded = expanded.Where(x => x.Day.Date >= today);
        }

        // 3) ordena por dia e dentro do dia por Start
        var ordered = expanded
            .OrderBy(x => x.Day)
            .ThenBy(x => x.Ev.Start.TimeOfDay)
            .Select(x => x.Ev)
            .ToList();

        if (ordered.Count == 0)
        {
            EmptyLabel.IsVisible = _dateFilterEnabled;
            return;
        }

        EmptyLabel.IsVisible = false;

        DateTime? currentDate = null;
        foreach (var ev in ordered)
        {
            // agrupa pelo DIA DA OCORRÊNCIA
            // (para multi-dia, a ocorrência foi expandida; mas aqui ainda temos o ev original.
            // Para header, usamos a lógica baseada no Start.Date quando single-day,
            // e para multi-day, o header correto vem do filtro/expand. Para não complicar,
            // calculamos "dayKey" como:
            var kind = NormKind(ev.Kind);
            var isMulti = IsMultiDayKind(kind) && ev.Start.Date != ev.End.Date;

            // Se está filtrado por data, o header deve ser essa data.
            // Se não está filtrado, inferimos pelo "ev.Start.Date" para single-day
            // e deixamos multi-day agrupado pelo Start.Date. Isso pode repetir dias se houver multi-day,
            // mas o efeito final fica correto porque a fonte expandida já foi ordenada por Day.
            // Para garantir 100%, quando não há filtro, usamos um truque:
            // Ao invés de tentar deduzir o dia aqui, reconstruímos novamente a sequência expandida por dia
            // para gerar as rows com o dia certo.
        }

        // Recria Rows corretamente com o Day real do expand (sem gambiarra)
        Rows.Clear();

        var orderedExpanded = ExpandByDay(Items);

        if (filterDay.HasValue)
            orderedExpanded = orderedExpanded.Where(x => x.Day.Date == filterDay.Value);
        else
        {
            if (!_showPast)
                orderedExpanded = orderedExpanded.Where(x => x.Day.Date >= today);
        }

        var seq = orderedExpanded
            .OrderBy(x => x.Day)
            .ThenBy(x => x.Ev.Start.TimeOfDay)
            .ToList();

        DateTime? cur = null;
        foreach (var it in seq)
        {
            var d = it.Day.Date;

            if (cur == null || cur.Value != d)
            {
                Rows.Add(AgendaRow.MakeHeader(d));
                cur = d;
            }

            Rows.Add(AgendaRow.MakeItem(it.Ev));
        }
    }

    private async Task RefreshCurrentModeAsync()
    {
        if (_modeAll) await LoadAllFromCloudAsync();
        else if (_selectedInst != null && _selectedClass != null)
            await LoadClassFromCloudAsync(_selectedInst.Id, _selectedClass.Id);
    }

    private async void OnModeAllClicked(object sender, EventArgs e)
    {
        _modeAll = true;
        BtnAll.Background = (Brush)Application.Current.Resources["PmGradient"];
        BtnClass.BackgroundColor = Color.FromArgb("#243056");
        await LoadCacheThenCloudAsync();
    }

    private async void OnModeClassClicked(object sender, EventArgs e)
    {
        _modeAll = false;
        BtnAll.BackgroundColor = Color.FromArgb("#243056");
        BtnClass.Background = (Brush)Application.Current.Resources["PmGradient"];

        if (_institutions.Count > 0 && InstitutionPicker.SelectedIndex < 0)
            InstitutionPicker.SelectedIndex = 0;

        await LoadCacheThenCloudAsync();
    }

    private async void OnInstitutionChanged(object sender, EventArgs e)
    {
        if (InstitutionPicker.SelectedIndex < 0 || InstitutionPicker.SelectedIndex >= _institutions.Count)
            return;

        _selectedInst = _institutions[InstitutionPicker.SelectedIndex];
        await LoadClassesAsync(_selectedInst.Id);

        _selectedClass = (ClassPicker.SelectedIndex >= 0 && ClassPicker.SelectedIndex < _classes.Count)
            ? _classes[ClassPicker.SelectedIndex]
            : null;

        if (!_modeAll)
            await LoadCacheThenCloudAsync();
    }

    private async void OnClassChanged(object sender, EventArgs e)
    {
        if (ClassPicker.SelectedIndex < 0 || ClassPicker.SelectedIndex >= _classes.Count)
        {
            _selectedClass = null;
            if (!_modeAll) await LoadCacheThenCloudAsync();
            return;
        }

        _selectedClass = _classes[ClassPicker.SelectedIndex];
        if (!_modeAll) await LoadCacheThenCloudAsync();
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
        => await RefreshCurrentModeAsync();

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var ev = new ScheduleEvent
        {
            Title = "",
            Kind = "Aula", // <<<< NOVO PADRÃO
            Description = "",
            Start = DateTime.Today.AddHours(8),
            End = DateTime.Today.AddHours(9),
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (!_modeAll && _selectedInst != null && _selectedClass != null)
        {
            ev.InstitutionId = _selectedInst.Id;
            ev.InstitutionName = _selectedInst.Name;
            ev.ClassId = _selectedClass.Id;
            ev.ClassName = _selectedClass.Name;
        }

        await Navigation.PushModalAsync(new AgendaEventEditorPage(_store, _db, ev, ModeLabel.Text ?? ""));
        await RefreshCurrentModeAsync();
    }

    private void OnToggleFilter(object sender, EventArgs e)
        => FilterPanel.IsVisible = !FilterPanel.IsVisible;

    private void OnShowPastToggled(object sender, ToggledEventArgs e)
    {
        if (_dateFilterEnabled)
        {
            ShowPastSwitch.IsToggled = false;
            _showPast = false;
            BuildRows();
            return;
        }

        _showPast = e.Value;
        BuildRows();
    }

    private void OnFilterDateSelected(object sender, DateChangedEventArgs e)
    {
        _dateFilterEnabled = true;
        _dateFilter = e.NewDate;

        ShowPastSwitch.IsToggled = false;
        _showPast = false;

        BuildRows();
    }

    private void OnClearDateFilter(object sender, EventArgs e)
    {
        _dateFilterEnabled = false;
        _dateFilter = DateTime.Today;

        FilterDatePicker.Date = DateTime.Today;

        BuildRows();
    }

    private async void OnRowSelected(object sender, SelectionChangedEventArgs e)
    {
        var row = e.CurrentSelection?.FirstOrDefault() as AgendaRow;
        if (row == null) return;

        ((CollectionView)sender).SelectedItem = null;

        if (row.Kind == AgendaRowKind.Header || row.Event == null)
            return;

        await Navigation.PushAsync(new AgendaEventDetailsPage(_store, _db, _storage, row.Event));
    }
}
