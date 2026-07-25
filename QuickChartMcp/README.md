# QuickChartMcp

Local **stdio MCP server** that renders charts via a configured self-hosted
[QuickChart](https://quickchart.io/) instance and writes the resulting file
(PNG/SVG/WebP/JPG/PDF) to a directory the calling agent specifies, returning only a compact
summary (file path, size, metadata) instead of an inline blob.

## Tool

- `create_chart` — mirrors QuickChart's POST `/chart` endpoint: `chart` (Chart.js config as
  JSON or JavaScript-syntax string), `width`, `height`, `devicePixelRatio`,
  `backgroundColor`, `format` (`png`/`svg`/`webp`/`jpg`/`pdf`), `version`, plus a required
  `outputDirectory` and an optional `fileName`.

## Configuration

Set the target instance via the `QuickChart` config section (env vars or `appsettings.json`):

- `QuickChart__BaseUrl` — default `http://localhost:3400`
- `QuickChart__ApiKey` — sent as the `key` body property; not needed for self-hosted instances
- `QuickChart__TimeoutSeconds` — default `60`
- `QuickChart__AllowedOutputPatterns__0`, `__1`, … — regex allow-list for output directories

### Output path safety

Every write is validated before touching disk: `outputDirectory` must be absolute/rooted with
no `.`/`..`/all-dots/empty/invalid segments (paths are validated as-is, never normalized), any
`fileName` must be a bare leaf name, and the directory must match at least one regex in
`AllowedOutputPatterns`. **An empty allow-list denies all writes** — configure at least one
pattern. Existing files are never overwritten (a numeric suffix is appended on collision).
Invalid regexes fail fast at startup. On rejection the tool returns
`{ "success": false, "error": "..." }` and writes nothing.

## Local development

```bash
dotnet run --project QuickChartMcp
```

Register with an MCP client over stdio, e.g. Claude Code:

```bash
claude mcp add quickchart-local \
  -e QuickChart__BaseUrl=http://localhost:3400 \
  -e "QuickChart__AllowedOutputPatterns__0=^C:\\\\Users\\\\me\\\\charts(\\\\|$)" \
  -- dotnet run --project /abs/path/to/QuickChartMcp
```

## Packaging

This project is set up as an MCP-server NuGet tool (`PackAsTool`, `PackageType=McpServer`).
Build a package with `dotnet pack -c Release`; the `.mcp/server.json` manifest describes the
stdio transport and the `QuickChart__*` environment variables.
