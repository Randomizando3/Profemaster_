using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using ProfeMaster.Config;
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

    // ===== Modos de geração =====
    private enum GenMode
    {
        OnlineAI = 0,
        OfflineData = 1,
        Custom = 2
    }

    private GenMode _mode = GenMode.OnlineAI;

    // Lista fixa do gabarito (Picker no DataTemplate)
    private static readonly List<string> AnswerOptions = new() { "A", "B", "C", "D" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ===== Persistência do modo no JSON =====
    // "online" | "offline" | "custom"
    private string _modeTag = "online";

    private string ModeToTag(GenMode mode) => mode switch
    {
        GenMode.OnlineAI => "online",
        GenMode.OfflineData => "offline",
        GenMode.Custom => "custom",
        _ => "online"
    };

    private GenMode TagToMode(string? tag)
    {
        var t = (tag ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "offline" => GenMode.OfflineData,
            "custom" => GenMode.Custom,
            _ => GenMode.OnlineAI
        };
    }

    private void SetMode(GenMode mode)
    {
        _mode = mode;
        _modeTag = ModeToTag(mode);

        if (ModePicker != null)
            ModePicker.SelectedIndex = (int)_mode;

        SyncModeLabel();
        ApplyModeUI();
    }

    // ===== Banco Offline (JSON local) =====
    private string OfflineBankPath => Path.Combine(FileSystem.AppDataDirectory, "offline_bank.json");

    private sealed class OfflineBankRoot
    {
        public int Version { get; set; } = 1;
        public List<OfflineBankQuestion> Questions { get; set; } = new();
    }

    private sealed class OfflineBankQuestion
    {
        public string Prompt { get; set; } = "";
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public string C { get; set; } = "";
        public string D { get; set; } = "";
        public string Answer { get; set; } = "A";

        public string Theme { get; set; } = "";
        public string Difficulty { get; set; } = "Ensino Médio";
        public string Source { get; set; } = "online"; // online | custom | import
        public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    // =========================
    // LIMITES POR PLANO (PROVAS)
    // Free: até 5 perguntas
    // Premium: até 10 perguntas
    // SuperPremium: até 10 perguntas (virtualmente ilimitado em provas, mas perguntas = 10)
    // =========================
    private static int GetPlanMaxQuestions()
    {
        // AppFlags.ApplyPlan já derruba plano expirado para Free.
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

    public QuizGeneratorPage(GroqQuizService svc, Action<string?>? onDone = null)
    {
        InitializeComponent();
        _svc = svc;
        _onDone = onDone;

        // Dificuldade
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

        // Modo
        ModePicker.ItemsSource = new List<string>
        {
            "IA (Online)",
            "Dados Offline",
            "Criar Personalizado"
        };
        ModePicker.SelectedIndex = 0;
        SyncModeLabel();
        ApplyModeUI();

        QuestionsList.ItemsSource = _doc.Questions;

        // >>> AQUI: garante limite já na abertura
        var maxQ = GetPlanMaxQuestions();
        if (_count > maxQ) _count = maxQ;
        SetCount(_count, updateStatus: false);

        _ = UpdateNetworkLabelAsync();
        UpdateEmpty();

        // Mensagem discreta de limite no status (sem mexer no XAML por enquanto)
        if (StatusLabel != null)
            StatusLabel.Text = $"Plano {GetPlanLabel()}: até {maxQ} pergunta(s) por quiz.";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await UpdateNetworkLabelAsync();
        SyncDifficultyLabel();
        SyncModeLabel();

        // Re-valida limite quando volta (caso plano mudou)
        var maxQ = GetPlanMaxQuestions();
        if (_count > maxQ)
            SetCount(maxQ, updateStatus: true);
    }

    // =========================
    // UI helpers
    // =========================
    private async Task UpdateNetworkLabelAsync()
    {
        bool hasNet = await _svc.HasInternetAsync();
        NetLabel.Text = hasNet ? "Online disponível" : "Sem internet (offline disponível)";
    }

    private void SyncDifficultyLabel()
    {
        var v = (DifficultyPicker.SelectedItem as string);
        if (string.IsNullOrWhiteSpace(v))
            v = "Ensino Médio";

        if (DifficultyValueLabel != null)
            DifficultyValueLabel.Text = v;
    }

    private void SyncModeLabel()
    {
        var v = (ModePicker.SelectedItem as string);
        if (string.IsNullOrWhiteSpace(v))
            v = "IA (Online)";

        if (ModeValueLabel != null)
            ModeValueLabel.Text = v;
    }

    private void ApplyModeUI()
    {
        _mode = (GenMode)Math.Max(0, ModePicker.SelectedIndex);

        if (GenerateBtn != null)
        {
            GenerateBtn.Text = _mode switch
            {
                GenMode.OnlineAI => "Gerar",
                GenMode.OfflineData => "Gerar",
                GenMode.Custom => "Gerar",
                _ => "Gerar"
            };
        }

        if (OfflinePanel != null)
            OfflinePanel.IsVisible = _mode == GenMode.OfflineData;
    }

    private void SetCount(int value, bool updateStatus)
    {
        var maxPlan = GetPlanMaxQuestions();

        if (value < 1) value = 1;

        // trava no limite do plano (e no limite absoluto 10 do app)
        var hardMax = Math.Min(10, maxPlan);
        if (value > hardMax) value = hardMax;

        _count = value;

        if (CountLabel != null)
            CountLabel.Text = _count.ToString();

        if (updateStatus && StatusLabel != null)
            StatusLabel.Text = $"Quantidade: {_count} (máx: {hardMax} no {GetPlanLabel()})";
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

    // =========================
    // Picker taps
    // =========================
    private void OnModeTap(object sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { ModePicker.Focus(); } catch { }
        });
    }

    private async void OnModeChanged(object sender, EventArgs e)
    {
        SyncModeLabel();
        ApplyModeUI();

        _modeTag = ModeToTag(_mode);

        if (_mode == GenMode.OfflineData && OfflineStatusLabel != null)
        {
            var count = await GetOfflineBankCountAsync();
            OfflineStatusLabel.Text = count > 0
                ? $"Banco offline: {count} pergunta(s) disponível(is)."
                : "Banco offline vazio. Gere online ou importe um JSON para preencher.";
        }
    }

    private void OnDifficultyTap(object sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { DifficultyPicker.Focus(); } catch { }
        });
    }

    // =========================
    // Load existing quiz
    // =========================
    public void LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            // 1) lê "mode" do JSON e seta UI corretamente
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("mode", out var modeProp) &&
                    modeProp.ValueKind == JsonValueKind.String)
                {
                    var tag = modeProp.GetString();
                    SetMode(TagToMode(tag));
                }
                else
                {
                    SetMode(GenMode.OnlineAI);
                }
            }
            catch
            {
                SetMode(GenMode.OnlineAI);
            }

            // 2) carrega QuizDocument
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

            // >>> LIMITE POR PLANO ao abrir quiz existente
            var maxQ = GetPlanMaxQuestions();
            if (_doc.Questions.Count > maxQ)
            {
                _doc.Questions = _doc.Questions.Take(maxQ).ToList();
                StatusLabel.Text = $"Seu plano {GetPlanLabel()} permite até {maxQ} pergunta(s). O quiz foi ajustado.";
            }

            ReNumber();
            UpdateEmpty();

            ThemeEntry.Text = _doc.Theme;

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

            var cnt = _doc.Questions.Count;
            if (cnt < 1) cnt = 1;

            // aqui SetCount já clampa pelo plano
            SetCount(cnt, updateStatus: false);

            if (_doc.Questions.Count > 0 && string.IsNullOrWhiteSpace(StatusLabel.Text))
            {
                StatusLabel.Text = $"Quiz carregado: {_doc.Questions.Count} pergunta(s).";
            }
        }
        catch
        {
            StatusLabel.Text = "Não foi possível carregar o quiz existente (JSON inválido).";
        }
    }

    // =========================
    // Count
    // =========================
    private void OnCountMinus(object sender, EventArgs e)
    {
        if (_busy) return;
        SetCount(_count - 1, updateStatus: true);
    }

    private async void OnCountPlus(object sender, EventArgs e)
    {
        if (_busy) return;

        var max = GetPlanMaxQuestions();
        if (_count >= max)
        {
            await DisplayAlert("Limite do plano", $"Seu plano {GetPlanLabel()} permite até {max} pergunta(s) por quiz.", "OK");
            return;
        }

        SetCount(_count + 1, updateStatus: true);
    }

    // =========================
    // Generate (by mode)
    // =========================
    private async void OnGenerate(object sender, EventArgs e)
    {
        if (_busy) return;

        // >>> garante clamp antes de gerar
        var max = GetPlanMaxQuestions();
        if (_count > max)
        {
            SetCount(max, updateStatus: true);
            await DisplayAlert("Limite do plano", $"Seu plano {GetPlanLabel()} permite até {max} pergunta(s) por quiz.", "OK");
            return;
        }

        var theme = (ThemeEntry.Text ?? "").Trim();
        var baseText = (BaseEditor.Text ?? "").Trim();
        var difficulty = (DifficultyPicker.SelectedItem as string) ?? "Ensino Médio";

        if (_mode != GenMode.Custom && string.IsNullOrWhiteSpace(theme))
        {
            await DisplayAlert("Atenção", "Informe o tema/assunto.", "OK");
            return;
        }

        SetBusy(true, "Processando...");

        try
        {
            _doc.Theme = theme;
            _doc.Difficulty = difficulty;
            _doc.GeneratedAt = DateTimeOffset.UtcNow;

            _doc.Questions.Clear();
            QuestionsList.ItemsSource = null;
            QuestionsList.ItemsSource = _doc.Questions;

            if (_mode == GenMode.Custom)
            {
                for (int i = 1; i <= _count; i++)
                {
                    _doc.Questions.Add(new QuizQuestion
                    {
                        Number = i,
                        Prompt = "",
                        A = "",
                        B = "",
                        C = "",
                        D = "",
                        Answer = "A"
                    });
                }

                ReNumber();
                UpdateEmpty();
                StatusLabel.Text = $"Template criado: {_doc.Questions.Count} pergunta(s).";
                return;
            }

            if (_mode == GenMode.OfflineData)
            {
                var result = await GenerateOfflineFromBankAsync(_count, theme, difficulty);

                int n = 1;
                foreach (var q in result)
                {
                    q.Number = n++;
                    q.Prompt = q.Prompt ?? "";
                    q.A = q.A ?? "";
                    q.B = q.B ?? "";
                    q.C = q.C ?? "";
                    q.D = q.D ?? "";
                    q.Answer = NormalizeAnswer(q.Answer);
                    _doc.Questions.Add(q);
                }

                ReNumber();
                UpdateEmpty();

                StatusLabel.Text = _doc.Questions.Count > 0
                    ? $"Offline: {_doc.Questions.Count} pergunta(s) montada(s) do banco local."
                    : "Offline: banco local vazio (gere online ou importe um JSON).";

                return;
            }

            // Online AI (Groq)
            var avoid = new List<string>();

            for (int i = 1; i <= _count; i++)
            {
                StatusLabel.Text = $"Gerando {i}/{_count} (IA) ...";

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

                if (q == null) continue;

                q.Number = i;
                _doc.Questions.Add(q);
            }

            ReNumber();
            UpdateEmpty();

            if (_doc.Questions.Count > 0)
            {
                await AddQuestionsToOfflineBankAsync(
                    questions: _doc.Questions,
                    theme: theme,
                    difficulty: difficulty,
                    source: "online"
                );
            }

            StatusLabel.Text = _doc.Questions.Count > 0
                ? $"Pronto: {_doc.Questions.Count} pergunta(s) gerada(s). (Banco offline atualizado)"
                : "Não foi possível gerar perguntas (verifique internet/chave).";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", "Falha:\n" + ex.Message, "OK");
            StatusLabel.Text = "Erro ao processar.";
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    // =========================
    // OFFLINE: gerar a partir do banco local
    // =========================
    private async Task<List<QuizQuestion>> GenerateOfflineFromBankAsync(int count, string theme, string difficulty)
    {
        var root = await LoadOfflineBankAsync();
        var all = root.Questions ?? new List<OfflineBankQuestion>();

        if (all.Count == 0)
            return new List<QuizQuestion>();

        var themed = FilterByTheme(all, theme);
        var src = themed.Count > 0 ? themed : all;

        var exactDiff = src.Where(x => string.Equals((x.Difficulty ?? "").Trim(), difficulty.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (exactDiff.Count > 0)
            src = exactDiff;

        var rnd = new Random();
        src = src.OrderBy(_ => rnd.Next()).ToList();

        var result = new List<QuizQuestion>();
        foreach (var q in src)
        {
            if (result.Count >= count) break;

            result.Add(new QuizQuestion
            {
                Prompt = q.Prompt ?? "",
                A = q.A ?? "",
                B = q.B ?? "",
                C = q.C ?? "",
                D = q.D ?? "",
                Answer = NormalizeAnswer(q.Answer)
            });
        }

        return result;
    }

    private static List<OfflineBankQuestion> FilterByTheme(List<OfflineBankQuestion> all, string theme)
    {
        var t = Normalize(theme);
        if (string.IsNullOrWhiteSpace(t)) return new List<OfflineBankQuestion>();

        var tokens = t.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Where(x => x.Length >= 3)
                      .Distinct()
                      .ToList();

        if (tokens.Count == 0) return new List<OfflineBankQuestion>();

        var scored = new List<(OfflineBankQuestion q, int score)>();

        foreach (var q in all)
        {
            var hay = Normalize($"{q.Prompt} {q.A} {q.B} {q.C} {q.D}");
            int score = 0;
            foreach (var tok in tokens)
                if (hay.Contains(tok)) score++;

            if (score > 0)
                scored.Add((q, score));
        }

        return scored
            .OrderByDescending(x => x.score)
            .Select(x => x.q)
            .ToList();
    }

    // =========================
    // Offline bank: load/save/add
    // =========================
    private async Task<int> GetOfflineBankCountAsync()
    {
        try
        {
            var root = await LoadOfflineBankAsync();
            return root.Questions?.Count ?? 0;
        }
        catch { return 0; }
    }

    private async Task<OfflineBankRoot> LoadOfflineBankAsync()
    {
        try
        {
            if (!File.Exists(OfflineBankPath))
                return new OfflineBankRoot();

            var json = await File.ReadAllTextAsync(OfflineBankPath);
            if (string.IsNullOrWhiteSpace(json))
                return new OfflineBankRoot();

            var root = JsonSerializer.Deserialize<OfflineBankRoot>(json, JsonOpts);
            return root ?? new OfflineBankRoot();
        }
        catch
        {
            return new OfflineBankRoot();
        }
    }

    private async Task SaveOfflineBankAsync(OfflineBankRoot root)
    {
        try
        {
            var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(OfflineBankPath, json);
        }
        catch
        {
            // silencioso
        }
    }

    private async Task AddQuestionsToOfflineBankAsync(IEnumerable<QuizQuestion> questions, string theme, string difficulty, string source)
    {
        try
        {
            var root = await LoadOfflineBankAsync();
            root.Questions ??= new List<OfflineBankQuestion>();

            var existing = new HashSet<string>(
                root.Questions.Select(x => Normalize(x.Prompt ?? "")),
                StringComparer.OrdinalIgnoreCase
            );

            int added = 0;

            foreach (var q in questions)
            {
                if (!IsValidForBank(q))
                    continue;

                var key = Normalize(q.Prompt ?? "");
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (existing.Contains(key)) continue;

                root.Questions.Add(new OfflineBankQuestion
                {
                    Prompt = (q.Prompt ?? "").Trim(),
                    A = (q.A ?? "").Trim(),
                    B = (q.B ?? "").Trim(),
                    C = (q.C ?? "").Trim(),
                    D = (q.D ?? "").Trim(),
                    Answer = NormalizeAnswer(q.Answer),

                    Theme = (theme ?? "").Trim(),
                    Difficulty = (difficulty ?? "Ensino Médio").Trim(),
                    Source = string.IsNullOrWhiteSpace(source) ? "online" : source.Trim(),
                    AddedAt = DateTimeOffset.UtcNow
                });

                existing.Add(key);
                added++;
            }

            if (added > 0)
                await SaveOfflineBankAsync(root);
        }
        catch
        {
            // silencioso
        }
    }

    private static bool IsValidForBank(QuizQuestion q)
    {
        if (q == null) return false;

        var p = (q.Prompt ?? "").Trim();
        var a = (q.A ?? "").Trim();
        var b = (q.B ?? "").Trim();
        var c = (q.C ?? "").Trim();
        var d = (q.D ?? "").Trim();

        if (p.Length < 10) return false;
        if (a.Length < 1 || b.Length < 1 || c.Length < 1 || d.Length < 1) return false;

        var ans = NormalizeAnswer(q.Answer);
        return ans is "A" or "B" or "C" or "D";
    }

    private async void OnImportOfflineBank(object sender, EventArgs e)
    {
        if (_busy) return;

        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "application/json", "text/plain" } },
                { DevicePlatform.iOS, new[] { "public.json", "public.text" } },
                { DevicePlatform.WinUI, new[] { ".json", ".txt" } },
                { DevicePlatform.MacCatalyst, new[] { "public.json", "public.text" } }
            });

            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Importar banco offline (JSON)",
                FileTypes = customFileType
            });

            if (result == null)
                return;

            await using var stream = await result.OpenReadAsync();
            using var sr = new StreamReader(stream);
            var json = await sr.ReadToEndAsync();

            var importedCount = await ImportOfflineBankJsonAsync(json);

            await DisplayAlert(
                "Importação concluída",
                importedCount > 0
                    ? $"Importado/mesclado: {importedCount} pergunta(s)."
                    : "Nenhuma pergunta válida foi encontrada no arquivo.",
                "OK"
            );

            if (OfflineStatusLabel != null)
            {
                var count = await GetOfflineBankCountAsync();
                OfflineStatusLabel.Text = count > 0
                    ? $"Banco offline: {count} pergunta(s) disponível(is)."
                    : "Banco offline vazio. Gere online ou importe um JSON para preencher.";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", "Falha ao importar:\n" + ex.Message, "OK");
        }
    }

    private async Task<int> ImportOfflineBankJsonAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;

        List<OfflineBankQuestion> items = new();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("questions", out var qarr) && qarr.ValueKind == JsonValueKind.Array)
            {
                items = ParseOfflineItemsArray(qarr);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                items = ParseOfflineItemsArray(root);
            }
        }
        catch
        {
            return 0;
        }

        if (items.Count == 0) return 0;

        var bank = await LoadOfflineBankAsync();
        bank.Questions ??= new List<OfflineBankQuestion>();

        var existing = new HashSet<string>(
            bank.Questions.Select(x => Normalize(x.Prompt ?? "")),
            StringComparer.OrdinalIgnoreCase
        );

        int added = 0;

        foreach (var it in items)
        {
            var key = Normalize(it.Prompt ?? "");
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (existing.Contains(key)) continue;

            if (it.Prompt.Trim().Length < 10) continue;
            if (string.IsNullOrWhiteSpace(it.A) || string.IsNullOrWhiteSpace(it.B) ||
                string.IsNullOrWhiteSpace(it.C) || string.IsNullOrWhiteSpace(it.D))
                continue;

            it.Answer = NormalizeAnswer(it.Answer);
            if (string.IsNullOrWhiteSpace(it.Source)) it.Source = "import";
            it.AddedAt = DateTimeOffset.UtcNow;

            bank.Questions.Add(it);
            existing.Add(key);
            added++;
        }

        if (added > 0)
            await SaveOfflineBankAsync(bank);

        return added;
    }

    private static List<OfflineBankQuestion> ParseOfflineItemsArray(JsonElement arr)
    {
        var list = new List<OfflineBankQuestion>();

        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;

            string s(string name)
            {
                if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString() ?? "";
                return "";
            }

            var q = new OfflineBankQuestion
            {
                Prompt = s("prompt"),
                A = s("a"),
                B = s("b"),
                C = s("c"),
                D = s("d"),
                Answer = s("answer"),
                Theme = s("theme"),
                Difficulty = s("difficulty"),
                Source = s("source")
            };

            if (string.IsNullOrWhiteSpace(q.Prompt)) q.Prompt = s("Prompt");
            if (string.IsNullOrWhiteSpace(q.A)) q.A = s("A");
            if (string.IsNullOrWhiteSpace(q.B)) q.B = s("B");
            if (string.IsNullOrWhiteSpace(q.C)) q.C = s("C");
            if (string.IsNullOrWhiteSpace(q.D)) q.D = s("D");
            if (string.IsNullOrWhiteSpace(q.Answer)) q.Answer = s("Answer");
            if (string.IsNullOrWhiteSpace(q.Theme)) q.Theme = s("Theme");
            if (string.IsNullOrWhiteSpace(q.Difficulty)) q.Difficulty = s("Difficulty");

            q.Answer = NormalizeAnswer(q.Answer);
            list.Add(q);
        }

        return list;
    }

    // =========================
    // Clear, JSON, Remove
    // =========================
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

    // =========================
    // PDF export
    // =========================
    private async void OnExportQuestionsPdf(object sender, EventArgs e)
        => await ExportPdfAsync(isAnswerKey: false);

    private async void OnExportAnswersPdf(object sender, EventArgs e)
        => await ExportPdfAsync(isAnswerKey: true);

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

    // =========================
    // Renumber + answer normalize
    // =========================
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

    // =========================
    // JSON build + close
    // =========================
    private string? BuildQuizJson()
    {
        if (_doc.Questions.Count == 0) return null;

        // >>> trava por segurança no build do JSON também
        var max = GetPlanMaxQuestions();
        if (_doc.Questions.Count > max)
        {
            _doc.Questions = _doc.Questions.Take(max).ToList();
            ReNumber();
        }

        EnsureValidAnswers(_doc);

        _modeTag = ModeToTag(_mode);

        var baseJson = JsonSerializer.Serialize(_doc, new JsonSerializerOptions { WriteIndented = false });
        using var src = JsonDocument.Parse(baseJson);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();

            foreach (var prop in src.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "mode", StringComparison.OrdinalIgnoreCase))
                    continue;

                writer.WritePropertyName(prop.Name);
                prop.Value.WriteTo(writer);
            }

            writer.WriteString("mode", _modeTag);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async void OnClose(object sender, EventArgs e)
    {
        try
        {
            var theme = (_doc.Theme ?? "").Trim();
            var difficulty = (_doc.Difficulty ?? "Ensino Médio").Trim();

            if (_doc.Questions.Count > 0)
            {
                var source = _mode == GenMode.Custom ? "custom"
                           : _mode == GenMode.OfflineData ? "offline"
                           : "online";

                await AddQuestionsToOfflineBankAsync(_doc.Questions, theme, difficulty, source);
            }
        }
        catch
        {
            // silencioso
        }

        string? json = null;
        try { json = BuildQuizJson(); } catch { }

        _onDone?.Invoke(json);
        await Navigation.PopModalAsync();
    }

    // =========================
    // Text helpers
    // =========================
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

    // =========================
    // Answer picker handler
    // =========================
    public void OnAnswerPickerHandlerChanged(object sender, EventArgs e)
    {
        if (sender is Picker p)
        {
            if (p.ItemsSource == null)
                p.ItemsSource = AnswerOptions;
        }
    }

    private async void OnDownloadOfflineBankExample(object sender, EventArgs e)
    {
        try
        {
            var example = new List<QuizQuestion>
            {
                new QuizQuestion
                {
                    Prompt = "Qual é a capital do Brasil?",
                    A = "Rio de Janeiro",
                    B = "Brasília",
                    C = "São Paulo",
                    D = "Belo Horizonte",
                    Answer = "B"
                },
                new QuizQuestion
                {
                    Prompt = "Qual planeta é conhecido como o Planeta Vermelho?",
                    A = "Terra",
                    B = "Júpiter",
                    C = "Marte",
                    D = "Vênus",
                    Answer = "C"
                },
                new QuizQuestion
                {
                    Prompt = "Quem escreveu Dom Casmurro?",
                    A = "José de Alencar",
                    B = "Machado de Assis",
                    C = "Clarice Lispector",
                    D = "Graciliano Ramos",
                    Answer = "B"
                }
            };

            var json = JsonSerializer.Serialize(example, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var path = Path.Combine(
                FileSystem.AppDataDirectory,
                "offline_bank_example.json"
            );

            await File.WriteAllTextAsync(path, json, Encoding.UTF8);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Banco Offline – Exemplo",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                "Não foi possível gerar o arquivo de exemplo:\n" + ex.Message,
                "OK"
            );
        }
    }
}
