using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class ExamEditorPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;
    private ExamItem _item;

    private string _uid = "";
    private string _token = "";

    public ExamEditorPage(FirebaseDbService db, LocalStore store, ExamItem item)
    {
        InitializeComponent();
        _db = db;
        _store = store;
        _item = item;

        TitleEntry.Text = _item.Title;
        DescEditor.Text = _item.Description;
        RefreshThumb();
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
    }

    private void RefreshThumb()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_item.ThumbLocalPath) && File.Exists(_item.ThumbLocalPath))
                ThumbPreview.Source = ImageSource.FromFile(_item.ThumbLocalPath);
            else
                ThumbPreview.Source = null;
        }
        catch { ThumbPreview.Source = null; }
    }

    private async void OnPickThumb(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione uma imagem",
                FileTypes = FilePickerFileType.Images
            });
            if (result == null) return;

            var ext = Path.GetExtension(result.FileName);
            var dest = Path.Combine(FileSystem.AppDataDirectory, $"exam_thumb_{Guid.NewGuid():N}{ext}");

            await using var src = await result.OpenReadAsync();
            await using var dst = File.OpenWrite(dest);
            await src.CopyToAsync(dst);

            _item.ThumbLocalPath = dest;
            RefreshThumb();
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível selecionar a imagem.", "OK");
        }
    }

    private void OnRemoveThumb(object sender, EventArgs e)
    {
        _item.ThumbLocalPath = "";
        _item.ThumbUrl = "";
        RefreshThumb();
    }

    private async void OnGeneratePlaceholder(object sender, EventArgs e)
    {
        await DisplayAlert("Em breve", "Aqui vamos ligar a geração de prova (IA ou banco local).", "OK");
    }

    private async void OnCancel(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    private async void OnSave(object sender, EventArgs e)
    {
        var title = (TitleEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Erro", "Informe o título da prova.", "OK");
            return;
        }

        _item.Title = title;
        _item.Description = (DescEditor.Text ?? "").Trim();

        var ok = await _db.UpsertExamAsync(_uid, _token, _item);
        if (!ok)
        {
            await DisplayAlert("Erro", "Falha ao salvar a prova.", "OK");
            return;
        }

        await Navigation.PopModalAsync();
    }
}
