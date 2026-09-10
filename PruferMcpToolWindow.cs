using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace PruferMCP;

/// <summary>
/// Prufer MCP tool window.
/// </summary>
[VisualStudioContribution]
internal class PruferMcpToolWindow : ToolWindow
{
    private readonly McpClient _mcpClient;
    private readonly ConnectionStore _connectionStore;
    private PruferMcpData? _dataContext;
    private PruferMcpToolWindowContent? _content;

    public PruferMcpToolWindow(VisualStudioExtensibility extensibility, McpClient mcpClient, ConnectionStore connectionStore)
        : base(extensibility)
    {
        _mcpClient = mcpClient;
        _connectionStore = connectionStore;
        this.Title = "Prufer MCP";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        // Dock next to Solution Explorer so the window opens as a tool window panel inside the IDE.
        Placement = ToolWindowPlacement.DockedTo(new Guid("3AE79031-E1BC-11D0-8F78-00A0C9110057")),
        DockDirection = Dock.Right,
        AllowAutoCreation = true,
    };

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _dataContext = new PruferMcpData(_mcpClient, _connectionStore);
        await _dataContext.LoadSavedConnectionsAsync(cancellationToken).ConfigureAwait(false);
    }

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        _content ??= new PruferMcpToolWindowContent(_dataContext!);
        return Task.FromResult<IRemoteUserControl>(_content);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content?.Dispose();
            _dataContext?.Dispose();
            _mcpClient.Dispose();
        }

        base.Dispose(disposing);
    }
}
