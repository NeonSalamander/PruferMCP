using System.Text.Json;
using System.Text.Json.Nodes;

namespace PruferMCP;

/// <summary>
/// Abstraction for exchanging JSON-RPC messages with an MCP server.
/// </summary>
internal interface IMcpTransport : IDisposable
{
    /// <summary>
    /// Transport type identifier, e.g. "stdio" or "http".
    /// </summary>
    string TransportType { get; }

    /// <summary>
    /// Returns <c>true</c> when the transport is ready to send messages.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Raised for raw traffic and transport-level diagnostics.
    /// </summary>
    event EventHandler<string>? LogMessage;

    /// <summary>
    /// Opens the transport (stdio process or HTTP SSE stream).
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes the transport.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Sends a JSON-RPC request and returns the <c>result</c> payload.
    /// </summary>
    Task<JsonElement> SendRequestAsync(string method, JsonObject? parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a JSON-RPC notification.
    /// </summary>
    Task SendNotificationAsync(string method, JsonObject? parameters, CancellationToken cancellationToken);
}
