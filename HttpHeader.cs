using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace PruferMCP;

/// <summary>
/// A single HTTP header used by the HTTP/SSE transport.
/// </summary>
[DataContract]
internal class HttpHeader : NotifyPropertyChangedObject
{
    private string _name = string.Empty;
    private string _value = string.Empty;

    public HttpHeader()
    {
        RemoveCommand = new AsyncCommand((_, __) => Task.CompletedTask);
    }

    public HttpHeader(Action<HttpHeader> remove)
        : this()
    {
        RemoveCommand = new AsyncCommand((_, __) =>
        {
            remove(this);
            return Task.CompletedTask;
        });
    }

    [DataMember]
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [DataMember]
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    [DataMember]
    public AsyncCommand RemoveCommand { get; }
}
