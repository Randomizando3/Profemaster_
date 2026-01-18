using ProfeMaster.Models;

namespace ProfeMaster.Helpers;

public sealed class AgendaTimeFormatter : IValueConverter
{
    public static AgendaTimeFormatter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not ScheduleEvent ev) return "";
        var s = ev.Start;
        var e = ev.End;

        // "08:00–09:00 • Terça"
        return $"{s:HH:mm}–{e:HH:mm} • {s:dddd}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
