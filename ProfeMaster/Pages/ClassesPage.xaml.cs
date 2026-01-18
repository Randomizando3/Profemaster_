using System.Collections.ObjectModel;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

[QueryProperty(nameof(InstitutionId), "institutionId")]
[QueryProperty(nameof(InstitutionName), "institutionName")]
public partial class ClassesPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    public ObservableCollection<Classroom> Classes { get; } = new();

    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";

    private string _uid = "";
    private string _token = "";

    // Editor state
    private bool _editorIsEdit = false;
    private Classroom? _editing;

    private readonly List<string> _periods = new()
    {
        "Manhã",
        "Tarde",
        "Noite",
        "Integral",
        "Outro"
    };

    public ClassesPage(LocalStore store, FirebaseDbService db)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        BindingContext = this;

        PeriodPicker.ItemsSource = _periods;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        InstLabel.Text = string.IsNullOrWhiteSpace(InstitutionName) ? "" : InstitutionName;

        var session = await _store.LoadSessionAsync();
        if (session == null || string.IsNullOrWhiteSpace(session.Uid))
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        _uid = session.Uid;
        _token = session.IdToken;

        if (!string.IsNullOrWhiteSpace(InstitutionId))
        {
            var cached = await _store.LoadClassesCacheAsync(InstitutionId);
            if (cached != null && cached.Count > 0 && Classes.Count == 0)
            {
                foreach (var c in cached) Classes.Add(c);
            }

            await LoadFromCloudAsync();
        }
    }

    private async Task LoadFromCloudAsync()
    {
        try
        {
            var list = await _db.GetClassesAsync(_uid, InstitutionId, _token);
            Classes.Clear();
            foreach (var c in list) Classes.Add(c);
            await _store.SaveClassesCacheAsync(InstitutionId, list);
        }
        catch
        {
            // mantém cache
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadFromCloudAsync();

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        // volta para página anterior (normalmente Institutions)
        if (Navigation.ModalStack.Count > 0)
            await Navigation.PopModalAsync();
        else
            await Shell.Current.GoToAsync("..");
    }

    // =========================
    // Editor (modal overlay)
    // =========================
    private void OpenEditor(bool isEdit, Classroom? cls)
    {
        _editorIsEdit = isEdit;
        _editing = cls;

        EditorTitleLabel.Text = isEdit ? "Editar turma" : "Nova turma";

        if (isEdit && cls != null)
        {
            NameEntry.Text = cls.Name ?? "";
            RoomEntry.Text = cls.Room ?? "";
            NotesEditor.Text = cls.Notes ?? "";

            // period -> index
            var p = (cls.Period ?? "").Trim();
            var idx = _periods.FindIndex(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
            PeriodPicker.SelectedIndex = idx >= 0 ? idx : -1;
        }
        else
        {
            NameEntry.Text = "";
            RoomEntry.Text = "";
            NotesEditor.Text = "";
            PeriodPicker.SelectedIndex = -1;
        }

        EditorOverlay.IsVisible = true;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(50);
            NameEntry.Focus();
        });
    }

    private void CloseEditor()
    {
        EditorOverlay.IsVisible = false;
    }

    private void OnEditorClose(object sender, EventArgs e) => CloseEditor();
    private void OnEditorCancel(object sender, EventArgs e) => CloseEditor();

    private void OnAddClicked(object sender, EventArgs e)
    {
        OpenEditor(isEdit: false, cls: null);
    }

    private void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Classroom cls) return;

        OpenEditor(isEdit: true, cls: cls);
    }

    private async void OnEditorSave(object sender, EventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Erro", "Informe o nome da turma.", "OK");
            return;
        }

        var period = "";
        if (PeriodPicker.SelectedIndex >= 0 && PeriodPicker.SelectedIndex < _periods.Count)
            period = _periods[PeriodPicker.SelectedIndex];

        var room = (RoomEntry.Text ?? "").Trim();
        var notes = (NotesEditor.Text ?? "").Trim();

        Classroom cls;
        var isEdit = _editorIsEdit && _editing != null;

        if (isEdit)
        {
            cls = _editing!;
            cls.Name = name;
            cls.Period = period;
            cls.Room = room;
            cls.Notes = notes;
        }
        else
        {
            cls = new Classroom
            {
                InstitutionId = InstitutionId,
                Name = name,
                Period = period,
                Room = room,
                Notes = notes,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        var ok = await _db.UpsertClassAsync(_uid, InstitutionId, _token, cls);
        if (!ok)
        {
            await DisplayAlert("Erro", isEdit ? "Não foi possível atualizar a turma." : "Não foi possível salvar a turma.", "OK");
            return;
        }

        if (isEdit)
        {
            var idx = Classes.IndexOf(cls);
            if (idx >= 0)
            {
                Classes.RemoveAt(idx);
                Classes.Insert(idx, cls);
            }
        }
        else
        {
            Classes.Insert(0, cls);
        }

        await _store.SaveClassesCacheAsync(InstitutionId, Classes.ToList());
        CloseEditor();
    }

    // =========================
    // Delete
    // =========================
    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Classroom cls) return;

        var confirm = await DisplayAlert("Excluir", $"Excluir a turma \"{cls.Name}\"?", "Sim", "Não");
        if (!confirm) return;

        var ok = await _db.DeleteClassAsync(_uid, InstitutionId, _token, cls.Id);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível excluir a turma.", "OK");
            return;
        }

        Classes.Remove(cls);
        await _store.SaveClassesCacheAsync(InstitutionId, Classes.ToList());
    }

    // =========================
    // Students
    // =========================
    private async void OnStudentsClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not Classroom cls) return;

        await Shell.Current.GoToAsync(
            $"students?institutionId={Uri.EscapeDataString(InstitutionId)}" +
            $"&institutionName={Uri.EscapeDataString(InstitutionName ?? "")}" +
            $"&classId={Uri.EscapeDataString(cls.Id)}" +
            $"&className={Uri.EscapeDataString(cls.Name)}"
        );
    }
}
