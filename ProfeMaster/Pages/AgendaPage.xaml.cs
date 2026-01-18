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

        ApplyModeVisual();
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

        ApplyModeVisual();

        await LoadInstitutionsAsync();
        await LoadCacheThenCloudAsync();
    }

    // ===== visual do segmentado (highlight + cores do texto) =====
    private void ApplyModeVisual()
    {
        if (SegHighlight == null || BtnAll == null || BtnClass == null) return;

        // move o highlight (fundo degradê)
        Grid.SetColumn(SegHighlight, _modeAll ? 0 : 1);

        // texto branco no selecionado, cinza no outro (igual print)
        BtnAll.TextColor = _modeAll ? Colors.White : Color.FromArgb("#6B6B6B");
        BtnClass.TextColor = _modeAll ? Color.FromArgb("#6B6B6B") : Colors.White;
    }

    private static string NormKind(string? s)
    {
        var v = (s ?? "").Trim();
        return string.IsNullOrWhiteSpace(v) ? "Aula" : v;
    }

    private static bool IsMultiDayKind(string kind)
    {
        return kind.Equals("Plano de aula", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("Plano", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("Evento", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(DateTime Day, ScheduleEvent Ev)> ExpandByDay(IEnumerable<ScheduleEvent> source)
    {
        foreach (var ev in source)
        {
            var kind = NormKind(ev.Kind);
            var s = ev.Start;
            var e = ev.End;

            if (e < s) (s, e) = (e, s);

            var startDay = s.Date;
            var endDay = e.Date;

            if (!IsMultiDayKind(kind) || startDay == endDay)
            {
                yield return (startDay, ev);
                continue;
            }

            for (var d = startDay; d <= endDay; d = d.AddDays(1))
                yield return (d, ev);
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

        var expanded = ExpandByDay(Items);

        if (filterDay.HasValue)
            expanded = expanded.Where(x => x.Day.Date == filterDay.Value);
        else
        {
            if (!_showPast)
                expanded = expanded.Where(x => x.Day.Date >= today);
        }

        var seq = expanded
            .OrderBy(x => x.Day)
            .ThenBy(x => x.Ev.Start.TimeOfDay)
            .ToList();

        if (seq.Count == 0)
        {
            EmptyLabel.IsVisible = _dateFilterEnabled;
            return;
        }

        EmptyLabel.IsVisible = false;

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
        ApplyModeVisual();
        await LoadCacheThenCloudAsync();
    }

    private async void OnModeClassClicked(object sender, EventArgs e)
    {
        _modeAll = false;
        ApplyModeVisual();

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
            Kind = "Aula",
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
