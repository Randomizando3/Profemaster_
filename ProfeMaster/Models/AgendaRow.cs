using System.Globalization;

namespace ProfeMaster.Models;

public enum AgendaRowKind
{
    Header = 0,
    Item = 1
}

public sealed class AgendaRow
{
    public AgendaRowKind Kind { get; set; }

    // Header
    public DateTime Date { get; set; }
    public string HeaderText { get; set; } = "";

    // Item
    public ScheduleEvent? Event { get; set; }

    public static AgendaRow MakeHeader(DateTime date)
    {
        var pt = new CultureInfo("pt-BR");
        var text = date.ToString("dd 'de' MMMM 'de' yyyy", pt);
        return new AgendaRow { Kind = AgendaRowKind.Header, Date = date.Date, HeaderText = text };
    }

    public static AgendaRow MakeItem(ScheduleEvent ev)
        => new AgendaRow { Kind = AgendaRowKind.Item, Event = ev };
}
