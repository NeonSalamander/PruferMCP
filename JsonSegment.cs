using System.Runtime.Serialization;

namespace PruferMCP;

/// <summary>
/// A single colored segment of a JSON response.
/// </summary>
[DataContract]
internal class JsonSegment
{
    [DataMember]
    public string Text { get; set; } = string.Empty;

    [DataMember]
    public string Category { get; set; } = "text";
}
