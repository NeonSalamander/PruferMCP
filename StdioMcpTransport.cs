using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PruferMCP;

/// <summary>
/// MCP transport over stdio: JSON-RPC messages are newline-delimited.
/// </summary>
internal sealed class StdioMcpTransport : IMcpTransport
{
    private readonly string _executablePath;
    private readonly string _arguments;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending
        = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();

    private Process? _process;
    private StreamWriter? _writer;
    private int _nextId;
    private bool _disposed;

    public string TransportType => "stdio";

    public bool IsConnected => _process is { HasExited: false };

    public event EventHandler<string>? LogMessage;

    public StdioMcpTransport(string executablePath, string arguments)
    {
        _executablePath = executablePath;
        _arguments = arguments;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Disconnect();
        _pending.Clear();
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_executablePath))
            throw new FileNotFoundException("Executable file not found.", _executablePath);

        Disconnect();

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = _arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? string.Empty,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) => Log($"Process exited with code {_process.ExitCode}.");

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start MCP server process.");

        _writer = new StreamWriter(_process.StandardInput.BaseStream, Encoding.UTF8) { AutoFlush = true };

        _ = Task.Run(() => ReadStandardOutputAsync(_process.StandardOutput, cancellationToken), cancellationToken);
        _ = Task.Run(() => ReadStandardErrorAsync(_process.StandardError, cancellationToken), cancellationToken);

        Log($"Connected to {_executablePath} {_arguments}");
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    _writer?.WriteLine();
                    _writer?.Flush();
                    _process.StandardInput.Close();
                }
                catch
                {
                    // Ignore errors while closing streams.
                }

                if (!_process.WaitForExit(2000))
                    _process.Kill();
            }
        }
        catch
        {
            // Ignore.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _writer = null;
        }

        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetException(new InvalidOperationException("Connection closed."));
        }

        _pending.Clear();
    }

    public Task<JsonElement> SendRequestAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
            throw new InvalidOperationException("MCP server is not connected.");

        var id = Interlocked.Increment(ref _nextId).ToString();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

        var request = CreateJsonRpcMessage(method, parameters, id);
        WriteMessage(request);

        return tcs.Task;
    }

    public Task SendNotificationAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
            throw new InvalidOperationException("MCP server is not connected.");

        var notification = CreateJsonRpcMessage(method, parameters, id: null);
        WriteMessage(notification);
        return Task.CompletedTask;
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

    private void WriteMessage(JsonObject message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Log($"--> {json}");

        lock (_writeLock)
        {
            _writer?.WriteLine(json);
        }
    }

    private async Task ReadStandardOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!_disposed && _process is not null && !_process.HasExited)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Log($"<-- {line}");
                ProcessIncomingMessage(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }
        catch (Exception ex)
        {
            Log($"stdout read error: {ex.Message}");
        }
    }

    private async Task ReadStandardErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!_disposed && _process is not null && !_process.HasExited)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;

                Log($"[stderr] {line}");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }
        catch (Exception ex)
        {
            Log($"stderr read error: {ex.Message}");
        }
    }

    private void ProcessIncomingMessage(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        if (root.TryGetProperty("id", out var idProperty) &&
            idProperty.ValueKind == JsonValueKind.String)
        {
            var id = idProperty.GetString()!;
            if (_pending.TryRemove(id, out var tcs))
            {
                if (root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var errorMessage)
                        ? errorMessage.GetString() ?? "Unknown error"
                        : "Unknown error";
                    tcs.TrySetException(new InvalidOperationException(message));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    tcs.TrySetResult(result.Clone());
                }
                else
                {
                    tcs.TrySetResult(JsonDocument.Parse("{}").RootElement.Clone());
                }
            }
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }
}
