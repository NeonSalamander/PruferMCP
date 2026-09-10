using System.Text.Json;
using System.Text.Json.Nodes;

namespace PruferMCP;

/// <summary>
/// High-level MCP client. Performs the initialization handshake and routes
/// calls through the selected transport.
/// </summary>
internal sealed class McpClient : IDisposable
{
    private IMcpTransport? _transport;
    private bool _disposed;

    public event EventHandler<string>? LogMessage;

    public bool IsConnected => _transport?.IsConnected ?? false;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Disconnect();
    }

    /// <summary>
    /// Selects the transport and performs the MCP initialization handshake.
    /// </summary>
    public async Task ConnectAsync(IMcpTransport transport, CancellationToken cancellationToken)
    {
        Disconnect();

        _transport = transport;
        _transport.LogMessage += OnTransportLogMessage;

        await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

        // Required MCP initialization exchange.
        var initResult = await _transport.SendRequestAsync(
            "initialize",
            new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "PruferMCP",
                    ["version"] = "1.0.0",
                },
            },
            cancellationToken).ConfigureAwait(false);

        OnTransportLogMessage(this, "Initialization complete.");
        _ = initResult;

        await _transport.SendNotificationAsync(
            "initialized",
            new JsonObject(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the current connection and disposes the transport.
    /// </summary>
    public void Disconnect()
    {
        if (_transport is not null)
        {
            _transport.LogMessage -= OnTransportLogMessage;
            _transport.Disconnect();
            _transport.Dispose();
            _transport = null;
        }
    }

    /// <summary>
    /// Sends a JSON-RPC request over the active transport.
    /// </summary>
    public Task<JsonElement> SendRequestAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (_transport is null)
            throw new InvalidOperationException("MCP server is not connected.");

        return _transport.SendRequestAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a JSON-RPC notification over the active transport.
    /// </summary>
    public Task SendNotificationAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (_transport is null)
            throw new InvalidOperationException("MCP server is not connected.");

        return _transport.SendNotificationAsync(method, parameters, cancellationToken);
    }

    private void OnTransportLogMessage(object? sender, string message)
    {
        LogMessage?.Invoke(this, message);
    }
}
