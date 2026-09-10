using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace PruferMCP;

/// <summary>
/// Command that opens the Prufer MCP tool window.
/// </summary>
[VisualStudioContribution]
internal class ShowPruferMcpCommand : Command
{
    public ShowPruferMcpCommand(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    public override CommandConfiguration CommandConfiguration => new("%PruferMCP.ShowPruferMcpCommand.DisplayName%")
    {
        Placements = new[] { CommandPlacement.KnownPlacements.ViewOtherWindowsMenu },
        Icon = new(ImageMoniker.KnownValues.ToolWindow, IconSettings.IconAndText),
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await this.Extensibility.Shell().ShowToolWindowAsync<PruferMcpToolWindow>(activate: true, cancellationToken);
    }
}
