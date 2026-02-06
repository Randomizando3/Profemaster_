using System.Text.Json;
using ProfeMaster.Config;

namespace ProfeMaster.Pages.Tools;

public partial class NotesPostItPage : ContentPage
{
    private const string FileName = "notes_postit.json";
    private readonly List<NoteItem> _notes = new();

    private const int FreeLimit = 5;
    private const int PremiumLimit = 20;

    public NotesPostItPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
    }

    private async Task LoadAsync()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
            {
                MainThread.BeginInvokeOnMainThread(Render);
                return;
            }

            var json = await File.ReadAllTextAsync(path);
            var list = JsonSerializer.Deserialize<List<NoteItem>>(json) ?? new List<NoteItem>();

            _notes.Clear();
            _notes.AddRange(list.OrderByDescending(x => x.CreatedAt));
        }
        catch
        {
            _notes.Clear();
        }

        MainThread.BeginInvokeOnMainThread(Render);
    }

    private async Task SaveAsync()
    {
        try
        {
            var path = GetPath();
            var json = JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch
        {
            // silencioso
        }
    }

    private void Render()
    {
        NotesList.ItemsSource = null;
        NotesList.ItemsSource = _notes.OrderByDescending(x => x.CreatedAt).ToList();

        UpdateLimitUi();
    }

    private static string GetPath()
        => Path.Combine(FileSystem.AppDataDirectory, FileName);

    private void OnClearNew(object sender, EventArgs e)
        => NewNoteEditor.Text = "";

    // =========================
    // LIMITES POR PLANO
    // =========================
    private int? GetMaxNotesForPlan()
    {
        // SuperPremium = ilimitado
        if (AppFlags.CurrentPlan == PlanTier.SuperPremium && AppFlags.HasPlanActive())
            return null;

        // Premium
        if (AppFlags.CurrentPlan == PlanTier.Premium && AppFlags.HasPlanActive())
            return PremiumLimit;

        // Free (default)
        return FreeLimit;
    }

    private void UpdateLimitUi()
    {
        try
        {
            var max = GetMaxNotesForPlan();
            CountBadge.Text = max == null
                ? $"Notas: {_notes.Count}/?"
                : $"Notas: {_notes.Count}/{max.Value}";

            // opcional: desabilita botão quando bateu o limite
            AddBtn.IsEnabled = max == null || _notes.Count < max.Value;
        }
        catch
        {
            // não quebra UI
        }
    }

    private async Task<bool> EnsureCanAddNoteAsync()
    {
        var max = GetMaxNotesForPlan();
        if (max == null) return true;

        if (_notes.Count < max.Value) return true;

        await DisplayAlert(
            "Limite atingido",
            $"Seu plano permite até {max.Value} notas. Faça upgrade para continuar.",
            "Ver planos"
        );

        await GoToUpgradeAsync();
        return false;
    }

    private static async Task GoToUpgradeAsync()
    {
        // tenta as rotas mais prováveis, sem quebrar se não existir
        try { await Shell.Current.GoToAsync("upgrade"); return; } catch { }
        try { await Shell.Current.GoToAsync("plans"); return; } catch { }
        try { await Shell.Current.GoToAsync("//plans"); } catch { }
    }

    // =========================
    // AÇÕES
    // =========================
    private async void OnAddNote(object sender, EventArgs e)
    {
        if (!await EnsureCanAddNoteAsync())
            return;

        var text = (NewNoteEditor.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlert("Info", "Escreva algo na nota.", "OK");
            return;
        }

        var note = new NoteItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = text,
            CreatedAt = DateTimeOffset.Now,
            BgHex = PickRandomPostItColor()
        };

        _notes.Insert(0, note);
        NewNoteEditor.Text = "";

        Render();
        await SaveAsync();
    }

    private async void OnDelete(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (b.CommandParameter is not NoteItem n) return;

        var ok = await DisplayAlert("Excluir", "Remover esta nota?", "Sim", "Não");
        if (!ok) return;

        _notes.RemoveAll(x => x.Id == n.Id);
        Render();
        await SaveAsync();
    }

    private static string PickRandomPostItColor()
    {
        // tons suaves tipo post-it (hex simples)
        var colors = new[]
        {
            "#FFF7CC", // amarelo
            "#DFF7E8", // verde
            "#E6F0FF", // azul
            "#FFE6F1", // rosa
            "#F1E7FF", // lilás
            "#FFE9D6", // laranja
        };

        var idx = Random.Shared.Next(0, colors.Length);
        return colors[idx];
    }

    public sealed class NoteItem
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string BgHex { get; set; } = "#FFF7CC";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

        // Computed para UI
        public string TitleLine
        {
            get
            {
                var t = (Text ?? "").Trim();
                if (t.Length <= 22) return t;
                return t.Substring(0, 22) + "…";
            }
        }

        public string CreatedLabel => $"Criado em {CreatedAt:dd/MM/yyyy HH:mm}";
    }
}
