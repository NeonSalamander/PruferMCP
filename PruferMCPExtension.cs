using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace PruferMCP;

/// <summary>
/// Extension entry point. Registers shared services, including the MCP client.
/// </summary>
[VisualStudioContribution]
internal class PruferMCPExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "PruferMCP.d3344fbb-1a3a-483c-8090-83021b3cf709",
            version: this.ExtensionAssemblyVersion,
            publisherName: "PruferMCP",
            displayName: "Prufer MCP",
            description: "Native WPF Model Context Protocol client for Visual Studio 2022."),
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);

        serviceCollection.AddSingleton<McpClient>();
        serviceCollection.AddSingleton<ConnectionStore>();
    }
}
