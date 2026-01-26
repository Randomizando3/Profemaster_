using ProfeMaster.Config;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class LessonEditorPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;
    private readonly FirebaseStorageService _storage;

    private Lesson _lesson;
    private string _uid = "";
    private string _token = "";

    private readonly List<Institution> _institutions = new();
    private readonly List<Classroom> _classes = new();
    private const string CreateNewClassLabel = "+ Criar nova turma...";

    public LessonEditorPage(FirebaseDbService db, LocalStore store, FirebaseStorageService storage, Lesson lesson)
    {
        InitializeComponent();
        _db = db;
        _store = store;
        _storage = storage;
        _lesson = lesson;

        UploadBtn.IsVisible = AppFlags.EnableStorageUploads;

        TitleEntry.Text = _lesson.Title ?? "";
        DescEditor.Text = _lesson.Description ?? "";
        DurationEntry.Text = (_lesson.DurationMinutes <= 0 ? 50 : _lesson.DurationMinutes).ToString();

        _lesson.MaterialsV2 ??= new();
        RenderMaterials();

        RefreshThumbPreview();
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

    private void RenderMaterials()
    {
        _lesson.MaterialsV2 ??= new();
        MatList.ItemsSource = null;
        MatList.ItemsSource = _lesson.MaterialsV2.OrderByDescending(x => x.CreatedAt).ToList();
    }

    private void RefreshThumbPreview()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_lesson.ThumbLocalPath) && File.Exists(_lesson.ThumbLocalPath))
                ThumbPreview.Source = ImageSource.FromFile(_lesson.ThumbLocalPath);
            else
                ThumbPreview.Source = null;
        }
        catch
        {
            ThumbPreview.Source = null;
        }
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

            if (selectExisting && !string.IsNullOrWhiteSpace(_lesson.InstitutionId))
            {
                var idx = _institutions.FindIndex(x => x.Id == _lesson.InstitutionId);
                InstitutionPicker.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else if (InstitutionPicker.SelectedIndex < 0)
            {
                InstitutionPicker.SelectedIndex = 0;
            }

            await LoadClassesAsync(selectExisting);
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

            if (selectExisting && !string.IsNullOrWhiteSpace(_lesson.ClassId))
            {
                var idx = _classes.FindIndex(x => x.Id == _lesson.ClassId);
                ClassPicker.SelectedIndex = idx >= 0 ? idx : (_classes.Count > 0 ? 0 : -1);
            }
            else
            {
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
        => await LoadClassesAsync(selectExisting: false);

    private async void OnClassChanged(object sender, EventArgs e)
    {
        if (ClassPicker.SelectedIndex < 0) return;

        var items = ClassPicker.ItemsSource as IList<string>;
        if (items == null || items.Count == 0) return;

        if (items[ClassPicker.SelectedIndex] == CreateNewClassLabel)
        {
            ClassPicker.SelectedIndex = _classes.Count > 0 ? 0 : -1;
            await Shell.Current.GoToAsync("classes");
        }
    }

    private async void OnPickThumb(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione uma imagem (thumb)",
                FileTypes = FilePickerFileType.Images
            });

            if (result == null) return;

            var ext = Path.GetExtension(result.FileName);
            var dest = Path.Combine(FileSystem.AppDataDirectory, $"lesson_thumb_{Guid.NewGuid():N}{ext}");

            await using var src = await result.OpenReadAsync();
            await using var dst = File.OpenWrite(dest);
            await src.CopyToAsync(dst);

            _lesson.ThumbLocalPath = dest;
            RefreshThumbPreview();
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível selecionar a imagem.", "OK");
        }
    }

    private void OnRemoveThumb(object sender, EventArgs e)
    {
        _lesson.ThumbLocalPath = "";
        _lesson.ThumbUrl = "";
        RefreshThumbPreview();
    }

    private async void OnCancel(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    // ===== Materiais =====

    private async void OnAddLink(object sender, EventArgs e)
    {
        var title = await DisplayPromptAsync("Novo link", "Título:", "Salvar", "Cancelar", "Ex: Vídeo aula");
        if (string.IsNullOrWhiteSpace(title)) return;

        var url = await DisplayPromptAsync("Novo link", "URL:", "Salvar", "Cancelar", "https://");
        if (string.IsNullOrWhiteSpace(url)) return;

        _lesson.MaterialsV2 ??= new();
        _lesson.MaterialsV2.Add(new LessonMaterial
        {
            Kind = MaterialKind.Link,
            Title = title.Trim(),
            Url = url.Trim()
        });

        RenderMaterials();
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
            // Garante Id antes de montar o path
            _lesson.Id = string.IsNullOrWhiteSpace(_lesson.Id) ? Guid.NewGuid().ToString("N") : _lesson.Id;

            // Aceita qualquer arquivo
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione um arquivo",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "*/*" } },
                    { DevicePlatform.iOS, new[] { "public.item" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.item" } },
                    { DevicePlatform.WinUI, new[] { "*" } }
                })
            });

            if (file == null) return;

            // Copia para MemoryStream para evitar instabilidades de stream no Android
            await using var src = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await src.CopyToAsync(ms);
            ms.Position = 0;

            var storagePath = $"users/{_uid}/lessons/{_lesson.Id}/{file.FileName}";
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

            var (ok, dl, path, err) = await _storage.UploadAsync(_token, storagePath, file.FileName, contentType, ms);
            if (!ok)
            {
                await DisplayAlert("Erro", err, "OK");
                return;
            }

            _lesson.MaterialsV2 ??= new();
            _lesson.MaterialsV2.Add(new LessonMaterial
            {
                Kind = MaterialKind.StorageFile,
                Title = file.FileName,
                Url = dl,
                StoragePath = path,
                ContentType = contentType
            });

            RenderMaterials();
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

        _lesson.MaterialsV2 ??= new();
        _lesson.MaterialsV2.Remove(m);

        RenderMaterials();
    }

    // ===== Save =====

    private async void OnSave(object sender, EventArgs e)
    {
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

        if (ClassPicker.SelectedIndex < 0 || ClassPicker.SelectedIndex >= _classes.Count)
        {
            await DisplayAlert("Erro", "Selecione uma turma.", "OK");
            return;
        }

        var inst = _institutions[InstitutionPicker.SelectedIndex];
        var cls = _classes[ClassPicker.SelectedIndex];

        _lesson.Title = title;
        _lesson.Description = (DescEditor.Text ?? "").Trim();

        if (!int.TryParse((DurationEntry.Text ?? "").Trim(), out var mins) || mins <= 0)
            mins = 50;
        _lesson.DurationMinutes = mins;

        _lesson.InstitutionId = inst.Id;
        _lesson.InstitutionName = inst.Name;
        _lesson.ClassId = cls.Id;
        _lesson.ClassName = cls.Name;

        _lesson.MaterialsV2 ??= new();
        _lesson.UpdatedAt = DateTimeOffset.UtcNow;

        _lesson.Id = string.IsNullOrWhiteSpace(_lesson.Id) ? Guid.NewGuid().ToString("N") : _lesson.Id;

        var okAll = await _db.UpsertLessonAllAsync(_uid, _token, _lesson);
        if (!okAll)
        {
            await DisplayAlert("Erro", "Falha ao salvar (all).", "OK");
            return;
        }

        await _db.UpsertLessonByClassAsync(_uid, _lesson.InstitutionId, _lesson.ClassId, _token, _lesson);

        await Navigation.PopModalAsync();
    }
}
