using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class PlansPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;
    private readonly FirebaseStorageService _storage;

    // VM simples só para exibir dados extras no card
    public sealed class PlanCardVm
    {
        public LessonPlan Plan { get; set; } = new();

        public string Title { get; set; } = "";

        // thumb pronto para o XAML (offline-first)
        public ImageSource? ThumbSource { get; set; }

        public string IntervalText { get; set; } = "";
        public string SlotsText { get; set; } = "";
        public string LinkedLessonsText { get; set; } = "";
    }

    public ObservableCollection<PlanCardVm> Items { get; } = new();

    private string _uid = "";
    private string _token = "";

    private bool _modeAll = true;

    private readonly List<Institution> _institutions = new();
    private readonly List<Classroom> _classes = new();

    private Institution? _selectedInst;
    private Classroom? _selectedClass;

    public PlansPage(LocalStore store, FirebaseDbService db, FirebaseStorageService storage)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        _storage = storage;
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

    private static DateTime SafeDate(DateTime dt)
        => dt == default ? DateTime.Today : dt.Date;

    private ImageSource? BuildThumbSource(LessonPlan p)
    {
        try
        {
            // Offline-first
            var localPath = p.ThumbLocalPath ?? "";
            var url = p.ThumbUrl ?? "";

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                return ImageSource.FromFile(localPath);

            if (!string.IsNullOrWhiteSpace(url))
                return new UriImageSource { Uri = new Uri(url), CachingEnabled = true };

            return null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureSlotMigration(LessonPlan p)
    {
        p.Slots ??= new();
        foreach (var s in p.Slots)
        {
            // compat com seu model novo
            try { s.EnsureMigrated(); } catch { }
            s.Items ??= new();
        }
    }

    private PlanCardVm ToVm(LessonPlan p)
    {
        p.Slots ??= new();
        EnsureSlotMigration(p);

        // Intervalo: usa Start/End se existirem, senão cai no Date antigo
        var start = p.StartDate != default ? p.StartDate.Date : SafeDate(p.Date);
        var end = p.EndDate != default ? p.EndDate.Date : SafeDate(p.Date);
        if (end < start) end = start;

        var intervalText = start == end
            ? $"Intervalo: {start:dd/MM/yyyy}"
            : $"Intervalo: {start:dd/MM/yyyy} → {end:dd/MM/yyyy}";

        // Slots = dias
        var slotsCount = p.Slots.Count;

        // Aulas vinculadas = soma de itens com LessonId preenchido
        var linkedCount = 0;
        foreach (var day in p.Slots)
        {
            day.Items ??= new();
            linkedCount += day.Items.Count(it => !string.IsNullOrWhiteSpace(it.LessonId));
        }

        return new PlanCardVm
        {
            Plan = p,
            Title = string.IsNullOrWhiteSpace(p.Title) ? "(Sem título)" : p.Title.Trim(),
            ThumbSource = BuildThumbSource(p),
            IntervalText = intervalText,
            SlotsText = $"Slots: {slotsCount}",
            LinkedLessonsText = $"Aulas vinculadas: {linkedCount}"
        };
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
            InstitutionPicker.ItemsSource = new List<string>();
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

            if (_classes.Count > 0) ClassPicker.SelectedIndex = 0;
            else ClassPicker.SelectedIndex = -1;
        }
        catch
        {
            _classes.Clear();
            ClassPicker.ItemsSource = new List<string>();
            ClassPicker.SelectedIndex = -1;
        }
    }

    private IEnumerable<LessonPlan> SortPlans(IEnumerable<LessonPlan> plans)
    {
        // Sem UpdatedAt: ordena por CreatedAt e fallback Date
        return plans
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Date);
    }

    private async Task LoadCacheThenCloudAsync()
    {
        Items.Clear();

        if (_modeAll)
        {
            PickersBox.IsVisible = false;
            SubLabel.Text = "Geral (todos)";

            var cached = await _store.LoadPlansAllCacheAsync();
            if (cached != null)
            {
                foreach (var p in SortPlans(cached))
                    Items.Add(ToVm(p));
            }

            await LoadAllFromCloudAsync();
        }
        else
        {
            PickersBox.IsVisible = true;

            if (_selectedInst == null || _selectedClass == null)
            {
                SubLabel.Text = "Selecione a instituição e a turma";
                return;
            }

            SubLabel.Text = $"{_selectedInst.Name} • {_selectedClass.Name}";

            var cached = await _store.LoadPlansClassCacheAsync(_selectedInst.Id, _selectedClass.Id);
            if (cached != null)
            {
                foreach (var p in SortPlans(cached))
                    Items.Add(ToVm(p));
            }

            await LoadClassFromCloudAsync(_selectedInst.Id, _selectedClass.Id);
        }
    }

    private async Task LoadAllFromCloudAsync()
    {
        try
        {
            var list = await _db.GetPlansAllAsync(_uid, _token);
            Items.Clear();
            foreach (var p in SortPlans(list))
                Items.Add(ToVm(p));

            await _store.SavePlansAllCacheAsync(list);
        }
        catch { }
    }

    private async Task LoadClassFromCloudAsync(string institutionId, string classId)
    {
        try
        {
            var list = await _db.GetPlansByClassAsync(_uid, institutionId, classId, _token);
            Items.Clear();
            foreach (var p in SortPlans(list))
                Items.Add(ToVm(p));

            await _store.SavePlansClassCacheAsync(institutionId, classId, list);
        }
        catch { }
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
        if (InstitutionPicker.SelectedIndex < 0 || InstitutionPicker.SelectedIndex >= _institutions.Count) return;

        _selectedInst = _institutions[InstitutionPicker.SelectedIndex];
        await LoadClassesAsync(_selectedInst.Id);

        if (ClassPicker.SelectedIndex >= 0 && ClassPicker.SelectedIndex < _classes.Count)
            _selectedClass = _classes[ClassPicker.SelectedIndex];
        else
            _selectedClass = null;

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
    {
        await RefreshCurrentModeAsync();
    }

    // ======= ADD/EDIT =======

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var plan = new LessonPlan
        {
            Date = DateTime.Today,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today,
            CreatedAt = DateTimeOffset.UtcNow,
            Slots = new List<LessonSlot>()
        };

        // se estiver em modo turma, já fixa vínculo
        if (!_modeAll && _selectedInst != null && _selectedClass != null)
        {
            plan.InstitutionId = _selectedInst.Id;
            plan.InstitutionName = _selectedInst.Name;
            plan.ClassId = _selectedClass.Id;
            plan.ClassName = _selectedClass.Name;
        }

        await Navigation.PushModalAsync(new PlanEditorPage(_db, _store, plan));
        await RefreshCurrentModeAsync();
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not PlanCardVm vm) return;

        await Navigation.PushModalAsync(new PlanEditorPage(_db, _store, vm.Plan));
        await RefreshCurrentModeAsync();
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not PlanCardVm vm) return;

        var plan = vm.Plan;

        var confirm = await DisplayAlert("Excluir", $"Excluir \"{plan.Title}\"?", "Sim", "Não");
        if (!confirm) return;

        await _db.DeletePlanAllAsync(_uid, _token, plan.Id);

        if (!string.IsNullOrWhiteSpace(plan.InstitutionId) && !string.IsNullOrWhiteSpace(plan.ClassId))
        {
            await _db.DeletePlanByClassAsync(_uid, plan.InstitutionId, plan.ClassId, _token, plan.Id);
        }

        Items.Remove(vm);
        await RefreshCurrentModeAsync();
    }

    // seleção abre detalhes
    private async void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        var vm = e.CurrentSelection?.FirstOrDefault() as PlanCardVm;
        if (vm == null) return;

        ((CollectionView)sender).SelectedItem = null;
        await Navigation.PushAsync(new PlanDetailsPage(_store, _db, _storage, vm.Plan));
    }
}
