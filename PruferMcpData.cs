using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.VisualStudio.Extensibility.UI;

namespace PruferMCP;

/// <summary>
/// View-model for the Prufer MCP panel.
/// </summary>
[DataContract]
internal class PruferMcpData : NotifyPropertyChangedObject, IDisposable
{
    private readonly McpClient _mcpClient;
    private readonly ConnectionStore _connectionStore;
    private bool _disposed;

    private string _transportType = "stdio";
    private string _executablePath = @"C:\Program Files\nodejs\node.exe";
    private string _arguments = @"C:\mcp\server.js";
    private string _serverUrl = "http://localhost:3000/sse";
    private bool _isConnected;
    private bool _isBusy;
    private ToolInfo? _selectedTool;
    private string _rawLog = string.Empty;
    private string _statusMessage = "Ready.";
    private string _responseText = string.Empty;

    public PruferMcpData(McpClient mcpClient, ConnectionStore connectionStore)
    {
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
        _connectionStore = connectionStore ?? throw new ArgumentNullException(nameof(connectionStore));
        _mcpClient.LogMessage += OnMcpLogMessage;

        TransportOptions = new ObservableCollection<string> { "stdio", "http" };
        SavedServerUrls = new ObservableCollection<string>();
        Headers = new ObservableCollection<HttpHeader>();

        ConnectCommand = new AsyncCommand(ConnectAsync);
        DisconnectCommand = new AsyncCommand(DisconnectAsync) { CanExecute = false };
        CallSelectedToolCommand = new AsyncCommand(CallSelectedToolAsync) { CanExecute = false };
        AddHeaderCommand = new AsyncCommand(AddHeaderAsync);

        Tools = new ObservableCollection<ToolInfo>();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _mcpClient.LogMessage -= OnMcpLogMessage;
    }

    public async Task LoadSavedServersAsync(CancellationToken cancellationToken)
    {
        var connections = await _connectionStore.LoadAsync(cancellationToken);

        var urls = connections
            .Where(c => string.Equals(c.TransportType, "http", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ServerUrl.Trim())
            .Where(url => !string.IsNullOrEmpty(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(url => url, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SavedServerUrls.Clear();
        foreach (var url in urls)
        {
            SavedServerUrls.Add(url);
        }
    }

    private void OnMcpLogMessage(object? sender, string e)
    {
        RawLog += e + Environment.NewLine;
    }

    [DataMember]
    public ObservableCollection<string> TransportOptions { get; }

    [DataMember]
    public ObservableCollection<string> SavedServerUrls { get; }

    [DataMember]
    public string TransportType
    {
        get => _transportType;
        set => SetProperty(ref _transportType, value);
    }

    [DataMember]
    public string ExecutablePath
    {
        get => _executablePath;
        set => SetProperty(ref _executablePath, value);
    }

    [DataMember]
    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    [DataMember]
    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    [DataMember]
    public ObservableCollection<HttpHeader> Headers { get; }

    [DataMember]
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                DisconnectCommand.CanExecute = value;
                UpdateCallToolCanExecute();
            }
        }
    }

    [DataMember]
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    [DataMember]
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    [DataMember]
    public ToolInfo? SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (SetProperty(ref _selectedTool, value))
            {
                UpdateCallToolCanExecute();
                RaiseNotifyPropertyChangedEvent(nameof(IsToolSelected));
            }
        }
    }

    [DataMember]
    public bool IsToolSelected => SelectedTool is not null;

    [DataMember]
    public string RawLog
    {
        get => _rawLog;
        set => SetProperty(ref _rawLog, value);
    }

    [DataMember]
    public string ResponseText
    {
        get => _responseText;
        private set => SetProperty(ref _responseText, value);
    }

    [DataMember]
    public ObservableCollection<ToolInfo> Tools { get; }

    [DataMember]
    public AsyncCommand ConnectCommand { get; }

    [DataMember]
    public AsyncCommand DisconnectCommand { get; }

    [DataMember]
    public AsyncCommand CallSelectedToolCommand { get; }

    [DataMember]
    public AsyncCommand AddHeaderCommand { get; }

    private async Task ConnectAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (IsBusy)
            return;

        // Capture all user-editable connection settings synchronously before any async work.
        // Remote UI updates bound properties asynchronously, so reading them after an await
        // could pick up stale values when the user quickly switches transport and clicks Connect.
        var transportType = TransportType.ToLowerInvariant();
        var executablePath = ExecutablePath;
        var arguments = Arguments;
        var serverUrl = ServerUrl;
        var headers = Headers
            .Where(h => !string.IsNullOrWhiteSpace(h.Name))
            .ToDictionary(h => h.Name.Trim(), h => h.Value ?? string.Empty, StringComparer.Ordinal);

        IsBusy = true;
        StatusMessage = "Connecting...";

        try
        {
            Tools.Clear();
            ResponseText = string.Empty;

            IMcpTransport transport = transportType switch
            {
                "http" => new HttpSseMcpTransport(serverUrl, headers),
                _ => new StdioMcpTransport(executablePath, arguments),
            };

            await _mcpClient.ConnectAsync(transport, cancellationToken);
            IsConnected = true;
            StatusMessage = "Connected. Requesting tool list...";

            var result = await _mcpClient.SendRequestAsync("tools/list", null, cancellationToken);
            RenderResponse(result);

            if (result.TryGetProperty("tools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in toolsElement.EnumerateArray())
                {
                    var toolInfo = CreateToolFromJson(tool);
                    if (toolInfo is not null)
                    {
                        Tools.Add(toolInfo);
                    }
                }
            }

            await SaveCurrentConnectionAsync(cancellationToken);
            StatusMessage = $"Loaded {Tools.Count} tools.";
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync(object? parameter, CancellationToken cancellationToken)
    {
        await Task.Yield();
        _mcpClient.Disconnect();
        IsConnected = false;
        Tools.Clear();
        ResponseText = string.Empty;
        StatusMessage = "Disconnected.";
    }

    private async Task CallSelectedToolAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (SelectedTool is null)
            return;

        IsBusy = true;
        StatusMessage = $"Calling {SelectedTool.Name}...";

        try
        {
            var arguments = BuildArguments(SelectedTool.Parameters);
            var args = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = SelectedTool.Name,
                ["arguments"] = arguments,
            };

            var result = await _mcpClient.SendRequestAsync("tools/call", args, cancellationToken);
            RenderResponse(result);
            StatusMessage = $"{SelectedTool.Name} completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Call error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task AddHeaderAsync(object? parameter, CancellationToken cancellationToken)
    {
        Headers.Add(new HttpHeader(h => Headers.Remove(h)));
        return Task.CompletedTask;
    }

    private async Task SaveCurrentConnectionAsync(CancellationToken cancellationToken)
    {
        var name = GenerateConnectionName();
        var connections = await _connectionStore.LoadAsync(cancellationToken);

        connections.RemoveAll(c => c.Name == name);
        connections.Insert(0, new SavedConnection
        {
            Name = name,
            TransportType = TransportType,
            ExecutablePath = ExecutablePath,
            Arguments = Arguments,
            ServerUrl = ServerUrl,
            Headers = Headers
                .Where(h => !string.IsNullOrWhiteSpace(h.Name))
                .Select(h => new SavedHeader { Name = h.Name.Trim(), Value = h.Value ?? string.Empty })
                .ToList(),
        });

        await _connectionStore.SaveAsync(connections, cancellationToken);
        await LoadSavedServersAsync(cancellationToken);
    }

    private string GenerateConnectionName()
    {
        if (TransportType.Equals("http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ServerUrl))
            return ServerUrl.Trim();

        if (!string.IsNullOrWhiteSpace(ExecutablePath))
            return $"{ExecutablePath} {Arguments}".Trim();

        return "Untitled";
    }

    private void UpdateCallToolCanExecute()
    {
        CallSelectedToolCommand.CanExecute = IsConnected && SelectedTool is not null;
    }

    private static ToolInfo? CreateToolFromJson(JsonElement element)
    {
        if (!element.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
            return null;

        var tool = new ToolInfo
        {
            Name = nameProp.GetString()!,
            Description = element.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String
                ? descProp.GetString()
                : null,
        };

        if (element.TryGetProperty("inputSchema", out var schema) &&
            schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            schema.TryGetProperty("required", out var requiredElement);
            var required = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

            if (requiredElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        required.Add(item.GetString()!);
                    }
                }
            }

            foreach (var property in properties.EnumerateObject())
            {
                var parameter = new ToolParameter
                {
                    Name = property.Name,
                    Type = property.Value.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                        ? typeProp.GetString() ?? "string"
                        : "string",
                    Description = property.Value.TryGetProperty("description", out var paramDesc) && paramDesc.ValueKind == JsonValueKind.String
                        ? paramDesc.GetString()
                        : null,
                    Required = required.Contains(property.Name),
                };

                if (property.Value.TryGetProperty("enum", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in enumElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            parameter.EnumValues.Add(item.GetString()!);
                        }
                    }

                    if (parameter.EnumValues.Count > 0)
                    {
                        parameter.InputKind = "ComboBox";
                        parameter.Value = parameter.EnumValues[0];
                    }
                }

                tool.Parameters.Add(parameter);
            }
        }

        return tool;
    }

    private static System.Text.Json.Nodes.JsonObject BuildArguments(IEnumerable<ToolParameter> parameters)
    {
        var arguments = new System.Text.Json.Nodes.JsonObject();

        foreach (var parameter in parameters)
        {
            var key = parameter.Name;
            var raw = parameter.Value;

            if (string.IsNullOrEmpty(raw))
            {
                arguments[key] = null;
                continue;
            }

            arguments[key] = parameter.Type.ToLowerInvariant() switch
            {
                "boolean" => bool.TryParse(raw, out var b) ? b : raw,
                "integer" => int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i)
                    ? i
                    : long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : raw,
                "number" => double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : raw,
                "array" or "object" => TryParseJsonNode(raw) ?? raw,
                _ => raw,
            };
        }

        return arguments;

        static System.Text.Json.Nodes.JsonNode? TryParseJsonNode(string json)
        {
            try
            {
                return System.Text.Json.Nodes.JsonNode.Parse(json);
            }
            catch
            {
                return null;
            }
        }
    }

    private void RenderResponse(JsonElement element)
    {
        var pretty = JsonSerializer.Serialize(element, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        var inputLines = pretty.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var outputLines = new string[inputLines.Length];

        for (int i = 0; i < inputLines.Length; i++)
        {
            outputLines[i] = $"{i + 1,4}  {inputLines[i]}";
        }

        ResponseText = string.Join(Environment.NewLine, outputLines);
    }
}
