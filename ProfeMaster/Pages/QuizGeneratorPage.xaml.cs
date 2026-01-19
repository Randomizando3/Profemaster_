using System.Text.Json;
using System.Text.RegularExpressions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using ProfeMaster.Models;
using ProfeMaster.Services;

namespace ProfeMaster.Pages;

public partial class QuizGeneratorPage : ContentPage
{
    private readonly GroqQuizService _svc;
    private readonly Action<string?>? _onDone;

    private readonly QuizDocument _doc = new();
    private int _count = 5;
    private bool _busy;

    // Lista fixa do gabarito (para reuso no Picker dentro do DataTemplate)
    private static readonly List<string> AnswerOptions = new() { "A", "B", "C", "D" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public QuizGeneratorPage(GroqQuizService svc, Action<string?>? onDone = null)
    {
        InitializeComponent();
        _svc = svc;
        _onDone = onDone;

        // Dificuldade (Picker nativo)
        DifficultyPicker.ItemsSource = new List<string>
        {
            "Fundamental I",
            "Fundamental II",
            "Ensino Médio",
            "Vestibular"
        };
        DifficultyPicker.SelectedIndex = 2;
        DifficultyPicker.SelectedIndexChanged += (_, __) => SyncDifficultyLabel();
        SyncDifficultyLabel();

        QuestionsList.ItemsSource = _doc.Questions;

        SetCount(_count, updateStatus: false);

        _ = UpdateNetworkLabelAsync();
        UpdateEmpty();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await UpdateNetworkLabelAsync();
        SyncDifficultyLabel();
    }

    private async Task UpdateNetworkLabelAsync()
    {
        bool hasNet = await _svc.HasInternetAsync();
        NetLabel.Text = hasNet ? "Online disponível" : "Sem internet (offline ainda será ligado depois)";
    }

    private void SyncDifficultyLabel()
    {
        var v = (DifficultyPicker.SelectedItem as string);
        if (string.IsNullOrWhiteSpace(v))
            v = "Ensino Médio";

        if (DifficultyValueLabel != null)
            DifficultyValueLabel.Text = v;
    }

    private void SetCount(int value, bool updateStatus)
    {
        if (value < 1) value = 1;
        if (value > 10) value = 10;

        _count = value;

        if (CountLabel != null)
            CountLabel.Text = _count.ToString();

        if (updateStatus && StatusLabel != null)
            StatusLabel.Text = $"Quantidade: {_count}";
    }

    private void OnCountMinus(object sender, EventArgs e)
    {
        if (_busy) return;
        SetCount(_count - 1, updateStatus: true);
    }

    private void OnCountPlus(object sender, EventArgs e)
    {
        if (_busy) return;
        SetCount(_count + 1, updateStatus: true);
    }

    private void OnDifficultyTap(object sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { DifficultyPicker.Focus(); } catch { }
        });
    }

    /// <summary>
    /// Permite abrir/editar um quiz já existente (JSON salvo no ExamItem.QuizJson).
    /// Chamado pelo ExamEditorPage.
    /// </summary>
    public void LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<QuizDocument>(json, JsonOpts);
            if (loaded == null) return;

            _doc.Theme = loaded.Theme ?? "";
            _doc.Difficulty = loaded.Difficulty ?? "Ensino Médio";
            _doc.GeneratedAt = loaded.GeneratedAt;

            _doc.Questions.Clear();
            if (loaded.Questions != null)
            {
                foreach (var q in loaded.Questions)
                {
                    if (q == null) continue;

                    q.Prompt = q.Prompt ?? "";
                    q.A = q.A ?? "";
                    q.B = q.B ?? "";
                    q.C = q.C ?? "";
                    q.D = q.D ?? "";

                    q.Answer = NormalizeAnswer(q.Answer);

                    _doc.Questions.Add(q);
                }
            }

            ReNumber();
            UpdateEmpty();

            ThemeEntry.Text = _doc.Theme;

            // Seleciona dificuldade na lista
            if (DifficultyPicker.ItemsSource is List<string> diffList)
            {
                var idx = diffList.FindIndex(x => string.Equals(x, _doc.Difficulty, StringComparison.OrdinalIgnoreCase));
                DifficultyPicker.SelectedIndex = idx >= 0 ? idx : 2;
            }
            else
            {
                DifficultyPicker.SelectedIndex = 2;
            }

            SyncDifficultyLabel();

            // Ajusta quantidade conforme quiz carregado
            var cnt = _doc.Questions.Count;
            if (cnt < 1) cnt = 1;
            if (cnt > 10) cnt = 10;

            SetCount(cnt, updateStatus: false);

            StatusLabel.Text = _doc.Questions.Count > 0
                ? $"Quiz carregado: {_doc.Questions.Count} pergunta(s)."
                : "Quiz carregado, mas sem perguntas.";
        }
        catch
        {
            StatusLabel.Text = "Não foi possível carregar o quiz existente (JSON inválido).";
        }
    }

    private async void OnGenerate(object sender, EventArgs e)
    {
        if (_busy) return;

        var theme = (ThemeEntry.Text ?? "").Trim();
        var baseText = (BaseEditor.Text ?? "").Trim();
        var difficulty = (DifficultyPicker.SelectedItem as string) ?? "Ensino Médio";

        if (string.IsNullOrWhiteSpace(theme))
        {
            await DisplayAlert("Atenção", "Informe o tema/assunto.", "OK");
            return;
        }

        SetBusy(true, "Gerando perguntas...");

        try
        {
            _doc.Theme = theme;
            _doc.Difficulty = difficulty;
            _doc.GeneratedAt = DateTimeOffset.UtcNow;

            _doc.Questions.Clear();
            QuestionsList.ItemsSource = null;
            QuestionsList.ItemsSource = _doc.Questions;

            var avoid = new List<string>();

            for (int i = 1; i <= _count; i++)
            {
                StatusLabel.Text = $"Gerando {i}/{_count}...";

                QuizQuestion? q = null;

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    q = await _svc.GenerateOneAsync(theme, baseText, difficulty, avoid, cts.Token);

                    if (q == null) continue;

                    q.Prompt = q.Prompt ?? "";
                    q.A = q.A ?? "";
                    q.B = q.B ?? "";
                    q.C = q.C ?? "";
                    q.D = q.D ?? "";

                    q.Answer = NormalizeAnswer(q.Answer);

                    var first = FirstSentence(q.Prompt);
                    if (!string.IsNullOrWhiteSpace(first))
                    {
                        var norm = Normalize(first);
                        bool repeated = avoid.Any(a => Normalize(a) == norm);
                        if (!repeated)
                        {
                            avoid.Add(first);
                            break;
                        }
                    }
                }

                if (q == null)
                    continue;

                q.Number = i;
                _doc.Questions.Add(q);
            }

            ReNumber();
            UpdateEmpty();

            StatusLabel.Text = _doc.Questions.Count > 0
                ? $"Pronto: {_doc.Questions.Count} pergunta(s) gerada(s)."
                : "Não foi possível gerar perguntas (verifique internet/chave).";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", "Falha ao gerar o quiz:\n" + ex.Message, "OK");
            StatusLabel.Text = "Erro ao gerar.";
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private void OnClear(object sender, EventArgs e)
    {
        if (_busy) return;

        _doc.Questions.Clear();
        ReNumber();
        UpdateEmpty();
        StatusLabel.Text = "Lista limpa.";
    }

    private async void OnShowJson(object sender, EventArgs e)
    {
        try
        {
            var json = BuildQuizJson() ?? "";
            await DisplayAlert("JSON do Quiz", json.Length > 3900 ? json[..3900] + "..." : json, "OK");
        }
        catch
        {
            await DisplayAlert("Erro", "Não foi possível gerar JSON.", "OK");
        }
    }

    private void OnRemoveQuestion(object sender, EventArgs e)
    {
        if (_busy) return;

        if (sender is not Button b) return;
        if (b.CommandParameter is not QuizQuestion q) return;

        _doc.Questions.Remove(q);
        ReNumber();
        UpdateEmpty();
    }

    private async void OnExportQuestionsPdf(object sender, EventArgs e)
        => await ExportPdfAsync(isAnswerKey: false);

    private async void OnExportAnswersPdf(object sender, EventArgs e)
        => await ExportPdfAsync(isAnswerKey: true);

    // ===== FIX: garante gabarito válido antes de exportar =====
    private static void EnsureValidAnswers(QuizDocument doc)
    {
        foreach (var q in doc.Questions)
            q.Answer = NormalizeAnswer(q.Answer);
    }

    private async Task ExportPdfAsync(bool isAnswerKey)
    {
        if (_busy) return;

        if (_doc.Questions.Count == 0)
        {
            await DisplayAlert("Atenção", "Não há perguntas para exportar.", "OK");
            return;
        }

        try
        {
            SetBusy(true, "Gerando PDF...");

            // FIX: antes de exportar, normaliza todos os gabaritos
            EnsureValidAnswers(_doc);

            var filename = isAnswerKey ? "quiz_gabarito.pdf" : "quiz_questoes.pdf";
            var path = Path.Combine(FileSystem.AppDataDirectory, filename);

            CreatePdf(path, isAnswerKey);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = isAnswerKey ? "Gabarito (PDF)" : "Questões (PDF)",
                File = new ShareFile(path)
            });

            StatusLabel.Text = "PDF gerado e pronto para compartilhar.";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", "Falha ao exportar PDF:\n" + ex.Message, "OK");
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private void CreatePdf(string path, bool isAnswerKey)
    {
        var document = new PdfDocument();
        document.Info.Title = isAnswerKey ? "Gabarito" : "Questionário";

        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);

        var fontTitle = new XFont("Helvetica", 14, XFontStyle.Bold);
        var font = new XFont("Helvetica", 11, XFontStyle.Regular);
        var fontSmall = new XFont("Helvetica", 10, XFontStyle.Regular);

        double margin = 40;
        double y = margin;

        void NewPage()
        {
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            y = margin;
        }

        gfx.DrawString(isAnswerKey ? "GABARITO" : "QUESTIONÁRIO", fontTitle, XBrushes.Black,
            new XRect(margin, y, page.Width - 2 * margin, 24), XStringFormats.TopLeft);
        y += 22;

        gfx.DrawString($"Tema: {_doc.Theme}", fontSmall, XBrushes.Black,
            new XRect(margin, y, page.Width - 2 * margin, 18), XStringFormats.TopLeft);
        y += 14;

        gfx.DrawString($"Nível: {_doc.Difficulty}   •   Gerado: {_doc.GeneratedAt:dd/MM/yyyy HH:mm}", fontSmall, XBrushes.Black,
            new XRect(margin, y, page.Width - 2 * margin, 18), XStringFormats.TopLeft);
        y += 22;

        foreach (var q in _doc.Questions.OrderBy(x => x.Number))
        {
            if (y > page.Height - margin - 120) NewPage();

            if (!isAnswerKey)
            {
                DrawWrappedLine($"{q.Number}) {q.Prompt}", font, ref y, margin, page, gfx);

                DrawWrappedLine($"A) {q.A}", fontSmall, ref y, margin + 10, page, gfx);
                DrawWrappedLine($"B) {q.B}", fontSmall, ref y, margin + 10, page, gfx);
                DrawWrappedLine($"C) {q.C}", fontSmall, ref y, margin + 10, page, gfx);
                DrawWrappedLine($"D) {q.D}", fontSmall, ref y, margin + 10, page, gfx);

                y += 10;
            }
            else
            {
                // FIX: imprime sempre a letra já normalizada
                var ans = NormalizeAnswer(q.Answer);
                DrawWrappedLine($"{q.Number}) {ans}", font, ref y, margin, page, gfx);
            }
        }

        document.Save(path);
    }

    private static void DrawWrappedLine(string text, XFont font, ref double y, double x, PdfPage page, XGraphics gfx)
    {
        const int maxChars = 92;

        var t = (text ?? "").Trim();
        while (t.Length > 0)
        {
            var take = Math.Min(maxChars, t.Length);
            var line = t[..take];

            if (take < t.Length)
            {
                var lastSpace = line.LastIndexOf(' ');
                if (lastSpace > 40) line = line[..lastSpace];
            }

            gfx.DrawString(line, font, XBrushes.Black,
                new XRect(x, y, page.Width - (x + 40), 16), XStringFormats.TopLeft);

            y += 16;
            t = t[line.Length..].TrimStart();

            if (y > page.Height - 60) break;
        }
    }

    private void ReNumber()
    {
        int n = 1;
        foreach (var q in _doc.Questions)
        {
            q.Number = n++;
            q.Answer = NormalizeAnswer(q.Answer);
        }

        QuestionsList.ItemsSource = null;
        QuestionsList.ItemsSource = _doc.Questions;
    }

    private void UpdateEmpty()
    {
        EmptyLabel.IsVisible = _doc.Questions.Count == 0;
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;

        if (!string.IsNullOrWhiteSpace(status))
            StatusLabel.Text = status;
    }

    private string? BuildQuizJson()
    {
        if (_doc.Questions.Count == 0) return null;
        return JsonSerializer.Serialize(_doc, new JsonSerializerOptions { WriteIndented = false });
    }

    private async void OnClose(object sender, EventArgs e)
    {
        string? json = null;
        try { json = BuildQuizJson(); } catch { }

        _onDone?.Invoke(json);
        await Navigation.PopModalAsync();
    }

    private static string FirstSentence(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var idx = s.IndexOfAny(['.', '?', '!']);
        if (idx > 0) return s[..(idx + 1)].Trim();
        return s.Trim();
    }

    private static string Normalize(string s)
    {
        s = (s ?? "").ToLowerInvariant();
        s = Regex.Replace(s, @"[^\p{L}\p{N}]+", " ");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    private static string NormalizeAnswer(string? answer)
    {
        var a = (answer ?? "").Trim().ToUpperInvariant();
        return (a is "A" or "B" or "C" or "D") ? a : "A";
    }

    // HandlerChanged do Picker do gabarito no DataTemplate (se você estiver usando isso no XAML)
    public void OnAnswerPickerHandlerChanged(object sender, EventArgs e)
    {
        if (sender is Picker p)
        {
            if (p.ItemsSource == null)
                p.ItemsSource = AnswerOptions;
        }
    }
}
