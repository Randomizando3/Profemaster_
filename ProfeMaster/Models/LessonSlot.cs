namespace ProfeMaster.Models;

public sealed class LessonSlot
{
    // Dia do slot (somente Date)
    public DateTime Date { get; set; } = DateTime.Today;

    // ===== NOVO: múltiplas aulas no mesmo dia =====
    public List<LessonSlotItem> Items { get; set; } = new();

    // ===== LEGACY (mantido para não quebrar JSON antigo) =====
    public string LessonId { get; set; } = "";
    public string LessonTitle { get; set; } = "";

    public bool HasLesson
    {
        get
        {
            if (Items != null && Items.Any(i => i != null && !string.IsNullOrWhiteSpace(i.LessonId)))
                return true;

            return !string.IsNullOrWhiteSpace(LessonId);
        }
    }

    // Normaliza/migra dados antigos para o formato novo
    public void EnsureMigrated()
    {
        Items ??= new();

        // se veio do formato antigo e ainda não tem itens, converte para 1 item
        if (Items.Count == 0 && !string.IsNullOrWhiteSpace(LessonId))
        {
            Items.Add(new LessonSlotItem
            {
                LessonId = LessonId,
                LessonTitle = LessonTitle,
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(9, 0, 0)
            });
        }

        // limpa legacy para não ficar duplicado (opcional, mas ajuda a manter consistente)
        LessonId = "";
        LessonTitle = "";
    }
}

public sealed class LessonSlotItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string LessonId { get; set; } = "";
    public string LessonTitle { get; set; } = "";

    // Horários dentro do dia
    public TimeSpan StartTime { get; set; } = new TimeSpan(8, 0, 0);
    public TimeSpan EndTime { get; set; } = new TimeSpan(9, 0, 0);

    public bool HasLesson => !string.IsNullOrWhiteSpace(LessonId);
}
