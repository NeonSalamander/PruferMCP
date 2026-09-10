using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace PruferMCP;

/// <summary>
/// A single configurable parameter of an MCP tool.
/// </summary>
[DataContract]
internal class ToolParameter : NotifyPropertyChangedObject
{
    private string _value = string.Empty;

    [DataMember]
    public string Name { get; set; } = string.Empty;

    [DataMember]
    public string Type { get; set; } = "string";

    [DataMember]
    public string? Description { get; set; }

    [DataMember]
    public bool Required { get; set; }

    [DataMember]
    public ObservableCollection<string> EnumValues { get; }

    [DataMember]
    public string InputKind { get; set; } = "TextBox";

    public ToolParameter()
    {
        EnumValues = new ObservableCollection<string>();
    }

    [DataMember]
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
