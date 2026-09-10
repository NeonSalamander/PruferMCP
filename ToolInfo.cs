using System.Collections.ObjectModel;
using System.Runtime.Serialization;

namespace PruferMCP;

/// <summary>
/// Describes an MCP tool exposed by the server.
/// </summary>
[DataContract]
internal class ToolInfo
{
    public ToolInfo()
    {
        Parameters = new ObservableCollection<ToolParameter>();
    }

    [DataMember]
    public string Name { get; set; } = string.Empty;

    [DataMember]
    public string? Description { get; set; }

    [DataMember]
    public ObservableCollection<ToolParameter> Parameters { get; }

    public override string ToString() => Name;
}
