namespace PruferMCP;

/// <summary>
/// A persisted connection profile.
/// </summary>
internal sealed class SavedConnection
{
    public string Name { get; set; } = string.Empty;

    public string TransportType { get; set; } = "stdio";

    public string ExecutablePath { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = string.Empty;

    public List<SavedHeader> Headers { get; set; } = new();
}

/// <summary>
/// A persisted HTTP header pair.
/// </summary>
internal sealed class SavedHeader
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
