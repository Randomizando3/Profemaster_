using System.Globalization;
using ProfeMaster.Models;

namespace ProfeMaster.Helpers;

public sealed class PastDotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ScheduleEvent ev)
        {
            // passado = cinza | futuro/hoje = roxo
            return ev.End < DateTime.Now ? Color.FromArgb("#94A3B8") : Color.FromArgb("#7A5AF8");
        }

        return Color.FromArgb("#94A3B8");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
