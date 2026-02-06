using System.Text.Json;
using ProfeMaster.Config;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class ExamEditorPage : ContentPage
{
    private readonly FirebaseDbService _db;
    private readonly LocalStore _store;
    private readonly GroqQuizService _quizSvc;

    private readonly bool _isExisting;
    private ExamItem _item;

    private string _uid = "";
    private string _token = "";

    private const string UpgradeRoute = "upgrade"; // ajuste se sua rota for "//upgrade"

    public ExamEditorPage(FirebaseDbService db, LocalStore store, GroqQuizService quizSvc, ExamItem item)
    {
        InitializeComponent();

        _db = db;
        _store = store;
        _quizSvc = quizSvc;
        _item = item;

        _isExisting = LooksExisting(_item);

        TitleEntry.Text = _item.Title;
        DescEditor.Text = _item.Description;

        RefreshThumb();
        RefreshQuizUI();
        RefreshPrimaryActionsUI();
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

        RefreshQuizUI();
        RefreshPrimaryActionsUI();
    }

    private static int GetPlanMaxQuestions()
    {
        return AppFlags.CurrentPlan switch
        {
            PlanTier.Free => 5,
            PlanTier.Premium => 10,
            PlanTier.SuperPremium => 10,
            _ => 5
        };
    }

    private static string GetPlanLabel()
    {
        return AppFlags.CurrentPlan switch
        {
            PlanTier.SuperPremium => "SuperPremium",
            PlanTier.Premium => "Premium",
            _ => "Free"
        };
    }

    private static bool LooksExisting(ExamItem it)
    {
        if (it == null) return false;

        if (!string.IsNullOrWhiteSpace(it.Title)) return true;
        if (!string.IsNullOrWhiteSpace(it.Description)) return true;
        if (!string.IsNullOrWhiteSpace(it.ThumbLocalPath)) return true;
        if (!string.IsNullOrWhiteSpace(it.ThumbUrl)) return true;

        var quizProp = it.GetType().GetProperty("QuizJson");
        if (quizProp != null)
        {
            var v = quizProp.GetValue(it) as string;
            if (!string.IsNullOrWhiteSpace(v)) return true;
        }

        try
        {
            var diff = DateTimeOffset.UtcNow - it.CreatedAt;
            if (diff.Duration() > TimeSpan.FromMinutes(2))
                return true;
        }
        catch { }

        return false;
    }

    private void RefreshPrimaryActionsUI()
    {
        if (_isExisting)
        {
            LeftActionBtn.Text = "Excluir prova";
            LeftActionBtn.Clicked -= OnCancel;
            LeftActionBtn.Clicked -= OnDeleteExam;
            LeftActionBtn.Clicked += OnDeleteExam;
        }
        else
        {
            LeftActionBtn.Text = "Cancelar";
            LeftActionBtn.Clicked -= OnDeleteExam;
            LeftActionBtn.Clicked -= OnCancel;
            LeftActionBtn.Clicked += OnCancel;
        }
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
        catch
        {
            ThumbPreview.Source = null;
        }
    }

    private void RefreshQuizUI()
    {
        var quizJson = GetQuizJson(_item);
        var hasQuiz = !string.IsNullOrWhiteSpace(quizJson);

        var maxQ = GetPlanMaxQuestions();
        var plan = GetPlanLabel();

        // tenta extrair contagem (sem quebrar)
        var qCount = TryGetQuizQuestionCount(quizJson);

        if (hasQuiz)
        {
            if (qCount > maxQ && qCount > 0)
            {
                QuizStatusLabel.Text = $"Quiz anexado ({qCount} perguntas). Seu plano {plan} permite até {maxQ}.";
            }
            else if (qCount > 0)
            {
                QuizStatusLabel.Text = $"Quiz anexado ({qCount} perguntas).";
            }
            else
            {
                QuizStatusLabel.Text = "Quiz já criado e anexado a esta prova.";
            }
        }
        else
        {
            QuizStatusLabel.Text = $"Nenhum quiz anexado ainda. Plano {plan}: até {maxQ} perguntas por quiz.";
        }

        QuizActionsRow.IsVisible = hasQuiz;

        BtnCreateQuiz.Text = hasQuiz ? "Criar novo quiz" : "Gerar quiz";

        BtnOpenQuiz.IsVisible = hasQuiz;
        BtnDeleteQuiz.IsVisible = hasQuiz;
    }

    private static int TryGetQuizQuestionCount(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("questions", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                return arr.GetArrayLength();
            }
        }
        catch { }
        return 0;
    }

    // ===== Helpers para QuizJson
    private static string GetQuizJson(ExamItem it)
    {
        try
        {
            var p = it.GetType().GetProperty("QuizJson");
            return (p?.GetValue(it) as string) ?? "";
        }
        catch { return ""; }
    }

    private static void SetQuizJson(ExamItem it, string value)
    {
        try
        {
            var p = it.GetType().GetProperty("QuizJson");
            if (p != null && p.PropertyType == typeof(string))
                p.SetValue(it, value ?? "");
        }
        catch { }
    }

    private async void OnBack(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

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
        if (!EnsureSession())
            return;

        // >>> só informativo aqui: limite é aplicado dentro do QuizGeneratorPage
        var maxQ = GetPlanMaxQuestions();
        var plan = GetPlanLabel();

        var currentQuiz = GetQuizJson(_item);
        var hasQuiz = !string.IsNullOrWhiteSpace(currentQuiz);

        if (hasQuiz)
        {
            var confirm = await DisplayAlert(
                "Criar novo quiz",
                "Já existe um quiz anexado. Criar um novo irá substituir o quiz atual. Deseja continuar?",
                "Sim, substituir",
                "Cancelar");

            if (!confirm)
                return;
        }

        var page = new QuizGeneratorPage(_quizSvc, async (json) =>
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            // segurança: se vier acima do limite, bloqueia e manda pro upgrade
            var qCount = TryGetQuizQuestionCount(json);
            if (qCount > maxQ)
            {
                var go = await DisplayAlert(
                    "Limite do plano",
                    $"Seu plano {plan} permite até {maxQ} pergunta(s) por quiz. Esse quiz veio com {qCount}.",
                    "Upgrade",
                    "OK"
                );

                if (go)
                {
                    try { await Shell.Current.GoToAsync(UpgradeRoute); } catch { }
                }
                return;
            }

            SetQuizJson(_item, json);

            MainThread.BeginInvokeOnMainThread(RefreshQuizUI);
            await TryAutoSaveExamAsync();
        });

        await Navigation.PushModalAsync(page);

        // feedback leve
        await DisplayAlert("Plano", $"Plano {plan}: até {maxQ} pergunta(s) por quiz.", "OK");
    }

    private async void OnOpenQuiz(object sender, EventArgs e)
    {
        if (!EnsureSession())
            return;

        var quizJson = GetQuizJson(_item);

        if (string.IsNullOrWhiteSpace(quizJson))
        {
            await DisplayAlert("Aviso", "Ainda não há quiz anexado.", "OK");
            RefreshQuizUI();
            return;
        }

        var maxQ = GetPlanMaxQuestions();
        var plan = GetPlanLabel();

        var page = new QuizGeneratorPage(_quizSvc, async (json) =>
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            var qCount = TryGetQuizQuestionCount(json);
            if (qCount > maxQ)
            {
                var go = await DisplayAlert(
                    "Limite do plano",
                    $"Seu plano {plan} permite até {maxQ} pergunta(s) por quiz. Esse quiz veio com {qCount}.",
                    "Upgrade",
                    "OK"
                );

                if (go)
                {
                    try { await Shell.Current.GoToAsync(UpgradeRoute); } catch { }
                }
                return;
            }

            SetQuizJson(_item, json);

            MainThread.BeginInvokeOnMainThread(RefreshQuizUI);
            await TryAutoSaveExamAsync();
        });

        page.LoadFromJson(quizJson);
        await Navigation.PushModalAsync(page);
    }

    private async void OnDeleteQuiz(object sender, EventArgs e)
    {
        if (!EnsureSession())
            return;

        var quizJson = GetQuizJson(_item);
        if (string.IsNullOrWhiteSpace(quizJson))
        {
            RefreshQuizUI();
            return;
        }

        var confirm = await DisplayAlert(
            "Excluir quiz",
            "Deseja remover o quiz anexado desta prova? Essa ação não pode ser desfeita.",
            "Excluir",
            "Cancelar");

        if (!confirm) return;

        SetQuizJson(_item, "");
        RefreshQuizUI();

        await TryAutoSaveExamAsync();
    }

    private bool EnsureSession()
    {
        if (!string.IsNullOrWhiteSpace(_uid) && !string.IsNullOrWhiteSpace(_token))
            return true;

        DisplayAlert("Erro", "Sessão inválida. Faça login novamente.", "OK");
        Shell.Current.GoToAsync("//login");
        return false;
    }

    private async Task TryAutoSaveExamAsync()
    {
        try
        {
            var title = (TitleEntry.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            _item.Title = title;
            _item.Description = (DescEditor.Text ?? "").Trim();

            await _db.UpsertExamAsync(_uid, _token, _item);
        }
        catch
        {
            // silencioso
        }
    }

    private async void OnCancel(object sender, EventArgs e)
        => await Navigation.PopModalAsync();

    private async void OnDeleteExam(object sender, EventArgs e)
    {
        if (!EnsureSession())
            return;

        var confirm = await DisplayAlert(
            "Excluir prova",
            "Deseja excluir esta prova? Essa ação não pode ser desfeita.",
            "Excluir",
            "Cancelar");

        if (!confirm) return;

        try
        {
            var ok = await _db.DeleteExamAsync(_uid, _token, _item.Id);

            if (!ok)
            {
                await DisplayAlert("Erro", "Não foi possível excluir a prova.", "OK");
                return;
            }

            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", "Falha ao excluir:\n" + ex.Message, "OK");
        }
    }

    private async void OnSave(object sender, EventArgs e)
    {
        var title = (TitleEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            await DisplayAlert("Erro", "Informe o título da prova.", "OK");
            return;
        }

        if (!EnsureSession())
            return;

        // segurança: se o quiz anexado estiver acima do limite, bloqueia salvar (evita “burlar”)
        var quizJson = GetQuizJson(_item);
        if (!string.IsNullOrWhiteSpace(quizJson))
        {
            var maxQ = GetPlanMaxQuestions();
            var plan = GetPlanLabel();
            var qCount = TryGetQuizQuestionCount(quizJson);

            if (qCount > maxQ)
            {
                var go = await DisplayAlert(
                    "Limite do plano",
                    $"Seu plano {plan} permite até {maxQ} pergunta(s) por quiz. Esse quiz tem {qCount}.",
                    "Upgrade",
                    "OK"
                );

                if (go)
                {
                    try { await Shell.Current.GoToAsync(UpgradeRoute); } catch { }
                }
                return;
            }
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
