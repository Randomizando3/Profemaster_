using ProfeMaster.Models;

namespace ProfeMaster.Helpers;

public sealed class AgendaRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? ItemTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is AgendaRow r && r.Kind == AgendaRowKind.Header)
            return HeaderTemplate!;

        return ItemTemplate!;
    }
}
