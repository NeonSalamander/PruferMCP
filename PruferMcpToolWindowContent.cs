using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.Threading;

namespace PruferMCP;

/// <summary>
/// Remote UI content for the Prufer MCP tool window.
/// </summary>
internal class PruferMcpToolWindowContent : RemoteUserControl
{
    public PruferMcpToolWindowContent(PruferMcpData dataContext)
        : base(dataContext: dataContext, synchronizationContext: new NonConcurrentSynchronizationContext(sticky: true))
    {
    }
}
