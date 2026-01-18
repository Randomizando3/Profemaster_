// Models/EventItem.cs
namespace ProfeMaster.Models;

public sealed class EventItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    public string ThumbLocalPath { get; set; } = "";
    public string ThumbUrl { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ===== Intervalo do Evento =====
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;

    // ===== Dias do Evento (slots) =====
    public List<EventDaySlot> Slots { get; set; } = new();

    // Helper para migração/compat
    public void EnsureSlotsMigrated()
    {
        Slots ??= new();
        foreach (var s in Slots)
        {
            s.Items ??= new();
            if (s.Items.Count == 0)
            {
                s.Items.Add(new EventAttractionItem
                {
                    StartTime = new TimeSpan(8, 0, 0),
                    EndTime = new TimeSpan(9, 0, 0),
                    Title = ""
                });
            }
        }
    }
}

public sealed class EventDaySlot
{
    public DateTime Date { get; set; } = DateTime.Today;

    // Atrações do dia
    public List<EventAttractionItem> Items { get; set; } = new();
}

public sealed class EventAttractionItem
{
    public string Title { get; set; } = "";

    public TimeSpan StartTime { get; set; } = new TimeSpan(8, 0, 0);
    public TimeSpan EndTime { get; set; } = new TimeSpan(9, 0, 0);

    public string Notes { get; set; } = "";
}
