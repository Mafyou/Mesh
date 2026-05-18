using Mesh.Mobile.Core.Models;

namespace Mesh.Mobile.Converters;

public sealed class ChatLineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MessageTemplate { get; set; }
    public DataTemplate? DateTemplate    { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
        => item is MessageLine ? MessageTemplate : DateTemplate;
}
