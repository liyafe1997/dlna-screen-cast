using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopDlnaCast.App;

public sealed class DisplayPickerTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SelectionTemplate { get; set; }

    public DataTemplate? DropDownTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        container is ComboBoxItem
            ? DropDownTemplate
            : SelectionTemplate;
}
