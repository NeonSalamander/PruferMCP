# PruferMCP

A Visual Studio 2022 extension that turns your IDE into an [MCP (Model Context Protocol)](https://modelcontextprotocol.io) client. Connect to MCP servers, browse available tools, fill in their parameters, and run them directly from a tool window inside Visual Studio.

## Features

- **Two transports**
  - `stdio` — run a local executable (e.g., a Node.js or Python MCP server).
  - `HTTP/SSE` — connect to a remote MCP server over Server-Sent Events.
- **Persistent connections** — saved connection profiles are stored in `%LocalAppData%\PruferMCP\connections.json` and restored on restart.
- **Tool discovery** — list all tools exposed by the connected MCP server.
- **Parameter form** — select a tool and a parameter form is generated from its JSON Schema.
- **Results & logs** — JSON responses are pretty-printed with line numbers; raw protocol traffic is shown in the log panel.
- **HTTP headers** — configure custom headers for the HTTP transport.

## Requirements

- Visual Studio 2022 (17.x or later)
- .NET 8 on Windows

## Installation

1. Download the latest `PruferMCP.vsix` from the [Releases](../../releases) page.
2. Double-click the `.vsix` file to install it into Visual Studio.
3. Restart Visual Studio.
4. Open the tool window from **View → Other Windows → PruferMCP**.

## Usage

1. **Choose transport**
   - **stdio**: set the executable path and arguments.
   - **HTTP**: set the server URL and optional headers on the **Settings** tab.
2. Click **Connect**. The extension initializes the MCP session and fetches the tool list.
3. Select a tool from the **Available tools** list. Its parameters appear below the result panel.
4. Fill in the values. Required parameters are marked with `*`.
5. Click **Call tool**. The JSON response is displayed in the result panel.

Saved connections can be loaded, saved, or deleted from the **Saved** dropdown.

## Building from source

```powershell
# Restore and build the extension
dotnet build

# The extension package is produced at:
# bin/Debug/net8.0-windows8.0/PruferMCP.vsix
```

To debug, press **F5** in Visual Studio. This starts the Experimental Instance of Visual Studio with the extension loaded.

## Configuration file

Saved connection profiles are stored as JSON in:

```
%LocalAppData%\PruferMCP\connections.json
```

The file is created automatically and should not need manual editing.

## Contributing

Contributions are welcome. If you find a bug or want a new transport/feature, please open an issue or submit a pull request.

## License

[MIT](LICENSE)
