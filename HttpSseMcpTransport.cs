using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PruferMCP;

/// <summary>
/// MCP transport over HTTP with Server-Sent Events support.
/// Handles both classic HTTP+SSE transports (GET /sse, POST endpoint)
/// and direct JSON responses to POST requests.
/// </summary>
internal sealed class HttpSseMcpTransport : IMcpTransport
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyDictionary<string, string> _defaultHeaders;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending
        = new(StringComparer.Ordinal);

    private Uri? _postUrl;
    private int _nextId;
    private bool _disposed;
    private Task? _sseReaderTask;

    public string TransportType => "http";

    public bool IsConnected { get; private set; }

    public event EventHandler<string>? LogMessage;

    public HttpSseMcpTransport(string baseUrl, IReadOnlyDictionary<string, string>? defaultHeaders = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Server URL cannot be empty.", nameof(baseUrl));

        _baseUrl = baseUrl;
        _defaultHeaders = defaultHeaders ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Disconnect();
        _httpClient.Dispose();
        _pending.Clear();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Disconnect();
        _postUrl = new Uri(_baseUrl, UriKind.Absolute);
        IsConnected = true;

        Log($"Connecting to HTTP/SSE {_baseUrl}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _postUrl);
            ApplyDefaultHeaders(request);
            if (!request.Headers.Contains("Accept"))
            {
                request.Headers.Add("Accept", "text/event-stream");
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                Log("SSE stream opened.");
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                _sseReaderTask = Task.Run(() => ReadSseStreamAsync(stream, cancellationToken), cancellationToken);
            }
            else
            {
                Log($"GET SSE not supported ({response.StatusCode}); relying on POST responses.");
                response.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to open SSE stream: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        IsConnected = false;

        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetException(new InvalidOperationException("Connection closed."));
        }

        _pending.Clear();
    }

    public async Task<JsonElement> SendRequestAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new InvalidOperationException("HTTP transport is not connected.");

        var id = Interlocked.Increment(ref _nextId).ToString();
        var request = CreateJsonRpcMessage(method, parameters, id);
        var (json, response) = await PostJsonAsync(request, cancellationToken).ConfigureAwait(false);

        using (response)
        {
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                Log($"<-- {json}");
                return ParseResponse(json, id);
            }

            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await ReadSseResponseAsync(stream, id, cancellationToken).ConfigureAwait(false);
            }

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[id] = tcs;

                using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);
                return await tcs.Task.ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Unexpected HTTP response: {response.StatusCode}");
        }
    }

    public async Task SendNotificationAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new InvalidOperationException("HTTP transport is not connected.");

        var notification = CreateJsonRpcMessage(method, parameters, id: null);
        var (_, response) = await PostJsonAsync(notification, cancellationToken).ConfigureAwait(false);
        response.Dispose();
    }

    private static JsonObject CreateJsonRpcMessage(string method, JsonObject? parameters, string? id)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };

        if (id is not null)
        {
            message["id"] = id;
        }

        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return message;
    }

    private void ApplyDefaultHeaders(HttpRequestMessage request)
    {
        foreach (var header in _defaultHeaders)
        {
            if (!request.Headers.Contains(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    private async Task<(string Json, HttpResponseMessage Response)> PostJsonAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Log($"--> {json}");

        if (_postUrl is null)
            throw new InvalidOperationException("POST URL is not set.");

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, _postUrl)
        {
            Content = content,
        };

        ApplyDefaultHeaders(request);
        if (!request.Headers.Contains("Accept"))
        {
            request.Headers.Add("Accept", "application/json, text/event-stream");
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var responseJson = response.Content.Headers.ContentType?.MediaType == "application/json"
            ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            : string.Empty;

        return (responseJson, response);
    }

    private async Task ReadSseStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sseEvent in ReadSseEventsAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                ProcessSseEvent(sseEvent);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
        catch (Exception ex)
        {
            Log($"SSE stream read error: {ex.Message}");
        }
    }

    private async Task<JsonElement> ReadSseResponseAsync(Stream stream, string id, CancellationToken cancellationToken)
    {
        await foreach (var sseEvent in ReadSseEventsAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            if (sseEvent.EventName == "endpoint" && !string.IsNullOrEmpty(sseEvent.Data))
            {
                UpdatePostUrl(sseEvent.Data);
                continue;
            }

            if (TryParseJsonRpcMessage(sseEvent.Data, out var message) &&
                message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("id", out var idProp) &&
                idProp.ValueKind == JsonValueKind.String &&
                idProp.GetString() == id)
            {
                return ExtractResult(message);
            }
        }

        throw new InvalidOperationException("No response received for the request in the SSE stream.");
    }

    private async IAsyncEnumerable<SseEvent> ReadSseEventsAsync(Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var currentEvent = new SseEvent();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;

            if (string.IsNullOrEmpty(line))
            {
                if (!string.IsNullOrEmpty(currentEvent.EventName) || !string.IsNullOrEmpty(currentEvent.Data))
                {
                    yield return currentEvent;
                    currentEvent = new SseEvent();
                }

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent.EventName = line.Substring(6).Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var data = line.Substring(5).Trim();
                currentEvent.Data = string.IsNullOrEmpty(currentEvent.Data)
                    ? data
                    : currentEvent.Data + "\n" + data;
            }
            else if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                currentEvent.Id = line.Substring(3).Trim();
            }
        }
    }

    private void ProcessSseEvent(SseEvent sseEvent)
    {
        if (sseEvent.EventName == "endpoint" && !string.IsNullOrEmpty(sseEvent.Data))
        {
            UpdatePostUrl(sseEvent.Data);
            Log($"Updated POST endpoint: {_postUrl}");
            return;
        }

        Log($"<-- {sseEvent.Data}");

        if (TryParseJsonRpcMessage(sseEvent.Data, out var message) &&
            message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("id", out var idProp) &&
            idProp.ValueKind == JsonValueKind.String)
        {
            var id = idProp.GetString()!;
            if (_pending.TryRemove(id, out var tcs))
            {
                tcs.TrySetResult(ExtractResult(message));
            }
        }
    }

    private void UpdatePostUrl(string endpointData)
    {
        if (Uri.TryCreate(endpointData, UriKind.Absolute, out var absolute))
        {
            _postUrl = absolute;
        }
        else if (Uri.TryCreate(endpointData, UriKind.Relative, out var relative) && _postUrl is not null)
        {
            _postUrl = new Uri(_postUrl, relative);
        }
    }

    private JsonElement ParseResponse(string json, string expectedId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("id", out var idProp) &&
            idProp.ValueKind == JsonValueKind.String &&
            idProp.GetString() == expectedId)
        {
            return ExtractResult(root);
        }

        throw new InvalidOperationException("Received response with unexpected id.");
    }

    private static JsonElement ExtractResult(JsonElement message)
    {
        if (message.TryGetProperty("error", out var error))
        {
            var text = error.TryGetProperty("message", out var errorMessage)
                ? errorMessage.GetString() ?? "Unknown error"
                : "Unknown error";
            throw new InvalidOperationException(text);
        }

        if (message.TryGetProperty("result", out var result))
        {
            return result.Clone();
        }

        return JsonDocument.Parse("{}").RootElement.Clone();
    }

    private static bool TryParseJsonRpcMessage(string data, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(data))
            return false;

        try
        {
            using var document = JsonDocument.Parse(data);
            element = document.RootElement.Clone();
            return element.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private struct SseEvent
    {
        public string EventName { get; set; }
        public string Data { get; set; }
        public string Id { get; set; }
    }
}
