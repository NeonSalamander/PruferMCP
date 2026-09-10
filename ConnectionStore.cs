using System.Text.Json;

namespace PruferMCP;

/// <summary>
/// Loads and persists saved connection profiles to the local app data folder.
/// </summary>
internal sealed class ConnectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public ConnectionStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PruferMCP");

        _filePath = Path.Combine(directory, "connections.json");
    }

    public async Task<List<SavedConnection>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new List<SavedConnection>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<SavedConnection>>(json, SerializerOptions) ?? new List<SavedConnection>();
        }
        catch
        {
            return new List<SavedConnection>();
        }
    }

    public async Task SaveAsync(List<SavedConnection> connections, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(connections, SerializerOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }
}
