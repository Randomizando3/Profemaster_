using System.Collections.ObjectModel;
using System.Text;
using ProfeMaster.Config;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

[QueryProperty(nameof(InstitutionId), "institutionId")]
[QueryProperty(nameof(InstitutionName), "institutionName")]
[QueryProperty(nameof(ClassId), "classId")]
[QueryProperty(nameof(ClassName), "className")]
public partial class StudentsPage : ContentPage
{
    private readonly LocalStore _store;
    private readonly FirebaseDbService _db;

    public ObservableCollection<StudentContact> Students { get; } = new();

    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";

    private string _uid = "";
    private string _token = "";

    // Editor state
    private bool _editorIsEdit = false;
    private StudentContact? _editing;

    // Whats state
    private StudentContact? _whatsTarget;
    private List<Lesson> _lessons = new();
    private bool _lessonsLoaded = false;

    public StudentsPage(LocalStore store, FirebaseDbService db)
    {
        InitializeComponent();
        _store = store;
        _db = db;
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var ctxA = (InstitutionName ?? "").Trim();
        var ctxB = (ClassName ?? "").Trim();
        CtxLabel.Text = string.IsNullOrWhiteSpace(ctxA) && string.IsNullOrWhiteSpace(ctxB)
            ? ""
            : $"{ctxA} • {ctxB}".Trim(' ', '•');

        var session = await _store.LoadSessionAsync();
        if (session == null || string.IsNullOrWhiteSpace(session.Uid))
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        _uid = session.Uid;
        _token = session.IdToken;

        // aplica override dev (se você configurou no AppFlags)
        AppFlags.TryApplyDevOverride(_uid);

        // cache primeiro
        if (!string.IsNullOrWhiteSpace(InstitutionId) && !string.IsNullOrWhiteSpace(ClassId))
        {
            var cached = await _store.LoadStudentsCacheAsync(InstitutionId, ClassId);
            if (cached != null && cached.Count > 0 && Students.Count == 0)
            {
                foreach (var s in cached) Students.Add(s);
            }

            await LoadFromCloudAsync();
        }
    }

    private async Task LoadFromCloudAsync()
    {
        try
        {
            var list = await _db.GetStudentsAsync(_uid, InstitutionId, ClassId, _token);
            Students.Clear();
            foreach (var s in list) Students.Add(s);
            await _store.SaveStudentsCacheAsync(InstitutionId, ClassId, list);
        }
        catch
        {
            // mantém cache
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadFromCloudAsync();

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        if (Navigation.ModalStack.Count > 0)
            await Navigation.PopModalAsync();
        else
            await Shell.Current.GoToAsync("..");
    }

    // =========================
    // LIMITES (Alunos por turma)
    // =========================
    private static int GetStudentsPerClassLimit(PlanTier tier) => tier switch
    {
        PlanTier.SuperPremium => int.MaxValue,
        PlanTier.Premium => 50,
        _ => 5
    };

    private async Task<bool> EnsureCanCreateStudentAsync()
    {
        var limit = GetStudentsPerClassLimit(AppFlags.CurrentPlan);
        if (limit == int.MaxValue) return true;

        if (Students.Count + 1 <= limit) return true;

        var planName = AppFlags.CurrentPlan == PlanTier.Free ? "Grátis" : "Premium";
        var msg = $"Você atingiu o limite do plano {planName}.\n\n" +
                  $"• Limite atual: {limit} alunos por turma\n" +
                  $"• Para adicionar mais, faça upgrade.";

        var go = await DisplayAlert("Limite atingido", msg, "Ver planos", "Cancelar");
        if (go)
            await Shell.Current.GoToAsync("upgrade");

        return false;
    }

    // =========================
    // Editor (modal overlay)
    // =========================
    private void OpenEditor(bool isEdit, StudentContact? st)
    {
        _editorIsEdit = isEdit;
        _editing = st;

        EditorTitleLabel.Text = isEdit ? "Editar aluno/contato" : "Novo aluno/contato";

        if (isEdit && st != null)
        {
            NameEntry.Text = st.Name ?? "";
            PhoneEntry.Text = st.Phone ?? "";
            EmailEntry.Text = st.Email ?? "";
            NotesEditor.Text = st.Notes ?? "";
        }
        else
        {
            NameEntry.Text = "";
            PhoneEntry.Text = "";
            EmailEntry.Text = "";
            NotesEditor.Text = "";
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

    private async void OnAddClicked(object sender, EventArgs e)
    {
        if (!await EnsureCanCreateStudentAsync()) return;
        OpenEditor(isEdit: false, st: null);
    }

    private void OnEditClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not StudentContact st) return;

        OpenEditor(isEdit: true, st: st);
    }

    private async void OnEditorSave(object sender, EventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Erro", "Informe o nome.", "OK");
            return;
        }

        var phone = (PhoneEntry.Text ?? "").Trim();
        var email = (EmailEntry.Text ?? "").Trim();
        var notes = (NotesEditor.Text ?? "").Trim();

        StudentContact st;
        var isEdit = _editorIsEdit && _editing != null;

        if (!isEdit)
        {
            if (!await EnsureCanCreateStudentAsync())
                return;
        }

        if (isEdit)
        {
            st = _editing!;
            st.Name = name;
            st.Phone = phone;
            st.Email = email;
            st.Notes = notes;
        }
        else
        {
            st = new StudentContact
            {
                InstitutionId = InstitutionId,
                ClassroomId = ClassId,
                Name = name,
                Phone = phone,
                Email = email,
                Notes = notes,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        var ok = await _db.UpsertStudentAsync(_uid, InstitutionId, ClassId, _token, st);
        if (!ok)
        {
            await DisplayAlert("Erro", isEdit ? "Não foi possível atualizar." : "Não foi possível salvar o aluno/contato.", "OK");
            return;
        }

        if (isEdit)
        {
            var idx = Students.IndexOf(st);
            if (idx >= 0)
            {
                Students.RemoveAt(idx);
                Students.Insert(idx, st);
            }
        }
        else
        {
            Students.Insert(0, st);
        }

        await _store.SaveStudentsCacheAsync(InstitutionId, ClassId, Students.ToList());
        CloseEditor();
    }

    // =========================
    // Delete
    // =========================
    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not StudentContact st) return;

        var confirm = await DisplayAlert("Excluir", $"Excluir \"{st.Name}\"?", "Sim", "Não");
        if (!confirm) return;

        var ok = await _db.DeleteStudentAsync(_uid, InstitutionId, ClassId, _token, st.Id);
        if (!ok)
        {
            await DisplayAlert("Erro", "Não foi possível excluir.", "OK");
            return;
        }

        Students.Remove(st);
        await _store.SaveStudentsCacheAsync(InstitutionId, ClassId, Students.ToList());
    }

    // =========================================================
    // WHATSAPP (NOVO) - modal + montagem da mensagem + abrir url
    // =========================================================
    private async Task EnsureLessonsLoadedAsync()
    {
        if (_lessonsLoaded) return;

        try
        {
            _lessons = await _db.GetLessonsAllAsync(_uid, _token);
            _lessons = _lessons
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();
        }
        catch
        {
            _lessons = new();
        }

        WhatsLessonPicker.ItemsSource = _lessons.Select(x => x.Title ?? "").ToList();
        WhatsLessonPicker.SelectedIndex = _lessons.Count > 0 ? 0 : -1;

        _lessonsLoaded = true;
    }

    private static string NormalizeBrPhone(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return "";

        // usuário normalmente cadastra 11999999999
        // regra: colocar 55 no início por padrão
        if (digits.StartsWith("55")) return digits;

        return "55" + digits;
    }

    private void OpenWhats(StudentContact st)
    {
        _whatsTarget = st;

        WhatsToLabel.Text = $"Para: {st.Name ?? "-"}";
        WhatsSubjectEntry.Text = "";
        WhatsMessageEditor.Text = "";

        // não fecha o editor de aluno; são overlays independentes
        WhatsOverlay.IsVisible = true;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(60);
            WhatsSubjectEntry.Focus();
        });
    }

    private void CloseWhats()
    {
        WhatsOverlay.IsVisible = false;
        _whatsTarget = null;
    }

    private async void OnWhatsClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.BindingContext is not StudentContact st) return;

        var phone = NormalizeBrPhone(st.Phone);
        if (string.IsNullOrWhiteSpace(phone))
        {
            await DisplayAlert("WhatsApp", "Esse aluno não tem telefone cadastrado.", "OK");
            return;
        }

        await EnsureLessonsLoadedAsync();
        OpenWhats(st);
    }

    private void OnWhatsCancel(object sender, EventArgs e) => CloseWhats();

    private async void OnWhatsSend(object sender, EventArgs e)
    {
        if (_whatsTarget == null)
        {
            CloseWhats();
            return;
        }

        var phone = NormalizeBrPhone(_whatsTarget.Phone);
        if (string.IsNullOrWhiteSpace(phone))
        {
            await DisplayAlert("WhatsApp", "Telefone inválido/vazio.", "OK");
            return;
        }

        var subject = (WhatsSubjectEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(subject))
        {
            await DisplayAlert("WhatsApp", "Informe o assunto.", "OK");
            return;
        }

        Lesson? lesson = null;
        if (WhatsLessonPicker.SelectedIndex >= 0 && WhatsLessonPicker.SelectedIndex < _lessons.Count)
            lesson = _lessons[WhatsLessonPicker.SelectedIndex];

        var lessonTitle = (lesson?.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(lessonTitle))
        {
            await DisplayAlert("WhatsApp", "Selecione uma aula.", "OK");
            return;
        }

        var msg = (WhatsMessageEditor.Text ?? "").Trim();

        // Materiais: pega links (Kind=Link) e arquivos (Kind=StorageFile) do MaterialsV2
        var links = new List<string>();
        try
        {
            if (lesson?.MaterialsV2 != null && lesson.MaterialsV2.Count > 0)
            {
                foreach (var m in lesson.MaterialsV2)
                {
                    if (!string.IsNullOrWhiteSpace(m?.Url))
                        links.Add(m.Url!.Trim());
                }
            }
        }
        catch
        {
            // ignora
        }

        var materials = links.Count == 0 ? "—" : string.Join(" | ", links);

        var finalText = $"Assunto: {subject} - Aula: {lessonTitle} - Materiais: {materials} - Mensagem: {msg}";
        var encoded = Uri.EscapeDataString(finalText);

        // api.whatsapp.com
        var url = $"https://api.whatsapp.com/send?phone={phone}&text={encoded}";

        try
        {
            await Launcher.Default.OpenAsync(url);
            CloseWhats();
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível abrir o WhatsApp.", "OK");
        }
    }
}
